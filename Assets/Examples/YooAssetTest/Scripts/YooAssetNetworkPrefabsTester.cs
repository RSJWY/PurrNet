#if YOOASSET_PURRNET_SUPPORT
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet;
using UnityEngine;
using YooAsset;

namespace YooAssetTest
{
    /// <summary>
    /// Tests the YooAssetNetworkPrefabs integration on a running server/host:
    /// - SpawnYooAssetAsync / DespawnYooAsset (registry API path)
    /// - AssetHandle.InstantiateSync / InstantiateAsync (IL-intercepted auto-spawn path)
    /// Requires YooAssetTestBootstrap to have initialized the package, and a
    /// YooAssetNetworkPrefabs registry assigned on the NetworkManager whose entries
    /// cover Package Name + Prefab Location.
    /// </summary>
    public sealed class YooAssetNetworkPrefabsTester : NetworkIdentity
    {
        [SerializeField] private string _packageName = "PurrNetTests";
        [SerializeField] private string _prefabLocation = "TestYooAssetNetworkPrefab";

        private readonly List<GameObject> _spawned = new();
        private readonly List<AssetHandle> _handles = new();

        [PurrButton, ContextMenu("Spawn via SpawnYooAssetAsync")]
        private async void SpawnViaRegistry()
        {
            if (!NetworkManager.main || !NetworkManager.main.isServer)
            {
                Debug.LogError("Only the server can network-spawn this test prefab.");
                return;
            }

            var go = await NetworkManager.main.SpawnYooAssetAsync(
                _packageName, _prefabLocation, Random.insideUnitSphere * 3f, Quaternion.identity);
            if (!go)
                return;

            _spawned.Add(go);
            Debug.Log($"[YooAsset] SpawnYooAssetAsync spawned '{go.name}'. Total: {_spawned.Count}", go);
        }

        [PurrButton, ContextMenu("Despawn last (DespawnYooAsset)")]
        private void DespawnLast()
        {
            if (_spawned.Count == 0)
                return;

            var go = _spawned[^1];
            _spawned.RemoveAt(_spawned.Count - 1);
            NetworkManager.main.DespawnYooAsset(go);
            Debug.Log($"[YooAsset] Despawned. Remaining: {_spawned.Count}");
        }

        [PurrButton, ContextMenu("InstantiateSync via handle (auto-spawn)")]
        private async void InstantiateSyncAutoSpawn()
        {
            var handle = await LoadHandle();
            if (handle == null)
                return;

            // This call is IL-rewritten to YooAssetProxy.InstantiateSync, which network-spawns
            // the instance automatically because the prefab is registered in the registry.
            var go = handle.InstantiateSync(new InstantiateOptions(
                true, Random.insideUnitSphere * 3f, Quaternion.identity));
            if (!go)
            {
                handle.Release();
                return;
            }

            _handles.Add(handle);
            _spawned.Add(go);
            Debug.Log($"[YooAsset] InstantiateSync auto-spawned '{go.name}'. Total: {_spawned.Count}", go);
        }

        [PurrButton, ContextMenu("InstantiateAsync via handle (auto-spawn)")]
        private async void InstantiateAsyncAutoSpawn()
        {
            var handle = await LoadHandle();
            if (handle == null)
                return;

            // This call is IL-rewritten to YooAssetProxy.InstantiateAsync, which network-spawns
            // the instance automatically once the operation completes.
            var op = handle.InstantiateAsync(new InstantiateOptions(
                true, Random.insideUnitSphere * 3f, Quaternion.identity));
            await op;
            if (op.Status != EOperationStatus.Succeeded || !op.Result)
            {
                handle.Release();
                Debug.LogError($"[YooAsset] InstantiateAsync failed: {op.Error}");
                return;
            }

            _handles.Add(handle);
            _spawned.Add(op.Result);
            Debug.Log($"[YooAsset] InstantiateAsync auto-spawned '{op.Result.name}'. Total: {_spawned.Count}", op.Result);
        }

        [PurrButton, ContextMenu("Destroy last (auto-despawn)")]
        private void DestroyLast()
        {
            if (_spawned.Count == 0)
                return;

            var go = _spawned[^1];
            _spawned.RemoveAt(_spawned.Count - 1);

            // Object.Destroy is IL-rewritten to UnityProxy.Destroy, which despawns spawned instances.
            if (go)
                UnityProxy.Destroy(go);

            if (_handles.Count > 0)
            {
                var handle = _handles[^1];
                _handles.RemoveAt(_handles.Count - 1);
                if (handle is { IsValid: true })
                    handle.Release();
            }

            Debug.Log($"[YooAsset] Destroyed last. Remaining: {_spawned.Count}");
        }

        private async Task<AssetHandle> LoadHandle()
        {
            if (!NetworkManager.main || !NetworkManager.main.isServer)
            {
                Debug.LogError("Only the server can network-spawn this test prefab.");
                return null;
            }

            if (!YooAssetTestBootstrap.isPackageReady)
            {
                Debug.LogError("YooAsset package is not initialized yet; wait for the bootstrap log.");
                return null;
            }

            var package = YooAssets.GetPackage(_packageName);
            var handle = package.LoadAssetAsync<GameObject>(_prefabLocation);
            await handle;
            if (handle.Status != EOperationStatus.Succeeded || !handle.AssetObject)
            {
                Debug.LogError($"[YooAsset] Failed to load '{_packageName}/{_prefabLocation}': {handle.Error}");
                handle.Release();
                return null;
            }

            return handle;
        }

        protected override void OnDestroy()
        {
            for (var i = 0; i < _handles.Count; i++)
            {
                if (_handles[i] is { IsValid: true })
                    _handles[i].Release();
            }

            _handles.Clear();
            base.OnDestroy();
        }
    }
}
#endif
