#if YOOASSET_PURRNET_SUPPORT
using System;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet
{
    public sealed partial class NetworkManager
    {
        /// <summary>
        /// Spawns a networked object from a pre-loaded YooAsset prefab.
        /// The package/location pair must match an entry in the assigned YooAssetNetworkPrefabs asset.
        /// The prefabs must be loaded (via YooAssetNetworkPrefabs.LoadAllAsync) before calling this.
        /// </summary>
        /// <param name="packageName">The YooAsset package containing the prefab.</param>
        /// <param name="location">The location of the prefab inside the package.</param>
        /// <param name="position">World position for the spawned object.</param>
        /// <param name="rotation">Rotation for the spawned object.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <returns>The spawned GameObject, or null if spawning failed.</returns>
        public GameObject SpawnYooAsset(
            string packageName,
            string location,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(location))
            {
                PurrLogger.LogError("SpawnYooAsset failed: package name or location is null or empty.");
                return null;
            }

            var key = YooAssetNetworkPrefabs.Encode(packageName, location);

            if (!_yooAssetNetworkPrefabs)
            {
                PurrLogger.LogError("SpawnYooAsset failed: No YooAssetNetworkPrefabs assigned on NetworkManager.");
                return null;
            }

            if (!_yooAssetNetworkPrefabs.TryGetPrefabDataByKey(key, out var localData))
            {
                PurrLogger.LogError($"SpawnYooAsset failed: No YooAsset prefab registered with key '{key}'.");
                return null;
            }

            if (!localData.prefab)
            {
                PurrLogger.LogError($"SpawnYooAsset failed: YooAsset prefab with key '{key}' is registered but not loaded. Use SpawnYooAssetAsync and await it.");
                return null;
            }

            return InstantiateAndSpawnYooAsset(localData.prefab, position, rotation, parent);
        }

        /// <summary>
        /// Spawns a networked object from a YooAsset prefab, loading it first if needed.
        /// The package/location pair must match an entry in the assigned YooAssetNetworkPrefabs asset.
        /// </summary>
        /// <param name="packageName">The YooAsset package containing the prefab.</param>
        /// <param name="location">The location of the prefab inside the package.</param>
        /// <param name="position">World position for the spawned object.</param>
        /// <param name="rotation">Rotation for the spawned object.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <returns>The spawned GameObject, or null if spawning failed.</returns>
        public async Task<GameObject> SpawnYooAssetAsync(
            string packageName,
            string location,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(location))
            {
                PurrLogger.LogError("SpawnYooAssetAsync failed: package name or location is null or empty.");
                return null;
            }

            var key = YooAssetNetworkPrefabs.Encode(packageName, location);

            if (!_yooAssetNetworkPrefabs)
            {
                PurrLogger.LogError("SpawnYooAssetAsync failed: No YooAssetNetworkPrefabs assigned on NetworkManager.");
                return null;
            }

            try
            {
                var prefabData = await _yooAssetNetworkPrefabs.LoadPrefabByKeyAsync(key);
                if (prefabData.prefab == null)
                {
                    PurrLogger.LogError($"SpawnYooAssetAsync failed: could not load YooAsset prefab key '{key}'.");
                    return null;
                }

                return InstantiateAndSpawnYooAsset(prefabData.prefab, position, rotation, parent);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"SpawnYooAssetAsync failed for key '{key}': {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        private GameObject InstantiateAndSpawnYooAsset(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            var instance = parent
                ? UnityProxy.Instantiate(prefab, position, rotation, parent)
                : UnityProxy.Instantiate(prefab, position, rotation);

            if (!instance)
                return null;

            if (instance.TryGetComponent(out NetworkIdentity identity) && !identity.IsSpawned(isServer))
                Spawn(instance);

            return instance;
        }

        /// <summary>
        /// Despawns a networked YooAsset object.
        /// The instance is destroyed through the network pipeline; releasing the
        /// YooAsset handle remains the caller's responsibility (YooAsset has no
        /// ReleaseInstance equivalent).
        /// </summary>
        /// <param name="instance">The spawned instance to despawn.</param>
        public void DespawnYooAsset(GameObject instance)
        {
            if (!instance)
                return;

            UnityProxy.Destroy(instance);
        }
    }
}
#endif
