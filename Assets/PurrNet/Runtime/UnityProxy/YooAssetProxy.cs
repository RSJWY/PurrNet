#if YOOASSET_PURRNET_SUPPORT
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Utils;
using UnityEngine;
using YooAsset;

namespace PurrNet
{
    /// <summary>
    /// IL-intercepted proxies for YooAsset <see cref="AssetHandle"/> instantiation.
    /// Spawning semantics mirror <see cref="AddressablesProxy"/>: instantiated GameObjects
    /// with a NetworkIdentity are auto-spawned when their prefab is registered.
    ///
    /// Semantic difference from Addressables: YooAsset has no ReleaseInstance, so there is
    /// intentionally no Release interception here. Destroying an instance goes through
    /// Object.Destroy (already intercepted by UnityProxy -> despawn), and releasing the
    /// AssetHandle itself remains the caller's own responsibility.
    /// </summary>
    [UsedByIL]
    public static class YooAssetProxy
    {
        #region AssetHandle.InstantiateSync - Intercepted

        [UsedByIL]
        public static GameObject InstantiateSync(AssetHandle self)
        {
            var go = InstantiateSyncDirectly(self);
            CheckAutoSpawn(self, go);
            return go;
        }

        [UsedByIL]
        public static GameObject InstantiateSync(AssetHandle self, InstantiateOptions options)
        {
            var go = InstantiateSyncDirectly(self, options);
            CheckAutoSpawn(self, go);
            return go;
        }

        #endregion

        #region AssetHandle.InstantiateAsync - Intercepted

        [UsedByIL]
        public static InstantiateOperation InstantiateAsync(AssetHandle self)
        {
            var op = InstantiateAsyncDirectly(self);
            RegisterAutoSpawn(self, op);
            return op;
        }

        [UsedByIL]
        public static InstantiateOperation InstantiateAsync(AssetHandle self, InstantiateOptions options)
        {
            var op = InstantiateAsyncDirectly(self, options);
            RegisterAutoSpawn(self, op);
            return op;
        }

        #endregion

        #region AssetHandle.InstantiateSync - Directly

        public static GameObject InstantiateSyncDirectly(AssetHandle self)
            => self.InstantiateSync();

        public static GameObject InstantiateSyncDirectly(AssetHandle self, InstantiateOptions options)
            => self.InstantiateSync(options);

        #endregion

        #region AssetHandle.InstantiateAsync - Directly

        public static InstantiateOperation InstantiateAsyncDirectly(AssetHandle self)
            => self.InstantiateAsync();

        public static InstantiateOperation InstantiateAsyncDirectly(AssetHandle self, InstantiateOptions options)
            => self.InstantiateAsync(options);

        #endregion

        #region Internal

        private static void RegisterAutoSpawn(AssetHandle handle, InstantiateOperation op)
        {
            if (op == null)
                return;

            op.Completed += completed => OnInstantiateCompleted(handle, (InstantiateOperation)completed);
        }

        private static void OnInstantiateCompleted(AssetHandle handle, InstantiateOperation op)
        {
            if (op.Status != EOperationStatus.Succeeded)
                return;

            CheckAutoSpawn(handle, op.Result);
        }

        private static void CheckAutoSpawn(AssetHandle handle, GameObject go)
        {
            if (!go)
                return;

            if (!go.GetComponentInChildren<NetworkIdentity>())
                return;

            var manager = NetworkManager.main;

            if (!manager)
                return;

            var prefab = ResolvePrefab(manager, handle);

            if (!prefab)
            {
                PurrLogger.LogWarning(
                    $"AssetHandle.Instantiate created '{go.name}' with a NetworkIdentity, " +
                    "but the prefab could not be resolved for network spawning. " +
                    "Make sure the prefab is registered in NetworkPrefabs or YooAssetNetworkPrefabs.",
                    go);
                return;
            }

            PurrNetGameObjectUtils.NotifyGameObjectCreated(go, prefab);
        }

        private static GameObject ResolvePrefab(NetworkManager manager, AssetHandle handle)
        {
            // Prefer resolving by the loaded asset object reference: YooAsset shares a single
            // AssetObject per resource, making this the most reliable match.
            var provider = manager.prefabProvider;

            if (provider != null && handle.AssetObject is GameObject loadedPrefab &&
                provider.TryGetPrefabData(loadedPrefab, out var providerData))
            {
                return providerData.prefab;
            }

            // Fallback: resolve by encoded persistent key, trying every location form
            // the asset info can provide (address, asset path, extensionless path).
            var yooAssetPrefabs = manager.yooAssetNetworkPrefabs;

            if (!yooAssetPrefabs)
                return null;

            var info = handle.GetAssetInfo();

            if (info == null || !info.IsValid)
                return null;

            if (TryResolveByLocation(yooAssetPrefabs, info.PackageName, info.Address, out var prefab))
                return prefab;

            if (TryResolveByLocation(yooAssetPrefabs, info.PackageName, info.AssetPath, out prefab))
                return prefab;

            var extension = System.IO.Path.GetExtension(info.AssetPath);
            if (!string.IsNullOrEmpty(extension))
            {
                var extensionless = info.AssetPath.Substring(0, info.AssetPath.Length - extension.Length);
                if (TryResolveByLocation(yooAssetPrefabs, info.PackageName, extensionless, out prefab))
                    return prefab;
            }

            return null;
        }

        private static bool TryResolveByLocation(
            YooAssetNetworkPrefabs yooAssetPrefabs,
            string packageName,
            string location,
            out GameObject prefab)
        {
            prefab = null;

            if (string.IsNullOrEmpty(location))
                return false;

            var key = YooAssetNetworkPrefabs.Encode(packageName, location);

            if (!yooAssetPrefabs.TryGetPrefabDataByPersistentId(key, out var data) || !data.prefab)
                return false;

            prefab = data.prefab;
            return true;
        }

        #endregion
    }
}
#endif
