#if YOOASSET_PURRNET_SUPPORT
using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

/// <summary>
/// YooAsset variant of <see cref="AddressableSceneTransferScenario"/>: loads the same target scene
/// through a YooAsset editor-simulated package, spawns the same runtime prefab pair in it, and
/// verifies client sync plus victim reconnect restore. Only runs in the editor (simulate mode);
/// player builds skip it.
/// </summary>
public class YooAssetSceneTransferScenario : Scenario
{
    private const string PackageName = "PurrNetTests";
    private const string TargetSceneLocation = "AddressableSceneTransferTarget";
    private const string TargetSceneName = "AddressableSceneTransferTarget";
    private const int ExpectedChildren = 1;

    [SerializeField] private float _packageTimeoutSeconds = 30f;
    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _transferCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _initialObservedCount;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private AddressableSceneTransferRoot _prefab;

#if UNITY_EDITOR
    private UniTask<ScenarioResult>? _packageInitTask;
    private ScenarioResult? _packageReady;

    // UniTask can only be awaited once; on Host both the client and server phase
    // run concurrently, so cache the outcome of the shared package init.
    private async UniTask<ScenarioResult> AwaitPackageReady()
    {
        if (_packageReady.HasValue)
            return _packageReady.Value;

        var result = await _packageInitTask.Value;
        _packageReady = result;
        return result;
    }
#endif

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(AddressableSceneTransferRoot));
        _prefab = rootGo.AddComponent<AddressableSceneTransferRoot>();

        var childGo = new GameObject(nameof(AddressableSceneTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<AddressableSceneTransferChild>();

        var identities = rootGo.GetComponentsInChildren<NetworkIdentity>(true);
        for (int i = 0; i < identities.Length; i++)
            identities[i].skipSceneAutoSpawning = true;

        rootGo.SetActive(false);
        AddressableSceneTransferRoot.ResetAll();
        AddressableSceneTransferChild.ResetAll();
        _victimId = 0;
        _victimReceived = false;
        _transferCommandReceived = false;
        _phaseDoneReceived = false;
        _initialObservedCount = 0;
        _victimReturnedCount = 0;
        _doneCount = 0;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);

#if UNITY_EDITOR
        // Fire-and-forget: Setup runs before any scenario starts, so the package is
        // ready long before the server loads the scene or clients receive the action.
        _packageInitTask = InitializePackageAsync(ctx);
#endif
    }

#if UNITY_EDITOR
    private async UniTask<ScenarioResult> InitializePackageAsync(ScenarioContext ctx)
    {
        try
        {
            // Package APIs require the global YooAssets runtime to be initialized first.
            if (!YooAssets.IsInitialized)
                YooAssets.Initialize();

            if (YooAssets.TryGetPackage(PackageName, out var existing) &&
                existing.InitializeStatus == EOperationStatus.Succeeded)
                return ScenarioResult.Ok();

            var buildResult = EditorSimulateBuildInvoker.Build(PackageName, (int)EBundleType.VirtualAssetBundle);
            var package = existing != null ? existing : YooAssets.CreatePackage(PackageName);
            var init = package.InitializePackageAsync(new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory)
            });

            // YooAsset 3.x init is only the file system; the manifest is loaded in two
            // extra steps (version request + manifest load) before any asset can be used.
            var result = await WaitPackageStep(init, "initialize", ctx);
            if (!result.success)
                return result;

            var requestVersion = package.RequestPackageVersionAsync();
            result = await WaitPackageStep(requestVersion, "version request", ctx);
            if (!result.success)
                return result;

            var loadManifest = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(requestVersion.PackageVersion, 60));
            return await WaitPackageStep(loadManifest, "manifest load", ctx);
        }
        catch (Exception exception)
        {
            return ScenarioResult.Fail($"yooasset package '{PackageName}' init error: {exception}");
        }
    }

    private async UniTask<ScenarioResult> WaitPackageStep(AsyncOperationBase op, string step, ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => op.IsDone, _packageTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"yooasset package '{PackageName}' {step} timeout");
        }

        return op.Status == EOperationStatus.Succeeded
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail($"yooasset package '{PackageName}' {step} failed: {op.Error}");
    }
#endif

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
#if UNITY_EDITOR
        return RunSplit(ctx, RunAsClient, RunAsServer);
