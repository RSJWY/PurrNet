#if UNITY_EDITOR && YOOASSET_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using PurrNet.Logging;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using YooAsset.Editor;

namespace PurrNet
{
    [CustomEditor(typeof(YooAssetNetworkPrefabs))]
    public class YooAssetNetworkPrefabsEditor : UnityEditor.Editor
    {
        private YooAssetNetworkPrefabs _target;
        private SerializedProperty _entriesProp;
        private SerializedProperty _linkedProp;
        private SerializedProperty _generationPackageProp;
        private SerializedProperty _generationGroupsProp;
        private SerializedProperty _generationTagsProp;
        private SerializedProperty _generationRuleProp;
        private ReorderableList _reorderableList;
        private string _searchFilter = "";

        private const float SPACING = 8f;
        private const float INDEX_WIDTH = 30f;

        private static bool _generating;

        // packageName -> (location -> assetPath), built from the YooAsset collector settings.
        // Only used to display/reverse-lookup prefabs in entry rows; entries on disk stay plain strings.
        private static readonly Dictionary<string, Dictionary<string, string>> _locationLookups = new();

        [InitializeOnLoadMethod]
        private static void SubscribeAutoGenerate()
        {
            YooAssetNetworkPrefabs.onAutoGenerateRequested += OnAutoGenerateRequested;
        }

        private static void OnAutoGenerateRequested(YooAssetNetworkPrefabs target)
        {
            if (target && target.autoGenerate)
                Generate(target);
        }

        private void OnEnable()
        {
            _target = (YooAssetNetworkPrefabs)target;
            _entriesProp = serializedObject.FindProperty("_entries");
            _linkedProp = serializedObject.FindProperty("linkedYooAssetPrefabs");
            _generationPackageProp = serializedObject.FindProperty("generationPackageName");
            _generationGroupsProp = serializedObject.FindProperty("generationGroupNames");
            _generationTagsProp = serializedObject.FindProperty("generationTags");
            _generationRuleProp = serializedObject.FindProperty("generationRuleName");

            if (_target.autoGenerate)
                Generate(_target);

            SetupReorderableList();
        }

        private void SetupReorderableList()
        {
            _reorderableList = new ReorderableList(serializedObject, _entriesProp, true, true, true, true);
            _reorderableList.elementHeight = EditorGUIUtility.singleLineHeight;

            _reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(new Rect(rect.x, rect.y, INDEX_WIDTH, rect.height), "ID");
                EditorGUI.LabelField(new Rect(rect.x + INDEX_WIDTH + SPACING, rect.y, rect.width - INDEX_WIDTH - SPACING, rect.height), "Package / Location / Prefab");
            };

            _reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = _entriesProp.GetArrayElementAtIndex(index);
                var packageProp = element.FindPropertyRelative("packageName");
                var locationProp = element.FindPropertyRelative("location");

                float x = rect.x;
                EditorGUI.LabelField(new Rect(x, rect.y, INDEX_WIDTH, rect.height), index.ToString());
                x += INDEX_WIDTH + SPACING;

                float remaining = rect.width - INDEX_WIDTH - SPACING;
                float packageWidth = Mathf.Max(60f, remaining * 0.22f);
                float locationWidth = Mathf.Max(80f, remaining * 0.40f);
                float objectWidth = Mathf.Max(40f, remaining - packageWidth - locationWidth - SPACING * 2);

                EditorGUI.BeginDisabledGroup(_target.autoGenerate);

                packageProp.stringValue = EditorGUI.TextField(
                    new Rect(x, rect.y, packageWidth, rect.height), packageProp.stringValue);
                x += packageWidth + SPACING;

                locationProp.stringValue = EditorGUI.TextField(
                    new Rect(x, rect.y, locationWidth, rect.height), locationProp.stringValue);
                x += locationWidth + SPACING;

                DrawEntryObjectField(new Rect(x, rect.y, objectWidth, rect.height), packageProp, locationProp);

                EditorGUI.EndDisabledGroup();
            };

