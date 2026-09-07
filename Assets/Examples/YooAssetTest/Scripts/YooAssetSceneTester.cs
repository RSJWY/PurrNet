#if YOOASSET_PURRNET_SUPPORT
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YooAssetTest
{
    /// <summary>
    /// YooAsset 场景同步测试入口。Package 必须已由业务侧初始化并完成资源清单加载。
    /// </summary>
    public sealed class YooAssetSceneTester : MonoBehaviour
    {
        [SerializeField] private string _packageName = "DefaultPackage";
        [SerializeField] private string _sceneLocation;
        [SerializeField] private bool _additive;

        [PurrButton, ContextMenu("Load YooAsset scene")]
        private void LoadYooAssetScene()
        {
            var manager = NetworkManager.main;
            if (!manager || !manager.isServer)
            {
                Debug.LogError("Only the server can load YooAsset scenes.");
                return;
            }

            if (string.IsNullOrEmpty(_packageName) || string.IsNullOrEmpty(_sceneLocation))
            {
                Debug.LogError("Set both Package Name and Scene Location first.");
                return;
            }

            var settings = new PurrSceneSettings
            {
                mode = _additive ? LoadSceneMode.Additive : LoadSceneMode.Single,
                physicsMode = LocalPhysicsMode.None,
                isPublic = true
            };

            manager.sceneModule.LoadYooAssetSceneAsync(_packageName, _sceneLocation, settings);
        }

        [PurrButton, ContextMenu("Unload YooAsset scene")]
        private void UnloadYooAssetScene()
        {
            var manager = NetworkManager.main;
            if (!manager || !manager.isServer)
            {
                Debug.LogError("Only the server can unload YooAsset scenes.");
                return;
            }

            var count = manager.sceneModule.UnloadYooAssetSceneByLocation(_packageName, _sceneLocation);
            Debug.Log($"Unloaded {count} YooAsset scene instance(s): {_packageName}/{_sceneLocation}");
        }

        [PurrButton, ContextMenu("Log YooAsset scene state")]
        private void LogSceneState()
        {
            var manager = NetworkManager.main;
            if (!manager)
                return;

            var scenes = manager.sceneModule;
            var loaded = scenes.IsYooAssetSceneLoaded(_packageName, _sceneLocation);
            var loading = scenes.IsYooAssetSceneLoading(_packageName, _sceneLocation);
            var found = scenes.TryGetSceneIdByYooAssetLocation(_packageName, _sceneLocation, out var sceneId);
            Debug.Log($"YooAsset scene {_packageName}/{_sceneLocation}: loaded={loaded}, loading={loading}, " +
                      $"sceneId={(found ? sceneId.ToString() : "<none>")}");
        }

        private void OnEnable()
        {
            if (!NetworkManager.main)
                return;

            NetworkManager.main.sceneModule.onYooAssetSceneStartLoading += OnSceneStartLoading;
            NetworkManager.main.sceneModule.onYooAssetSceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            if (!NetworkManager.main)
                return;

            NetworkManager.main.sceneModule.onYooAssetSceneStartLoading -= OnSceneStartLoading;
            NetworkManager.main.sceneModule.onYooAssetSceneLoaded -= OnSceneLoaded;
        }

        private static void OnSceneStartLoading(SceneID sceneId, string packageName, string location, bool asServer)
        {
            Debug.Log($"YooAsset scene loading: id={sceneId}, package={packageName}, location={location}, asServer={asServer}");
        }

        private static void OnSceneLoaded(SceneID sceneId, string packageName, string location, bool asServer)
        {
            Debug.Log($"YooAsset scene loaded: id={sceneId}, package={packageName}, location={location}, asServer={asServer}");
        }
    }
}
#endif
