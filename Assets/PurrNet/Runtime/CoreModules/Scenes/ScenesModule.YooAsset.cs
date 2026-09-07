#if YOOASSET_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using PurrNet.Logging;
using UnityEngine.SceneManagement;
using YooAsset;
using YooAssetSceneHandle = YooAsset.SceneHandle;

namespace PurrNet.Modules
{
    public partial class ScenesModule
    {
        private const string YooAssetSceneKeyPrefix = "purrnet-yooasset-v1:";

        public struct PendingYooAssetSceneOperation
        {
            public string packageName;
            public string location;
            public YooAssetSceneHandle handle;
            public SceneID idToAssign;
            public PurrSceneSettings settings;
            public bool ownsHandle;
        }

        private struct YooAssetSceneRegistration
        {
            public string packageName;
            public string location;
            public YooAssetSceneHandle handle;
            public bool ownsHandle;
        }

        private readonly List<PendingYooAssetSceneOperation> _pendingYooAssetOperations =
            new List<PendingYooAssetSceneOperation>();

        private readonly Dictionary<SceneID, YooAssetSceneRegistration> _yooAssetScenes =
            new Dictionary<SceneID, YooAssetSceneRegistration>();

        private readonly Dictionary<string, List<SceneID>> _yooAssetSceneKeyToIds =
            new Dictionary<string, List<SceneID>>();

        private readonly List<UnloadSceneOperation> _pendingYooAssetCleanupUnloads =
            new List<UnloadSceneOperation>();

        public delegate void OnYooAssetSceneEvent(
            SceneID sceneId,
            string packageName,
            string location,
            bool asServer);

        /// <summary>Fired when a YooAsset scene begins loading.</summary>
        public event OnYooAssetSceneEvent onYooAssetSceneStartLoading;

        /// <summary>Fired when a YooAsset scene has finished loading and is registered.</summary>
        public event OnYooAssetSceneEvent onYooAssetSceneLoaded;

        private static string EncodeYooAssetSceneKey(string packageName, string location)
        {
            return $"{YooAssetSceneKeyPrefix}{packageName.Length}:{packageName}{location}";
        }

        private static bool TryDecodeYooAssetSceneKey(
            string key,
            out string packageName,
            out string location)
        {
            packageName = null;
            location = null;

            if (string.IsNullOrEmpty(key) || !key.StartsWith(YooAssetSceneKeyPrefix, StringComparison.Ordinal))
                return false;

            var lengthStart = YooAssetSceneKeyPrefix.Length;
            var separator = key.IndexOf(':', lengthStart);
            if (separator < 0 || !int.TryParse(key.Substring(lengthStart, separator - lengthStart), out var length))
                return false;

            var packageStart = separator + 1;
            if (length <= 0 || packageStart + length > key.Length)
                return false;

            packageName = key.Substring(packageStart, length);
            location = key.Substring(packageStart + length);
            return !string.IsNullOrEmpty(location);
        }

        private static bool IsYooAssetSceneKey(string key)
        {
            return !string.IsNullOrEmpty(key) &&
                   key.StartsWith(YooAssetSceneKeyPrefix, StringComparison.Ordinal);
        }

        private void RegisterYooAssetCompletionCallback(YooAssetSceneHandle handle)
        {
            handle.Completed += _ => ProcessCompletedYooAssetLoads();
        }

        partial void ProcessCompletedYooAssetLoads()
        {
            for (var i = _pendingYooAssetOperations.Count - 1; i >= 0; i--)
            {
                var operation = _pendingYooAssetOperations[i];
                if (!operation.handle.IsDone)
                    continue;

                if (operation.handle.Status == EOperationStatus.Succeeded)
                {
                    var scene = operation.handle.SceneObject;
                    _sceneActionScenes.Add(operation.idToAssign);
                    AddScene(scene, operation.settings, operation.idToAssign);
                    RegisterYooAssetScene(operation);
                    onYooAssetSceneLoaded?.Invoke(
                        operation.idToAssign,
                        operation.packageName,
                        operation.location,
                        _asServer);
                }
                else
                {
                    PurrLogger.LogError(
                        $"YooAsset scene load failed for package '{operation.packageName}', " +
                        $"location '{operation.location}': {operation.handle.Error}");
                }

                _pendingYooAssetOperations.RemoveAt(i);
            }
        }