            _reorderableList.onAddDropdownCallback = (Rect buttonRect, ReorderableList list) =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Empty Entry"), false, () =>
                {
                    int index = list.count;
                    list.serializedProperty.arraySize++;
                    var element = list.serializedProperty.GetArrayElementAtIndex(index);
                    element.FindPropertyRelative("packageName").stringValue = string.Empty;
                    element.FindPropertyRelative("location").stringValue = string.Empty;
                    serializedObject.ApplyModifiedProperties();
                });

                menu.AddItem(new GUIContent("Add Selected YooAsset Prefabs"), false, () =>
                {
                    bool addedAny = false;
                    foreach (var obj in Selection.gameObjects)
                    {
                        if (!PrefabUtility.IsPartOfPrefabAsset(obj))
                            continue;

                        var path = AssetDatabase.GetAssetPath(obj);
                        if (string.IsNullOrEmpty(path))
                            continue;

                        if (!TryGetLocationForAssetPath(_target.generationPackageName, path, out var location))
                        {
                            PurrLogger.LogWarning($"`{obj.name}` is not collected by YooAsset package '{_target.generationPackageName}' and was not added.", obj);
                            continue;
                        }

                        addedAny = true;
                        int index = list.count;
                        list.serializedProperty.arraySize++;
                        var element = list.serializedProperty.GetArrayElementAtIndex(index);
                        element.FindPropertyRelative("packageName").stringValue = _target.generationPackageName;
                        element.FindPropertyRelative("location").stringValue = location;
                    }

                    if (addedAny)
                        serializedObject.ApplyModifiedProperties();
                });

                menu.ShowAsContext();
            };
        }

        private void DrawEntryObjectField(Rect rect, SerializedProperty packageProp, SerializedProperty locationProp)
        {
            var current = LoadPrefabForEntry(packageProp.stringValue, locationProp.stringValue);

            EditorGUI.BeginChangeCheck();
            var selected = (GameObject)EditorGUI.ObjectField(rect, current, typeof(GameObject), false);
            if (!EditorGUI.EndChangeCheck())
                return;

            if (!selected)
            {
                locationProp.stringValue = string.Empty;
                return;
            }

            if (!PrefabUtility.IsPartOfPrefabAsset(selected))
            {
                PurrLogger.LogWarning($"`{selected.name}` is not a prefab asset and was not added.", selected);
                return;
            }

            var path = AssetDatabase.GetAssetPath(selected);
            var packageName = packageProp.stringValue;

            if (string.IsNullOrEmpty(packageName))
            {
                packageName = _target.generationPackageName;
                packageProp.stringValue = packageName;
            }

            if (!TryGetLocationForAssetPath(packageName, path, out var location))
            {
                PurrLogger.LogWarning($"`{selected.name}` is not collected by YooAsset package '{packageName}' and was not added.", selected);
                return;
            }

            locationProp.stringValue = location;
        }

        private static GameObject LoadPrefabForEntry(string packageName, string location)
        {
            if (string.IsNullOrEmpty(location))
                return null;

            if (location.StartsWith("Assets/", StringComparison.Ordinal))
            {
                var direct = AssetDatabase.LoadAssetAtPath<GameObject>(location);
                if (direct)
                    return direct;

                if (!location.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    var withExtension = AssetDatabase.LoadAssetAtPath<GameObject>(location + ".prefab");
                    if (withExtension)
                        return withExtension;
                }
            }

            if (!string.IsNullOrEmpty(packageName) &&
                GetLocationLookup(packageName).TryGetValue(location, out var path))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            return null;
        }

        /// <summary>
        /// Validates that the asset path is collected by the given package as a main asset
        /// and returns the location to load it with (address or asset path, depending on
        /// the package's EnableAddressable setting).
        /// </summary>
        private static bool TryGetLocationForAssetPath(string packageName, string assetPath, out string location)
        {
            location = null;

            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(assetPath))
                return false;

            foreach (var kvp in GetLocationLookup(packageName))
            {
                if (!string.Equals(kvp.Value, assetPath, StringComparison.Ordinal))
                    continue;

                location = kvp.Key;
                return true;
            }

            return false;
        }

        private static Dictionary<string, string> GetLocationLookup(string packageName)
        {
            if (_locationLookups.TryGetValue(packageName, out var lookup))
                return lookup;

            lookup = BuildLocationLookup(packageName);
            _locationLookups[packageName] = lookup;
            return lookup;
        }

        private static Dictionary<string, string> BuildLocationLookup(string packageName)
        {
            var lookup = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(packageName) || !BundleCollectorSettingData.HasSettingAsset())
                return lookup;

            try
            {
                var setting = BundleCollectorSettingData.Setting;
                var package = setting.GetPackage(packageName);
                if (package == null)
                    return lookup;

                var result = setting.BeginCollect(packageName, true, false);
                foreach (var asset in result.CollectAssets)
                {
                    if (asset.CollectorType != ECollectorType.MainAssetCollector)
                        continue;

                    var path = asset.AssetInfo.AssetPath;
                    if (string.IsNullOrEmpty(path))
                        continue;

                    var location = package.EnableAddressable ? asset.Address : path;
                    if (!string.IsNullOrEmpty(location) && !lookup.ContainsKey(location))
                        lookup.Add(location, path);
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogWarning($"Failed to collect YooAsset package '{packageName}': {e.Message}");
            }

            return lookup;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. Header
            SharedAssetEditorUI.DrawHeader(
                "YooAsset Network Prefabs",
                "This asset stores YooAsset prefab locations for network spawning. " +
                "Prefabs can be added manually or auto-generated from a YooAsset package's collector settings.");

            // 2. Generation Settings
            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_generationPackageProp, new GUIContent("Package"));
            EditorGUILayout.PropertyField(_generationGroupsProp,
                new GUIContent("Groups", "Only assets from these collector groups are generated. Empty = all groups."), true);
            EditorGUILayout.PropertyField(_generationTagsProp,
                new GUIContent("Tags", "Only assets carrying at least one of these tags are generated. Empty = no tag filtering."), true);
            DrawRulePopup();
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
            }

            // 3. Toggle buttons row
            GUILayout.BeginHorizontal();
            DrawToggleButton("Auto generate", ref _target.autoGenerate);
            _target.preloadAtStartup = SharedAssetEditorUI.DrawToggleButton("Preload at startup", _target.preloadAtStartup, _target);
            GUILayout.EndHorizontal();

            // 4. Generate button (full-width, own row)
            SharedAssetEditorUI.DrawGenerateButton(() =>
            {
                Generate(_target);
                serializedObject.Update();
                _entriesProp = serializedObject.FindProperty("_entries");
            });

            // 5. Linked prefabs
            SharedAssetEditorUI.DrawLinkedField(_linkedProp);

            // 6. Entry list
            SharedAssetEditorUI.DrawEntryList(_reorderableList, _target.autoGenerate,
                ref _searchFilter, i =>
                {
                    if (i >= _entriesProp.arraySize) return null;
                    var element = _entriesProp.GetArrayElementAtIndex(i);
                    var location = element.FindPropertyRelative("location")?.stringValue;
                    return string.IsNullOrEmpty(location) ? null : location;
                });

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                _target.Refresh();
                EditorUtility.SetDirty(_target);
            }
        }

        private void DrawToggleButton(string label, ref bool value)
        {
            value = SharedAssetEditorUI.DrawToggleButton(label, value, _target, () =>
            {
                if (_target.autoGenerate)
                {
                    Generate(_target);
                    serializedObject.Update();
                    _entriesProp = serializedObject.FindProperty("_entries");
                }
            });
        }

        /// <summary>
        /// Draws a dropdown of all discovered IYooAssetNetworkPrefabRule implementations,
        /// YooAsset collector-window style. The asset stores the rule's class name.
        /// </summary>
        private void DrawRulePopup()
        {
            var options = new List<string> { "Default (main-collected prefabs)" };
            var values = new List<string> { string.Empty };

            foreach (var type in TypeCache.GetTypesDerivedFrom<IYooAssetNetworkPrefabRule>())
            {
                if (type.IsAbstract || type == typeof(DefaultYooAssetNetworkPrefabRule))
                    continue;

                options.Add(type.FullName);
                values.Add(type.Name);
            }

            var current = _generationRuleProp.stringValue;
            int index = values.IndexOf(current);
            if (index < 0)
            {
                options.Add($"{current} (missing)");
                values.Add(current);
                index = values.Count - 1;
            }

            int newIndex = EditorGUILayout.Popup(
                new GUIContent("Generation Rule",
                    "IYooAssetNetworkPrefabRule implementation deciding which collected assets become entries."),
                index, options.ToArray());

            if (newIndex != index)
                _generationRuleProp.stringValue = values[newIndex];
        }

        /// <summary>
        /// Collects the configured YooAsset package and adds matching assets as entries.
        /// Scope can be narrowed to specific collector groups and/or asset tags, and the
        /// final per-asset decision is made by the configured IYooAssetNetworkPrefabRule
        /// (default: main-collected .prefab assets).
        /// Entries covered by linked providers are skipped; duplicates are avoided.
        /// </summary>
        public static void Generate(YooAssetNetworkPrefabs target)
        {
            if (!target) return;
            if (_generating) return;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            _generating = true;
            try
            {
                var packageName = target.generationPackageName;
                if (string.IsNullOrEmpty(packageName) || !BundleCollectorSettingData.HasSettingAsset())
                    return;

                var setting = BundleCollectorSettingData.Setting;
                var package = setting.GetPackage(packageName);
                if (package == null)
                {
                    PurrLogger.LogWarning($"YooAsset package '{packageName}' not found in the collector settings.");
                    return;
                }

                var rule = ResolveRule(target.generationRuleName);

                // Mirrors BundleCollectorSetting.BeginCollect's command setup, but collects per
                // group so the group scope can be filtered and group names reach the rule data.
                var ignoreRule = BundleCollectorSettingData.GetAssetIgnoreRuleInstance(package.IgnoreRuleName);
                var command = new CollectCommand(packageName, ignoreRule)
                {
                    UniqueBundleName = setting.UniqueBundleName,
                    UseAssetDependencyDB = false,
                    EnableAddressable = package.EnableAddressable,
                    SupportExtensionless = package.SupportExtensionless,
                    LocationToLower = package.LocationToLower,
                    IncludeAssetGUID = package.IncludeAssetGUID,
                    AutoCollectShaders = package.AutoCollectShaders
                };
                command.SetSimulateBuild(true);

                var groupNames = target.generationGroupNames;
                bool filterGroups = groupNames != null && groupNames.Count > 0;
                var missingGroups = filterGroups ? new HashSet<string>(groupNames) : null;

                var tags = target.generationTags;
                bool filterTags = tags != null && tags.Count > 0;

                var linkedKeys = CollectLinkedKeys(target);
                var existingKeys = target.GetExistingKeys();

                bool changed = false;
                foreach (var group in package.Groups)
                {
                    if (filterGroups)
                    {
                        if (!groupNames.Contains(group.GroupName))
                            continue;
                        missingGroups.Remove(group.GroupName);
                    }

                    var assets = group.GetAllCollectAssets(command);
                    foreach (var asset in assets)
                    {
                        if (filterTags && (asset.AssetTags == null || !HasAnyTag(asset.AssetTags, tags)))
                            continue;

                        var path = asset.AssetInfo.AssetPath;
                        if (string.IsNullOrEmpty(path))
                            continue;

                        var location = package.EnableAddressable ? asset.Address : path;
                        if (string.IsNullOrEmpty(location))
                            continue;

                        var data = new YooAssetNetworkPrefabRuleData(
                            packageName,
                            group.GroupName,
                            asset.Address ?? string.Empty,
                            path,
                            asset.AssetTags,
                            asset.CollectorType == ECollectorType.MainAssetCollector);

                        if (!rule.IsNetworkPrefab(data))
                            continue;

                        var key = YooAssetNetworkPrefabs.Encode(packageName, location);
                        if (linkedKeys.Contains(key) || !existingKeys.Add(key))
                            continue;

                        target.AddEntry(packageName, location);
                        changed = true;
                    }
                }

                if (missingGroups is { Count: > 0 })
                {
                    PurrLogger.LogWarning(
                        $"YooAsset package '{packageName}' has no collector groups named: {string.Join(", ", missingGroups)}.");
                }

                if (changed)
                {
                    target.Refresh();
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                }

                _locationLookups.Remove(packageName);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"An error occurred during YooAsset prefab generation: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _generating = false;
            }
        }

        private static bool HasAnyTag(IReadOnlyList<string> assetTags, List<string> wantedTags)
        {
            for (int i = 0; i < assetTags.Count; i++)
            {
                if (wantedTags.Contains(assetTags[i]))
                    return true;
            }
            return false;
        }

        private static readonly DefaultYooAssetNetworkPrefabRule _defaultRule = new();
        private static readonly Dictionary<string, IYooAssetNetworkPrefabRule> _ruleInstances = new();

        /// <summary>
        /// Resolves a generation rule by class name, YooAsset style: implementations are
        /// discovered via TypeCache and instantiated once. Empty name = default rule.
        /// </summary>
        private static IYooAssetNetworkPrefabRule ResolveRule(string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName))
                return _defaultRule;

            if (_ruleInstances.TryGetValue(ruleName, out var cached))
                return cached;

            foreach (var type in TypeCache.GetTypesDerivedFrom<IYooAssetNetworkPrefabRule>())
            {
                if (type.IsAbstract)
                    continue;
                if (type.Name != ruleName && type.FullName != ruleName)
                    continue;

                var instance = (IYooAssetNetworkPrefabRule)Activator.CreateInstance(type);
                _ruleInstances[ruleName] = instance;
                return instance;
            }

            PurrLogger.LogWarning(
                $"IYooAssetNetworkPrefabRule '{ruleName}' was not found; falling back to the default rule.");
            return _defaultRule;
        }

        private static HashSet<string> CollectLinkedKeys(YooAssetNetworkPrefabs target)
        {
            var keys = new HashSet<string>();
            var visited = new HashSet<YooAssetNetworkPrefabs>();

            void Collect(YooAssetNetworkPrefabs provider)
            {
                if (!provider || !visited.Add(provider))
                    return;

                foreach (var entry in provider.entries)
                {
                    if (!string.IsNullOrEmpty(entry.packageName) && !string.IsNullOrEmpty(entry.location))
                        keys.Add(YooAssetNetworkPrefabs.Encode(entry.packageName, entry.location));
                }

                if (provider.linkedYooAssetPrefabs == null)
                    return;

                foreach (var link in provider.linkedYooAssetPrefabs)
                    Collect(link);
            }

            if (target.linkedYooAssetPrefabs != null)
            {
                foreach (var link in target.linkedYooAssetPrefabs)
                    Collect(link);
            }

            return keys;
        }
    }
}
#endif
