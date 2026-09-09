#if YOOASSET_PURRNET_SUPPORT
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;
using YooAsset;

namespace PurrNet
{
    /// <summary>
    /// A network-serializable reference to a YooAsset asset.
    /// Only the encoded (packageName, location) key travels over the wire;
    /// the receiver loads the asset from its own YooAsset package after unpacking.
    /// Mirrors <see cref="NetworkAddressable"/> for the YooAsset integration.
    /// </summary>
    public struct NetworkYooAsset : IPackedAuto, IAsyncPackable
    {
        /// <summary>
        /// Prefix for encoded keys. Keeps generic asset keys from colliding with
        /// the YooAsset prefab keys (`purrnet-yooasset-prefab-v1:`) and scene keys.
        /// </summary>
        public const string KeyPrefix = "purrnet-yooasset-asset-v1:";

        [DontPack] public AssetHandle handle;
        private string _key;

        public NetworkYooAsset(AssetHandle handle)
        {
            this.handle = handle;
            _key = null;
        }

        /// <summary>The loaded asset, or null if not yet resolved.</summary>
        public Object Asset => handle is { IsValid: true } ? handle.AssetObject : null;

        /// <summary>Whether the YooAsset has been resolved and the asset is loaded.</summary>
        public bool IsValid => handle is { IsValid: true } && handle.AssetObject != null;

        public static implicit operator NetworkYooAsset(AssetHandle handle) => new(handle);

        public static implicit operator AssetHandle(NetworkYooAsset networkYooAsset) =>
            networkYooAsset.handle;

        /// <summary>
        /// Encodes a package/location pair into a wire key.
        /// Length-prefixed package name keeps the pair unambiguous.
        /// </summary>
        public static string Encode(string packageName, string location)
        {
            return $"{KeyPrefix}{packageName.Length}:{packageName}{location}";
        }

        /// <summary>
        /// Decodes a wire key back into its package name and location.
        /// </summary>
        public static bool TryDecode(string key, out string packageName, out string location)
        {
            packageName = null;
            location = null;

            if (string.IsNullOrEmpty(key) || !key.StartsWith(KeyPrefix, System.StringComparison.Ordinal))
                return false;

            var lengthStart = KeyPrefix.Length;
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

        /// <summary>
        /// Releases the loaded asset handle, if any.
        /// The receiver owns the handle created during unpacking and should call this when done.
        /// </summary>
        public void Release()
        {
            if (handle is { IsValid: true })
                handle.Release();
            handle = null;
        }

        public ValueTask<IAsyncPackable> PrepareForPackAsync()
        {
            if (handle is { IsValid: true })
            {
                var info = handle.GetAssetInfo();
                var location = !string.IsNullOrEmpty(info.Address) ? info.Address : info.AssetPath;

                if (!string.IsNullOrEmpty(info.PackageName) && !string.IsNullOrEmpty(location))
                    _key = Encode(info.PackageName, location);
                else
                    PurrLogger.LogError("Failed to pack network YooAsset as the asset info is incomplete.");
            }
            return new ValueTask<IAsyncPackable>(this);
        }

        public async ValueTask<IAsyncPackable> PrepareAfterUnpackAsync()
        {
            if (string.IsNullOrEmpty(_key))
            {
                PurrLogger.LogError("Failed to unpack network YooAsset as the key is empty.");
                return this;
            }

            if (!TryDecode(_key, out var packageName, out var location))
            {
                PurrLogger.LogError($"Failed to unpack network YooAsset as the key '{_key}' is invalid.");
                return this;
            }

            if (!YooAssets.TryGetPackage(packageName, out var package))
            {
                PurrLogger.LogError($"Failed to unpack network YooAsset as the YooAsset package '{packageName}' was not found.");
                return this;
            }

            if (package.InitializeStatus != EOperationStatus.Succeeded)
            {
                PurrLogger.LogError($"Failed to unpack network YooAsset as the YooAsset package '{packageName}' is not initialized.");
                return this;
            }

            var loadHandle = package.LoadAssetAsync<Object>(location);
            await loadHandle;

            if (loadHandle.Status != EOperationStatus.Succeeded || !loadHandle.AssetObject)
            {
                PurrLogger.LogError($"Failed to load YooAsset '{packageName}/{location}' while unpacking: {loadHandle.Error}");
                loadHandle.Release();
                return this;
            }

            handle = loadHandle;
            return this;
        }

        public override string ToString()
        {
            if (Asset)
                return Asset.name;
            if (!string.IsNullOrEmpty(_key))
                return $"NetworkYooAsset({_key}, not loaded)";
            return "NetworkYooAsset(empty)";
        }
    }
}
#endif