        private void ProcessLoadYooAssetAction(LoadAddressableSceneAction action)
        {
            var key = action.guid.value;
            if (!TryDecodeYooAssetSceneKey(key, out var packageName, out var location))
            {
                PurrLogger.LogError($"Received an invalid YooAsset scene key: '{key}'");
                return;
            }

            if (_scenes.ContainsKey(action.sceneID) || IsScenePendingYooAsset(action.sceneID))
                return;

            if (action.parameters.mode == LoadSceneMode.Single)
                PrepareForSingleYooAssetSceneLoad();

            YooAssetSceneHandle handle;
            try
            {
                var package = YooAssets.GetPackage(packageName);
                handle = package.LoadSceneAsync(
                    location,
                    action.parameters.mode,
                    action.parameters.physicsMode,
                    true,
                    100);
            }
            catch (Exception exception)
            {
                PurrLogger.LogError(
                    $"Error loading YooAsset scene from package '{packageName}', location '{location}': {exception}");
                return;
            }

            AddPendingYooAssetOperation(packageName, location, handle, action.sceneID, action.parameters, true);
            onYooAssetSceneStartLoading?.Invoke(action.sceneID, packageName, location, _asServer);
        }

        private void AddPendingYooAssetOperation(
            string packageName,
            string location,
            YooAssetSceneHandle handle,
            SceneID sceneId,
            PurrSceneSettings settings,
            bool ownsHandle)
        {
            if (handle == null || !handle.IsValid)
            {
                PurrLogger.LogError(
                    $"YooAsset returned an invalid scene handle for package '{packageName}', location '{location}'");
                return;
            }

            _pendingYooAssetOperations.Add(new PendingYooAssetSceneOperation
            {
                packageName = packageName,
                location = location,
                handle = handle,
                idToAssign = sceneId,
                settings = settings,
                ownsHandle = ownsHandle
            });
            _sceneActionScenes.Add(sceneId);
            RegisterYooAssetCompletionCallback(handle);
        }

        private bool IsScenePendingYooAsset(SceneID sceneId)
        {
            for (var i = 0; i < _pendingYooAssetOperations.Count; i++)
            {
                if (_pendingYooAssetOperations[i].idToAssign == sceneId)
                    return true;
            }

            return false;
        }

        private bool IsYooAssetScenePending(SceneID sceneId, string key)
        {
            for (var i = 0; i < _pendingYooAssetOperations.Count; i++)
            {
                var operation = _pendingYooAssetOperations[i];
                if (operation.idToAssign != sceneId)
                    continue;

                return string.IsNullOrEmpty(key) ||
                       EncodeYooAssetSceneKey(operation.packageName, operation.location) == key;
            }

            return false;
        }

        /// <summary>
        /// Loads a scene through the specified YooAsset package. Only the server can load scenes.
        /// The package must already be created, initialized, and have a valid manifest on every peer.
        /// </summary>
        public YooAssetSceneHandle LoadYooAssetSceneAsync(
            string packageName,
            string location,
            PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes");
                return null;
            }

            if (string.IsNullOrEmpty(packageName))
            {
                PurrLogger.LogError("LoadYooAssetSceneAsync failed: package name is null or empty");
                return null;
            }

            if (string.IsNullOrEmpty(location))
            {
                PurrLogger.LogError("LoadYooAssetSceneAsync failed: location is null or empty");
                return null;
            }

            var idToAssign = GetNextID();
            if (settings.mode == LoadSceneMode.Single)
                PrepareForSingleYooAssetSceneLoad();

            var key = EncodeYooAssetSceneKey(packageName, location);
            _history.AddLoadAddressableAction(new LoadAddressableSceneAction
            {
                guid = key,
                sceneID = idToAssign,
                parameters = settings
            });

            YooAssetSceneHandle handle;
            try
            {
                var package = YooAssets.GetPackage(packageName);
                handle = package.LoadSceneAsync(location, settings.mode, settings.physicsMode, true, 100);
            }
            catch (Exception exception)
            {
                PurrLogger.LogError(
                    $"Error loading YooAsset scene from package '{packageName}', location '{location}': {exception}");
                return null;
            }

            AddPendingYooAssetOperation(packageName, location, handle, idToAssign, settings, true);

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule.AddPendingYooAssetOperation(
                    packageName,
                    location,
                    handle,
                    idToAssign,
                    settings,
                    false);
            }

