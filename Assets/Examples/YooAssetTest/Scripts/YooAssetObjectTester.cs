#if YOOASSET_PURRNET_SUPPORT
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using YooAsset;

namespace YooAssetTest
{
    /// <summary>Tests YooAsset prefab loading, local instantiation, network spawning and RPCs.</summary>
    public sealed class YooAssetObjectTester : NetworkIdentity
    {
        [SerializeField] private string _packageName = "DefaultPackage";
        [SerializeField] private string _prefabLocation;

        private readonly List<AssetHandle> _networkAssetHandles = new List<AssetHandle>();
        private readonly List<GameObject> _networkSpawned = new List<GameObject>();
        private readonly List<AssetHandle> _localAssetHandles = new List<AssetHandle>();
        private readonly List<GameObject> _localInstances = new List<GameObject>();
        private readonly List<AssetHandle> _referenceHandles = new List<AssetHandle>();

        [PurrButton, ContextMenu("Load and network spawn prefab")]
        private async void SpawnByLocation()
        {
            if (!NetworkManager.main || !NetworkManager.main.isServer)
            {
                Debug.LogError("Only the server can network-spawn this test prefab.");
                return;
            }

            var package = GetPackage();
            if (package == null)
                return;

            var handle = package.LoadAssetAsync<GameObject>(_prefabLocation);
            await handle;
            if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
            {
                Debug.LogError($"Failed to load YooAsset prefab: {_packageName}/{_prefabLocation}");
                return;
            }

            var prefab = handle.GetAssetObject<GameObject>();
            var go = handle.InstantiateSync(new InstantiateOptions(true, Random.insideUnitSphere * 3f, Quaternion.identity));
            if (!go)
            {
                handle.Release();
                return;
            }

            NetworkIdentity.Spawn(go, prefab, NetworkManager.main);
            _networkAssetHandles.Add(handle);
            _networkSpawned.Add(go);
            Debug.Log($"Network spawned YooAsset prefab '{go.name}'. Total: {_networkSpawned.Count}", go);
        }

        [PurrButton, ContextMenu("Despawn last prefab")]
        private void DespawnLast()
        {
            if (_networkSpawned.Count == 0)
                return;

            var index = _networkSpawned.Count - 1;
            var go = _networkSpawned[index];
            _networkSpawned.RemoveAt(index);
            if (go && go.TryGetComponent<NetworkIdentity>(out var identity))
                identity.Despawn();

            if (index < _networkAssetHandles.Count)
            {
                _networkAssetHandles[index].Release();
                _networkAssetHandles.RemoveAt(index);
            }

            Debug.Log($"Despawned YooAsset prefab. Remaining: {_networkSpawned.Count}");
        }

        [PurrButton, ContextMenu("Instantiate prefab locally")]
        private async void InstantiateByLocation()
        {
            var package = GetPackage();
            if (package == null)
                return;

            var handle = package.LoadAssetAsync<GameObject>(_prefabLocation);
            await handle;
            if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
            {
                Debug.LogError($"Failed to load YooAsset prefab: {_packageName}/{_prefabLocation}");
                return;
            }

            var operation = handle.InstantiateAsync(new InstantiateOptions(
                true,
                Random.insideUnitSphere * 3f,
                Quaternion.identity));
            await operation;
            if (operation.Status != EOperationStatus.Succeeded || !operation.Result)
            {
                handle.Release();
                Debug.LogError($"Failed to instantiate YooAsset prefab: {_packageName}/{_prefabLocation}");
                return;
            }

            _localAssetHandles.Add(handle);
            _localInstances.Add(operation.Result);
            Debug.Log($"Locally instantiated YooAsset prefab '{operation.Result.name}'. Total: {_localInstances.Count}", operation.Result);
        }

        [PurrButton, ContextMenu("Release last local instance")]
        private void ReleaseLastLocal()
        {
            if (_localInstances.Count == 0)
                return;

            var index = _localInstances.Count - 1;
            var instance = _localInstances[index];
            if (instance)
                Destroy(instance);
            _localInstances.RemoveAt(index);
            _localAssetHandles[index].Release();
            _localAssetHandles.RemoveAt(index);
            Debug.Log($"Released local YooAsset instance. Remaining: {_localInstances.Count}");
        }

        [PurrButton, ContextMenu("Release all local instances")]
        private void ReleaseAllLocal()
        {
            for (var i = 0; i < _localInstances.Count; i++)
            {
                if (_localInstances[i])
                    Destroy(_localInstances[i]);
                _localAssetHandles[i].Release();
            }

            _localInstances.Clear();
            _localAssetHandles.Clear();
            Debug.Log("Released all local YooAsset instances.");
        }

        [PurrButton, ContextMenu("Send test RPC")]
        private void SendTestRpc()
        {
            TestRpc();
        }

        [PurrButton, ContextMenu("Send YooAsset reference")]
        private void SendYooAssetReference()
        {
            TestSendYooAssetReference(_packageName, _prefabLocation);
        }

        [ObserversRpc]
        private void TestSendYooAssetReference(string packageName, string location)
        {
            Debug.Log($"Received YooAsset reference: package={packageName}, location={location}");
        }

        [PurrButton, ContextMenu("Send YooAsset reference (NetworkYooAsset)")]
        private async void SendNetworkYooAssetReference()
        {
            var package = GetPackage();
            if (package == null)
                return;

            var handle = package.LoadAssetAsync<GameObject>(_prefabLocation);
            await handle;
            if (handle.Status != EOperationStatus.Succeeded || !handle.AssetObject)
            {
                Debug.LogError($"Failed to load YooAsset prefab: {_packageName}/{_prefabLocation}");
                handle.Release();
                return;
            }

            // Keep the sender-side handle alive until OnDestroy; the RPC only carries the encoded key.
            _referenceHandles.Add(handle);
            TestSendNetworkYooAssetReference(handle);
        }

        [ObserversRpc]
        private void TestSendNetworkYooAssetReference(NetworkYooAsset reference)
        {
            Debug.Log($"Received YooAsset reference: {reference}", reference.Asset);

            // The receiver owns the unpacked handle; the Contains guard skips the sender's
            // own handle when the RPC loops back to the host.
            if (reference.handle is { IsValid: true } && !_referenceHandles.Contains(reference.handle))
                _referenceHandles.Add(reference.handle);
        }

        [ObserversRpc]
        private void TestRpc()
        {
            Debug.Log($"Received YooAsset test RPC on {name}.");
        }

        private ResourcePackage GetPackage()
        {
            if (string.IsNullOrEmpty(_packageName) || string.IsNullOrEmpty(_prefabLocation))
            {
                Debug.LogError("Set both Package Name and Prefab Location first.");
                return null;
            }

            try
            {
                return YooAssets.GetPackage(_packageName);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }

        protected override void OnDestroy()
        {
            ReleaseAllLocal();
            for (var i = 0; i < _networkAssetHandles.Count; i++)
            {
                if (_networkAssetHandles[i] != null && _networkAssetHandles[i].IsValid)
                    _networkAssetHandles[i].Release();
            }

            _networkAssetHandles.Clear();

            for (var i = 0; i < _referenceHandles.Count; i++)
            {
                if (_referenceHandles[i] is { IsValid: true })
                    _referenceHandles[i].Release();
            }

            _referenceHandles.Clear();
            base.OnDestroy();
        }
    }
}
#endif