#else
        return UniTask.FromResult(
            ScenarioResult.Ok("skipped: YooAsset editor simulate mode is not available in players"));
#endif
    }

#if UNITY_EDITOR
    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var packageReady = await AwaitPackageReady();
        if (!packageReady.success)
            return packageReady;

        var victim = PickNonHostClient(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("yooasset transfer: no eligible non-server / non-host client");

        BroadcastVictim(victim.Value.id.value);

        var load = await LoadYooAssetTarget(ctx);
        if (!load.success)
            return load;

        if (!TryGetYooAssetTargetScene(ctx, out var targetScene))
            return ScenarioResult.Fail($"yooasset transfer: target scene not registered after load: {DescribeState(ctx)}");

        SpawnInScene(targetScene);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AddressableSceneTransferRoot.ServerAliveCount == 1
                      && AddressableSceneTransferChild.ServerAliveCount == ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"yooasset transfer server spawn timeout: {DescribeState(ctx)}");
        }

        if (AddressableSceneTransferRoot.SawBadId || AddressableSceneTransferChild.SawBadId)
            return ScenarioResult.Fail($"yooasset transfer server spawn saw default id: {DescribeState(ctx)}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _initialObservedCount >= ctx.expectedConnections,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"yooasset transfer initial observation timeout: got {_initialObservedCount}/{ctx.expectedConnections}; {DescribeState(ctx)}");
        }

        BroadcastTransferCommand();

        var failures = string.Empty;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReturnedCount >= 1,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures = $"yooasset transfer victim did not reconnect and restore: {DescribeState(ctx)}";
        }

        BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _doneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            var message = $"yooasset transfer done timeout: got {_doneCount}/{ctx.expectedConnections}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok($"victim={victim.Value.id.value}")
            : ScenarioResult.Fail(failures);
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var packageReady = await AwaitPackageReady();
        if (!packageReady.success)
            return packageReady;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReceived,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"yooasset transfer victim id timeout: {DescribeState(ctx)}");
        }

        var initial = await WaitForClientScene(ctx, "yooasset transfer initial", false, 0, 0);
        if (!initial.success)
            return initial;

        SignalInitialObserved();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _transferCommandReceived,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"yooasset transfer command timeout: {DescribeState(ctx)}");
        }

        var failures = string.Empty;
        if (IsLocalVictim(ctx))
        {
            int rootSpawnsBeforeTransfer = AddressableSceneTransferRoot.ClientSpawnCount;
            int childSpawnsBeforeTransfer = AddressableSceneTransferChild.ClientSpawnCount;

            ctx.networkManager.TransferToNewServer();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => ctx.networkManager.isClient && ctx.networkManager.isLocalPlayerReady,
                    _transferTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures = $"yooasset transfer reconnect timeout: {DescribeState(ctx)}";
            }

            if (string.IsNullOrEmpty(failures))
            {
                var restored = await WaitForClientScene(
                    ctx,
                    "yooasset transfer restore",
                    true,
                    rootSpawnsBeforeTransfer,
                    childSpawnsBeforeTransfer);
                if (!restored.success)
                    failures = restored.message;
            }

            if (string.IsNullOrEmpty(failures))
                SignalVictimReturned();
        }
        else
        {
            var retained = await WaitForClientScene(ctx, "yooasset transfer non-victim retained", false, 0, 0);
            if (!retained.success)
                failures = retained.message;
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _phaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            var message = $"yooasset transfer phase done timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(IsLocalVictim(ctx) ? "victim yooasset transfer restored" : "non-victim retained")
            : ScenarioResult.Fail(failures);
    }

    private async UniTask<ScenarioResult> LoadYooAssetTarget(ScenarioContext ctx)
    {
        var handle = ctx.networkManager.sceneModule.LoadYooAssetSceneAsync(PackageName, TargetSceneLocation, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (handle == null)
            return ScenarioResult.Fail($"yooasset transfer: LoadYooAssetSceneAsync returned null handle for '{PackageName}:{TargetSceneLocation}'");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => handle.IsDone
                      && handle.Status == EOperationStatus.Succeeded
                      && IsNetworkYooAssetTargetLoaded(ctx),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"yooasset transfer scene load timeout (done={handle.IsDone}, status={handle.Status}, error={handle.Error}): {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private AddressableSceneTransferRoot SpawnInScene(Scene targetScene)
    {
        var previous = SceneManager.GetActiveScene();
        bool changed = SceneManager.SetActiveScene(targetScene);
        try
        {
            return Instantiate(_prefab);
        }
        finally
        {
            if (changed && previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
        }
    }

    private async UniTask<ScenarioResult> WaitForClientScene(
        ScenarioContext ctx,
        string phase,
        bool requireFreshSpawn,
        int rootSpawnsBefore,
        int childSpawnsBefore)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AddressableSceneTransferRoot.ClientAliveCount == 1
                      && AddressableSceneTransferChild.ClientAliveCount == ExpectedChildren
                      && AddressableSceneTransferRoot.ClientSceneName == TargetSceneName
                      && IsNetworkYooAssetTargetLoaded(ctx)
                      && ctx.networkManager.isLocalPlayerReady
                      && (!requireFreshSpawn ||
                          (AddressableSceneTransferRoot.ClientSpawnCount > rootSpawnsBefore
                           && AddressableSceneTransferChild.ClientSpawnCount > childSpawnsBefore)),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (AddressableSceneTransferRoot.SawBadId || AddressableSceneTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private static bool IsNetworkYooAssetTargetLoaded(ScenarioContext ctx)
    {
        return IsYooAssetTargetSceneLoaded()
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsYooAssetSceneLoaded(PackageName, TargetSceneLocation)
               && ctx.networkManager.sceneModule.TryGetSceneIdByYooAssetLocation(PackageName, TargetSceneLocation, out _);
    }

    private static bool TryGetYooAssetTargetScene(ScenarioContext ctx, out Scene scene)
    {
        scene = SceneManager.GetSceneByName(TargetSceneName);
        return scene.IsValid()
               && scene.isLoaded
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.TryGetSceneIdByYooAssetLocation(PackageName, TargetSceneLocation, out _);
    }

    private static bool IsYooAssetTargetSceneLoaded()
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static bool IsLocalVictim(ScenarioContext ctx)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == _victimId;
    }

    private static PlayerID? PickNonHostClient(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? best = null;
        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;
            if (!best.HasValue || player.id.value < best.Value.id.value)
                best = player;
        }

        return best;
    }

    private static string DescribeState(ScenarioContext ctx)
    {
        return $"role={ctx.role}, victim={_victimId}, victimReceived={_victimReceived}, " +
               $"transfer={_transferCommandReceived}, phaseDone={_phaseDoneReceived}, " +
               $"initial={_initialObservedCount}, returned={_victimReturnedCount}, done={_doneCount}, " +
               $"clientState={ctx.networkManager.clientState}, serverState={ctx.networkManager.serverState}, " +
               $"client={ctx.networkManager.isClient}, server={ctx.networkManager.isServer}, ready={ctx.networkManager.isLocalPlayerReady}, " +
               $"clientRoots={AddressableSceneTransferRoot.ClientAliveCount}, " +
               $"clientChildren={AddressableSceneTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={AddressableSceneTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={AddressableSceneTransferChild.ClientSpawnCount}, " +
               $"clientScene={AddressableSceneTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={AddressableSceneTransferRoot.ServerAliveCount}, " +
               $"serverChildren={AddressableSceneTransferChild.ServerAliveCount}, " +
               $"rootBadId={AddressableSceneTransferRoot.SawBadId}, childBadId={AddressableSceneTransferChild.SawBadId}, " +
               $"sceneLoaded={IsYooAssetTargetSceneLoaded()}, " +
               $"networkSceneLoaded={IsNetworkYooAssetTargetLoaded(ctx)}, " +
               $"yooAssetLoading={ctx.networkManager.sceneModule != null && ctx.networkManager.sceneModule.IsYooAssetSceneLoading(PackageName, TargetSceneLocation)}";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastVictim(ulong victimId)
    {
        _victimId = victimId;
        _victimReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastTransferCommand()
    {
        _transferCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhaseDone()
    {
        _phaseDoneReceived = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalInitialObserved(RPCInfo info = default)
    {
        _initialObservedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalVictimReturned(RPCInfo info = default)
    {
        _victimReturnedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone(RPCInfo info = default)
    {
        _doneCount++;
    }
#endif
}
#endif