            onYooAssetSceneStartLoading?.Invoke(idToAssign, packageName, location, _asServer);
            return handle;
        }

        /// <summary>
        /// Loads a scene through the specified YooAsset package using the location-first argument order.
        /// </summary>
        public YooAssetSceneHandle LoadYooAssetSceneAsync(
            string location,
            PurrSceneSettings settings,
            string packageName)
        {
            return LoadYooAssetSceneAsync(packageName, location, settings);
        }

        private void PrepareForSingleYooAssetSceneLoad()
        {
            if (TryGetSceneID(_networkManager.gameObject.scene, out var managerSceneId) &&
                TryGetSceneState(managerSceneId, out var managerScene) &&
                !IsDontDestroyOnLoadScene(managerScene.scene))
            {
                PurrLogger.LogError(
                    "Network manager scene is not DontDestroyOnLoad and you are trying to load a new scene " +
                    "with LoadSceneMode.Single");
            }

            var yooAssetIds = new List<SceneID>(_yooAssetScenes.Keys);
            for (var i = yooAssetIds.Count - 1; i >= 0; i--)
            {
                var id = yooAssetIds[i];
                if (_scenes.TryGetValue(id, out var state) && !IsDontDestroyOnLoadScene(state.scene))
                    TryRemoveYooAssetScene(id, true, out _);
            }

            for (var i = _pendingYooAssetOperations.Count - 1; i >= 0; i--)
            {
                var operation = _pendingYooAssetOperations[i];
                if (operation.ownsHandle && operation.handle != null && operation.handle.IsValid)
                    operation.handle.UnloadSceneAsync();
                _pendingYooAssetOperations.RemoveAt(i);
            }

            for (var i = _rawScenes.Count - 1; i >= 0; i--)
            {
                var state = _scenes[_rawScenes[i]];
                if (!IsDontDestroyOnLoadScene(state.scene))
                    RemoveScene(state.scene);
            }
        }

        /// <summary>Returns the pending YooAsset scene load operations.</summary>
        public IReadOnlyList<PendingYooAssetSceneOperation> GetPendingYooAssetOperations()
        {
            return _pendingYooAssetOperations;
        }

        public bool IsYooAssetScene(SceneID sceneId)
        {
            return _yooAssetScenes.ContainsKey(sceneId);
        }

        public bool IsYooAssetSceneLoaded(string packageName, string location)
        {
            var key = EncodeYooAssetSceneKey(packageName, location);
            if (_yooAssetSceneKeyToIds.TryGetValue(key, out var ids) && ids.Count > 0)
                return true;

            return IsYooAssetSceneLoading(packageName, location);
        }

        public bool IsYooAssetSceneLoading(string packageName, string location)
        {
            for (var i = 0; i < _pendingYooAssetOperations.Count; i++)
            {
                var operation = _pendingYooAssetOperations[i];
                if (operation.packageName == packageName && operation.location == location)
                    return true;
            }

            return false;
        }

        public bool TryGetSceneIdByYooAssetLocation(
            string packageName,
            string location,
            out SceneID sceneId)
        {
            var key = EncodeYooAssetSceneKey(packageName, location);
            if (_yooAssetSceneKeyToIds.TryGetValue(key, out var ids) && ids.Count > 0)
            {
                sceneId = ids[0];
                return true;
            }

            sceneId = default;
            return false;
        }

        public IReadOnlyList<SceneID> GetSceneIdsByYooAssetLocation(string packageName, string location)
        {
            var key = EncodeYooAssetSceneKey(packageName, location);
            return _yooAssetSceneKeyToIds.TryGetValue(key, out var ids)
                ? ids
                : Array.Empty<SceneID>();
        }

        public int UnloadYooAssetSceneByLocation(
            string packageName,
            string location,
            UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return 0;
            }

            var ids = GetSceneIdsByYooAssetLocation(packageName, location);
            var copy = new List<SceneID>(ids);
            for (var i = copy.Count - 1; i >= 0; i--)
                UnloadYooAssetSceneAsync(copy[i], options);

            return copy.Count;
        }

        /// <summary>Unloads a YooAsset scene by its network SceneID.</summary>
        public UnloadSceneOperation UnloadYooAssetSceneAsync(
            SceneID sceneId,
            UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return null;
            }

            if (!IsYooAssetScene(sceneId))
            {
                PurrLogger.LogError($"Scene with ID {sceneId} is not a loaded YooAsset scene");
                return null;
            }

            if (_scenes.TryGetValue(sceneId, out var state) && _networkManager.gameObject.scene == state.scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return null;
            }

            _history.AddUnloadAction(new UnloadSceneAction { sceneID = sceneId, options = options });
            TryRemoveYooAssetScene(sceneId, false, out var operation);
            return operation;
        }

        private bool TryUnloadYooAssetScene(SceneID sceneId, UnloadSceneOptions options)
        {
            return TryRemoveYooAssetScene(sceneId, false, out _);
        }

        private bool TryRemoveYooAssetScene(
            SceneID sceneId,
            bool playUnloadEventsImmediately,
            out UnloadSceneOperation unloadOperation)
        {
            unloadOperation = null;
            if (!_yooAssetScenes.TryGetValue(sceneId, out var registration))
                return false;

            var hasState = _scenes.TryGetValue(sceneId, out var state);
            if (registration.ownsHandle && registration.handle != null && registration.handle.IsValid)
                unloadOperation = registration.handle.UnloadSceneAsync();

            UnregisterYooAssetScene(sceneId);
            if (hasState)
                RemoveScene(state.scene, playUnloadEventsImmediately);

            return true;
        }

        private void RegisterYooAssetScene(PendingYooAssetSceneOperation operation)
        {
            var registration = new YooAssetSceneRegistration
            {
                packageName = operation.packageName,
                location = operation.location,
                handle = operation.handle,
                ownsHandle = operation.ownsHandle
            };

            _yooAssetScenes[operation.idToAssign] = registration;
            RegisterYooAssetSceneKey(operation.idToAssign, operation.packageName, operation.location);
        }

        private void RegisterYooAssetSceneKey(SceneID sceneId, string packageName, string location)
        {
            var key = EncodeYooAssetSceneKey(packageName, location);
            if (!_yooAssetSceneKeyToIds.TryGetValue(key, out var ids))
            {
                ids = new List<SceneID>();
                _yooAssetSceneKeyToIds[key] = ids;
            }

            if (!ids.Contains(sceneId))
                ids.Add(sceneId);
        }

        private void UnregisterYooAssetScene(SceneID sceneId)
        {
            if (!_yooAssetScenes.TryGetValue(sceneId, out var registration))
                return;

            _yooAssetScenes.Remove(sceneId);
            var key = EncodeYooAssetSceneKey(registration.packageName, registration.location);
            if (!_yooAssetSceneKeyToIds.TryGetValue(key, out var ids))
                return;

            ids.Remove(sceneId);
            if (ids.Count == 0)
                _yooAssetSceneKeyToIds.Remove(key);
        }

        private bool TryReconcileLoadedYooAssetTransferScene(
            LoadAddressableSceneAction loadAction,
            ICollection<SceneID> replayLoadEvents)
        {
            var key = loadAction.guid.value;
            if (!TryDecodeYooAssetSceneKey(key, out var packageName, out var location))
                return false;

            if (_scenes.TryGetValue(loadAction.sceneID, out var existing))
            {
                if (IsLoadedYooAssetScene(loadAction.sceneID, key, existing))
                {
                    _scenes[loadAction.sceneID] = new SceneState(existing.scene, loadAction.parameters);
                    _sceneActionScenes.Add(loadAction.sceneID);
                    RegisterYooAssetSceneKey(loadAction.sceneID, packageName, location);
                    replayLoadEvents.Add(loadAction.sceneID);
                    return true;
                }

                RemoveExistingYooAssetTransferScene(loadAction.sceneID, existing);
            }

            if (!_yooAssetSceneKeyToIds.TryGetValue(key, out var sceneIds))
                return false;

            var ids = new List<SceneID>(sceneIds);
            for (var i = 0; i < ids.Count; i++)
            {
                var oldId = ids[i];
                if (!_scenes.TryGetValue(oldId, out var state) || !state.scene.IsValid() || !state.scene.isLoaded)
                    continue;

                MoveYooAssetSceneRegistration(oldId, loadAction.sceneID);
                BindLoadedTransferScene(state.scene, loadAction.parameters, loadAction.sceneID);
                replayLoadEvents.Add(loadAction.sceneID);
                return true;
            }

            return false;
        }

        private bool IsLoadedYooAssetScene(SceneID sceneId, string key, SceneState state)
        {
            if (!state.scene.IsValid() || !state.scene.isLoaded ||
                !_yooAssetScenes.TryGetValue(sceneId, out var registration))
                return false;

            return EncodeYooAssetSceneKey(registration.packageName, registration.location) == key;
        }

        private void MoveYooAssetSceneRegistration(SceneID oldId, SceneID newId)
        {
            if (!_yooAssetScenes.TryGetValue(oldId, out var registration))
                return;

            UnregisterYooAssetScene(oldId);
            _yooAssetScenes[newId] = registration;
            RegisterYooAssetSceneKey(newId, registration.packageName, registration.location);
        }

        private void RemoveExistingYooAssetTransferScene(SceneID sceneId, SceneState state)
        {
            if (TryRemoveYooAssetScene(sceneId, true, out _))
                return;

            RemoveScene(state.scene, true);
            if (!ShouldKeepLocalSceneDuringTransfer(state.scene) && state.scene.IsValid() && state.scene.isLoaded)
                SceneManager.UnloadSceneAsync(state.scene);
        }

        private void RemoveStaleYooAssetTransferScenes(IReadOnlyDictionary<SceneID, string> targetScenes)
        {
            for (var i = _pendingYooAssetOperations.Count - 1; i >= 0; i--)
            {
                var operation = _pendingYooAssetOperations[i];
                var key = EncodeYooAssetSceneKey(operation.packageName, operation.location);
                if (targetScenes.TryGetValue(operation.idToAssign, out var targetKey) && key == targetKey)
                    continue;

                if (operation.ownsHandle && operation.handle != null && operation.handle.IsValid)
                    operation.handle.UnloadSceneAsync();
                _pendingYooAssetOperations.RemoveAt(i);
            }

            var ids = new List<SceneID>(_yooAssetScenes.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var registration = _yooAssetScenes[id];
                var key = EncodeYooAssetSceneKey(registration.packageName, registration.location);
                if (targetScenes.TryGetValue(id, out var targetKey) && key == targetKey)
                    continue;

                TryRemoveYooAssetScene(id, true, out _);
            }
        }

        partial void RebuildYooAssetHistoryFromLoadedScenes()
        {
            for (var i = 0; i < _rawScenes.Count; i++)
            {
                var id = _rawScenes[i];
                if (!_sceneActionScenes.Contains(id) || !_scenes.TryGetValue(id, out var state) ||
                    !state.scene.IsValid() || !state.scene.isLoaded ||
                    !_yooAssetScenes.TryGetValue(id, out var registration))
                    continue;

                _history.AddLoadAddressableAction(new LoadAddressableSceneAction
                {
                    guid = EncodeYooAssetSceneKey(registration.packageName, registration.location),
                    sceneID = id,
                    parameters = state.settings
                });
            }
        }

        private bool TryCleanupYooAssetScene(SceneID sceneId, bool keepNetworkManager)
        {
            if (!_yooAssetScenes.ContainsKey(sceneId))
                return false;

            if (keepNetworkManager && _scenes.TryGetValue(sceneId, out var state) &&
                _networkManager.gameObject.scene.handle == state.scene.handle)
                return false;

            if (TryRemoveYooAssetScene(sceneId, false, out var operation) && operation != null)
                _pendingYooAssetCleanupUnloads.Add(operation);
            return true;
        }

        private bool UnloadAllYooAssetScenesCleanup(bool keepNetworkManager)
        {
            var ids = new List<SceneID>(_yooAssetScenes.Keys);
            for (var i = 0; i < ids.Count; i++)
                TryCleanupYooAssetScene(ids[i], keepNetworkManager);

            return AreYooAssetCleanupUnloadsDone();
        }

        private bool AreYooAssetCleanupUnloadsDone()
        {
            for (var i = _pendingYooAssetCleanupUnloads.Count - 1; i >= 0; i--)
            {
                if (!_pendingYooAssetCleanupUnloads[i].IsDone)
                    return false;
                _pendingYooAssetCleanupUnloads.RemoveAt(i);
            }

            return true;
        }
    }
}
#endif
