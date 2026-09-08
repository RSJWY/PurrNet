#if UNITY_EDITOR && YOOASSET_PURRNET_SUPPORT
using System;
using System.Collections.Generic;

namespace PurrNet
{
    /// <summary>
    /// Input data for <see cref="IYooAssetNetworkPrefabRule"/>.
    /// Mirrors YooAsset's own rule data style (AssetFilterRuleData etc.).
    /// </summary>
    public readonly struct YooAssetNetworkPrefabRuleData
    {
        /// <summary>The YooAsset package the asset belongs to.</summary>
        public string PackageName { get; }

        /// <summary>The collector group the asset was collected from.</summary>
        public string GroupName { get; }

        /// <summary>The addressable address of the asset (empty when addressable is disabled).</summary>
        public string Address { get; }

        /// <summary>The asset database path of the asset.</summary>
        public string AssetPath { get; }

        /// <summary>The YooAsset asset tags carried by this asset (group + collector tags).</summary>
        public IReadOnlyList<string> AssetTags { get; }

        /// <summary>Whether the asset was collected by a main asset collector.</summary>
        public bool IsMainAssetCollector { get; }

        public YooAssetNetworkPrefabRuleData(
            string packageName,
            string groupName,
            string address,
            string assetPath,
            IReadOnlyList<string> assetTags,
            bool isMainAssetCollector)
        {
            PackageName = packageName;
            GroupName = groupName;
            Address = address;
            AssetPath = assetPath;
            AssetTags = assetTags;
            IsMainAssetCollector = isMainAssetCollector;
        }
    }

    /// <summary>
    /// Custom generation rule for <see cref="YooAssetNetworkPrefabs"/>.
    /// Implement this interface (in any editor assembly) and put the class name into the
    /// registry asset's Generation Rule Name field to control which collected YooAsset
    /// assets become network prefab entries. Same pattern as YooAsset's IAssetFilterRule.
    /// </summary>
    public interface IYooAssetNetworkPrefabRule
    {
        /// <summary>Returns true if the collected asset should become a network prefab entry.</summary>
        bool IsNetworkPrefab(YooAssetNetworkPrefabRuleData data);
    }

    /// <summary>
    /// Default generation rule: main-collected .prefab assets.
    /// </summary>
    public sealed class DefaultYooAssetNetworkPrefabRule : IYooAssetNetworkPrefabRule
    {
        public bool IsNetworkPrefab(YooAssetNetworkPrefabRuleData data)
        {
            return data.IsMainAssetCollector &&
                   !string.IsNullOrEmpty(data.AssetPath) &&
                   data.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
