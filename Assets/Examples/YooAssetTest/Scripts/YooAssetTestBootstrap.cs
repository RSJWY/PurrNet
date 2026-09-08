#if YOOASSET_PURRNET_SUPPORT
using System;
using System.Threading.Tasks;
using PurrNet;
using UnityEngine;
using YooAsset;

namespace YooAssetTest
{
    /// <summary>
    /// Initializes the YooAsset package (editor simulate mode) and then preloads the
    /// assigned YooAssetNetworkPrefabs registry. Runs before NetworkManager.Start.
    /// Keep 'Preload At Startup' disabled on the registry asset; this bootstrap owns the timing.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class YooAssetTestBootstrap : MonoBehaviour
    {
        [SerializeField] private string _packageName = "PurrNetTests";
        [SerializeField] private YooAssetNetworkPrefabs _registry;

        /// <summary>True once the YooAsset package is initialized and ready to load assets.</summary>
        public static bool isPackageReady { get; private set; }

        private void Awake()
        {
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
#if UNITY_EDITOR
                if (!YooAssets.IsInitialized)
                    YooAssets.Initialize();

                if (!YooAssets.TryGetPackage(_packageName, out var package))
                    package = YooAssets.CreatePackage(_packageName);

                if (package.InitializeStatus == EOperationStatus.Processing)
                {
                    // Another initializer owns the package init; wait for it to finish.
                    while (package.InitializeStatus == EOperationStatus.Processing)
                        await Task.Delay(50);
                }

                if (package.InitializeStatus != EOperationStatus.Succeeded)
                {
                    var buildResult = EditorSimulateBuildInvoker.Build(_packageName, (int)EBundleType.VirtualAssetBundle);

                    // YooAsset 3.x init is only the file system; the manifest is loaded in two
                    // extra steps (version request + manifest load) before any asset can be used.
                    var init = package.InitializePackageAsync(new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                            buildResult.PackageRootDirectory)
                    });
                    await init;
                    if (init.Status != EOperationStatus.Succeeded)
                    {
                        Debug.LogError($"[YooAssetTestBootstrap] Package '{_packageName}' init failed: {init.Error}");
                        return;
                    }

                    var version = package.RequestPackageVersionAsync();
                    await version;
                    if (version.Status != EOperationStatus.Succeeded)
                    {
                        Debug.LogError($"[YooAssetTestBootstrap] Package '{_packageName}' version request failed: {version.Error}");
                        return;
                    }

                    var manifest = package.LoadPackageManifestAsync(
                        new LoadPackageManifestOptions(version.PackageVersion, 60));
                    await manifest;
                    if (manifest.Status != EOperationStatus.Succeeded)
                    {
                        Debug.LogError($"[YooAssetTestBootstrap] Package '{_packageName}' manifest load failed: {manifest.Error}");
                        return;
                    }
                }

                isPackageReady = true;
                Debug.Log($"[YooAssetTestBootstrap] Package '{_packageName}' ready.");
#else
                Debug.LogError("[YooAssetTestBootstrap] Only editor simulate mode is supported by this test bootstrap.");
#endif

                if (_registry && !_registry.isLoaded)
                {
                    await _registry.LoadAllAsync();
                    Debug.Log($"[YooAssetTestBootstrap] Registry '{_registry.name}' preloaded: {_registry.isLoaded}");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
#endif
