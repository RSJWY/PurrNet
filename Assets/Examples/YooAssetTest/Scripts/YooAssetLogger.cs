#if YOOASSET_PURRNET_SUPPORT
using PurrNet;
using UnityEngine;
using YooAsset;

namespace YooAssetTest
{
    public sealed class YooAssetLogger : MonoBehaviour
    {
        [SerializeField] private string _packageName = "DefaultPackage";

        [PurrButton, ContextMenu("Log YooAsset package state")]
        private void LogPackageState()
        {
            ResourcePackage package;
            try
            {
                package = YooAssets.GetPackage(_packageName);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                return;
            }

            if (package == null)
            {
                Debug.LogError($"Package '{_packageName}' was not found. Set it up before running this test.");
                return;
            }

            Debug.Log($"YooAsset package '{package.PackageName}' status: {package.InitializeStatus}");
        }
    }
}
#endif
