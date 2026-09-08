#if YOOASSET_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;
using YooAsset;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "YooAssetNetworkPrefabs", menuName = "PurrNet/Network Prefabs/YooAsset Network Prefabs", order = -199)]
    public class YooAssetNetworkPrefabs : PrefabProviderScriptable, IAsyncPrefabProvider, IPersistentPrefabProvider
    {
        /// <summary>
        /// Prefix for encoded persistent keys. Keeps YooAsset keys from colliding with
        /// Addressables GUIDs and the YooAsset scene keys (`purrnet-yooasset-v1:`).
        /// </summary>
        public const string KeyPrefix = "purrnet-yooasset-prefab-v1:";

        [Serializable]
        public struct Entry
        {
            public string packageName;
            public string location;
        }

        [SerializeField] private bool _preloadAtStartup = true;
        [SerializeField] private List<Entry> _entries = new();

        [Tooltip("Will also get entries from these linked YooAssetNetworkPrefabs.")]
        public List<YooAssetNetworkPrefabs> linkedYooAssetPrefabs = new();

        public bool autoGenerate;

        [Tooltip("The YooAsset package whose collector settings are used when auto-generating entries.")]
        public string generationPackageName = "DefaultPackage";

        [Tooltip("Only assets from these collector groups are auto-generated. Empty = all groups in the package.")]
        public List<string> generationGroupNames = new();

        [Tooltip("Only assets carrying at least one of these YooAsset tags are auto-generated. Empty = no tag filtering.")]
        public List<string> generationTags = new();

        [Tooltip("Class name of an IYooAssetNetworkPrefabRule implementation used to decide which collected assets become entries. " +
                 "Empty = default rule (main-collected .prefab assets).")]
        public string generationRuleName = string.Empty;

        /// <summary>
        /// Whether all registered YooAsset prefabs have been loaded and are ready for use.
        /// </summary>
        public bool isLoaded { get; private set; }

        private readonly Dictionary<int, PrefabData> _prefabLookup = new();
        private readonly Dictionary<string, int> _keyToId = new();
        private readonly Dictionary<int, string> _idToKey = new();
        private readonly List<AssetHandle> _loadHandles = new();
        private readonly List<RuntimePrefabEntry> _runtimePrefabs = new();
        private readonly HashSet<string> _warnedNameFallbacks = new();
        private int _runtimePrefabStartId;

        private struct RuntimePrefabEntry
        {
            public string uniqueName;
            public GameObject prefab;
            public bool pooled;
            public int warmupCount;
        }

        /// <summary>
        /// The entries registered in this asset. Read-only access for external inspection.
        /// </summary>
        public IReadOnlyList<Entry> entries => _entries;

        /// <summary>
        /// Number of registered entries.
        /// </summary>
        public int count => _entries.Count;

        public bool preloadAtStartup
        {
            get => _preloadAtStartup;
            set => _preloadAtStartup = value;
        }

        public override IEnumerable<PrefabData> allPrefabs => _prefabLookup.Values;
        public IEnumerable<string> persistentIds => _keyToId.Keys;

        /// <summary>
        /// Encodes a package/location pair into a persistent key.
        /// Length-prefixed package name keeps the pair unambiguous.
        /// </summary>
        public static string Encode(string packageName, string location)
        {
            return $"{KeyPrefix}{packageName.Length}:{packageName}{location}";
        }

        /// <summary>
        /// Decodes a persistent key back into its package name and location.
        /// </summary>
        public static bool TryDecode(string key, out string packageName, out string location)
        {
            packageName = null;
            location = null;

            if (string.IsNullOrEmpty(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
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
        /// Tries to get the loaded prefab data for a given encoded key.
        /// Returns false if the key is not registered or the prefab isn't loaded yet.
        /// </summary>
        public bool TryGetPrefabDataByKey(string key, out PrefabData prefabData)
        {
            if (_keyToId.TryGetValue(key, out int id))
                return _prefabLookup.TryGetValue(id, out prefabData);

            prefabData = default;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            return TryGetPrefabDataByKey(persistentId, out prefabData);
        }

        public override bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            return _prefabLookup.TryGetValue(prefabId, out prefabData);
        }

        public override bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            foreach (var data in _prefabLookup.Values)
            {
                if (data.prefab == prefab)
                {
                    prefabData = data;
                    return true;
                }
            }

            if (!TryMatchByName(_prefabLookup.Values, prefab, out prefabData))
                return false;

            if (_warnedNameFallbacks.Add(prefab.name))
            {
                PurrLogger.LogWarning(
                    $"Resolved YooAsset network prefab `{prefab.name}` by name because the given reference isn't the registered one.\n" +
                    "This happens when the same asset is duplicated across bundles; fix the YooAsset collector setup to avoid spawning the wrong prefab.");
            }

            return true;
        }

        public override void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5)
        {
            _runtimePrefabs.Add(new RuntimePrefabEntry
            {
                uniqueName = uniqueName,
                prefab = prefab,
                pooled = pooled,
                warmupCount = warmup
            });

            Refresh();
        }

        public bool TryGetKey(int localPrefabId, out string key)
        {
            return _idToKey.TryGetValue(localPrefabId, out key);
        }

        public bool TryGetPersistentId(int prefabId, out string persistentId)
        {
            return TryGetKey(prefabId, out persistentId);
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (TryGetPrefabData(prefab, out var prefabData))
                return TryGetPersistentId(prefabData.prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabByPersistentId(string persistentId, out GameObject prefab)
        {
            if (TryGetPrefabDataByPersistentId(persistentId, out var prefabData))
            {
                prefab = prefabData.prefab;
                return prefab;
            }

            prefab = null;
            return false;
        }

        public static event Action<string, bool> onLoadStateChanged;

        internal static void NotifyLoadStateChanged(string key, bool loaded)
        {
            onLoadStateChanged?.Invoke(key, loaded);
        }

        public IEnumerable<string> GetLoadedKeys()
        {
            foreach (var kvp in _prefabLookup)
            {
                if (kvp.Value.prefab != null && _idToKey.TryGetValue(kvp.Key, out var key))
                    yield return key;
            }
        }

        public override void Refresh()
        {
            RebuildLookup();
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

        /// <summary>
        /// Rebuilds the internal lookup tables from the entry list.
        /// Assigns deterministic IDs sorted by encoded key, so entry order in the
        /// inspector does not affect the cross-peer addressing.
        /// Includes entries from linked YooAssetNetworkPrefabs (deduped by key).
        /// Note: PrefabData.prefab will be null until LoadAllAsync is called.
        /// The IDs assigned here are local to this provider and will be
        /// offset by the CompositePrefabProvider when combined with other providers.
        /// </summary>
        private void RebuildLookup()
        {
            _prefabLookup.Clear();
            _keyToId.Clear();
            _idToKey.Clear();

            var seenKeys = new HashSet<string>();
            var sorted = new List<string>();
            var visited = new HashSet<YooAssetNetworkPrefabs>();

            void CollectEntries(YooAssetNetworkPrefabs provider)
            {
                if (!provider || !visited.Add(provider)) return;

                for (int i = 0; i < provider._entries.Count; i++)
                {
                    var entry = provider._entries[i];
                    if (string.IsNullOrEmpty(entry.packageName) || string.IsNullOrEmpty(entry.location))
                        continue;

                    var key = Encode(entry.packageName, entry.location);
                    if (!seenKeys.Add(key))
                        continue;

                    sorted.Add(key);
                }

                if (provider.linkedYooAssetPrefabs == null) return;
                for (int i = 0; i < provider.linkedYooAssetPrefabs.Count; i++)
                {
                    var link = provider.linkedYooAssetPrefabs[i];
                    if (link) CollectEntries(link);
                }
            }

            CollectEntries(this);
            sorted.Sort(StringComparer.Ordinal);

            for (int i = 0; i < sorted.Count; i++)
            {
                var key = sorted[i];

                _keyToId[key] = i;
                _idToKey[i] = key;
                _prefabLookup[i] = new PrefabData
                {
                    prefabId = i,
                    prefab = null,
                    pooled = false,
                    warmupCount = 0
                };
            }

            int runtimeStart = sorted.Count;
            _runtimePrefabStartId = runtimeStart;
            for (int i = 0; i < _runtimePrefabs.Count; i++)
            {
                var rt = _runtimePrefabs[i];
                if (!rt.prefab) continue;

                int id = runtimeStart + i;
                if (!string.IsNullOrEmpty(rt.uniqueName) && seenKeys.Add(rt.uniqueName))
                {
                    _keyToId[rt.uniqueName] = id;
                    _idToKey[id] = rt.uniqueName;
                }

                _prefabLookup[id] = new PrefabData
                {
                    prefabId = id,
                    prefab = rt.prefab,
                    pooled = rt.pooled,
                    warmupCount = rt.warmupCount
                };
            }
        }

        public bool NeedsLoad(int prefabId)
        {
            if (!_prefabLookup.TryGetValue(prefabId, out var data))
                return false;
            return data.prefab == null;
        }

        public async Task<PrefabData> LoadPrefabByKeyAsync(string key)
        {
            if (!_keyToId.TryGetValue(key, out int localId))
            {
                PurrLogger.LogError($"LoadPrefabByKeyAsync: key '{key}' not registered.");
                return default;
            }
            return await LoadPrefabAsync(localId);
        }

        public async Task<PrefabData> LoadPrefabAsync(int prefabId)
        {
            if (!_prefabLookup.TryGetValue(prefabId, out var data))
            {
                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not found in YooAssetNetworkPrefabs.");
                return default;
            }

            if (data.prefab != null)
                return data;

            if (!_idToKey.TryGetValue(prefabId, out var key))
            {
                PurrLogger.LogError($"LoadPrefabAsync: no key mapping for prefabId {prefabId}.");
                return default;
            }

            var prefab = await LoadYooAssetPrefabAsync(key);
            if (!prefab)
                return default;

            data.prefab = prefab;
            _prefabLookup[prefabId] = data;
            NotifyLoadStateChanged(key, true);
            return data;
        }

        /// <summary>
        /// Loads all registered YooAsset prefabs into memory.
        /// Must be called (and awaited) before the network is started.
        /// After loading, PrefabData.prefab will contain valid references.
        /// </summary>
        public async Task LoadAllAsync()
        {
            RebuildLookup();
            ReleaseAll();

            var sortedKeys = new List<string>(_keyToId.Count);
            foreach (var kvp in _keyToId)
                sortedKeys.Add(kvp.Key);
            sortedKeys.Sort(StringComparer.Ordinal);

            for (int i = 0; i < sortedKeys.Count; i++)
            {
                var key = sortedKeys[i];
                var id = _keyToId[key];

                var prefab = await LoadYooAssetPrefabAsync(key);
                if (!prefab)
                    continue;

                if (_prefabLookup.TryGetValue(id, out var data))
                {
                    data.prefab = prefab;
                    _prefabLookup[id] = data;
                    NotifyLoadStateChanged(key, true);
                }
            }

            isLoaded = true;
        }

        private async Task<GameObject> LoadYooAssetPrefabAsync(string key)
        {
            if (!TryDecode(key, out var packageName, out var location))
            {
                PurrLogger.LogError($"LoadYooAssetPrefabAsync: invalid key '{key}'.");
                return null;
            }

            try
            {
                if (!YooAssets.TryGetPackage(packageName, out var package))
                {
                    PurrLogger.LogError($"LoadYooAssetPrefabAsync: YooAsset package '{packageName}' not found for key '{key}'.");
                    return null;
                }

                if (package.InitializeStatus != EOperationStatus.Succeeded)
                {
                    PurrLogger.LogError($"LoadYooAssetPrefabAsync: YooAsset package '{packageName}' is not initialized for key '{key}'.");
                    return null;
                }

                var handle = package.LoadAssetAsync<GameObject>(location);
                _loadHandles.Add(handle);

                var completion = new TaskCompletionSource<bool>();
                handle.Completed += _ => completion.TrySetResult(true);
                await completion.Task;

                if (handle.Status != EOperationStatus.Succeeded || !handle.AssetObject)
                {
                    PurrLogger.LogError($"LoadYooAssetPrefabAsync: failed to load YooAsset prefab key '{key}': {handle.Error}");
                    return null;
                }

                return handle.AssetObject as GameObject;
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"LoadYooAssetPrefabAsync: exception loading key '{key}': {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Releases all loaded YooAsset handles.
        /// Call this when the network shuts down or the provider is no longer needed.
        /// </summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _loadHandles.Count; i++)
            {
                var handle = _loadHandles[i];
                if (handle is { IsValid: true })
                    handle.Release();
            }

            _loadHandles.Clear();
            isLoaded = false;

            foreach (var key in _idToKey.Values)
                NotifyLoadStateChanged(key, false);

            var keys = new List<int>(_prefabLookup.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (key >= _runtimePrefabStartId)
                    continue;
                if (_prefabLookup.TryGetValue(key, out var data))
                {
                    data.prefab = null;
                    _prefabLookup[key] = data;
                }
            }
        }

        private void OnDisable()
        {
            ReleaseAll();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (autoGenerate &&
                !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode &&
                !UnityEditor.EditorApplication.isCompiling &&
                !UnityEditor.EditorApplication.isUpdating)
            {
                UnityEditor.EditorApplication.delayCall += OnAutoGenerate;
            }
        }

        private void OnAutoGenerate()
        {
            // Actual generation is handled by the editor (YooAssetNetworkPrefabsEditor)
            // because it requires YooAsset.Editor which is in a separate editor assembly.
            // This triggers via onAutoGenerateRequested event.
            if (!this) return;
            onAutoGenerateRequested?.Invoke(this);
        }

        /// <summary>
        /// Event fired when auto-generation is triggered via OnValidate.
        /// The editor subscribes to this to perform the actual generation.
        /// </summary>
        public static event Action<YooAssetNetworkPrefabs> onAutoGenerateRequested;

        /// <summary>
        /// Gets the set of encoded keys already registered as entries.
        /// Used by the editor during generation to avoid duplicates.
        /// </summary>
        public HashSet<string> GetExistingKeys()
        {
            var keys = new HashSet<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (!string.IsNullOrEmpty(entry.packageName) && !string.IsNullOrEmpty(entry.location))
                    keys.Add(Encode(entry.packageName, entry.location));
            }
            return keys;
        }

        /// <summary>
        /// Adds an entry by package name and location. Used by the editor during generation.
        /// </summary>
        public void AddEntry(string packageName, string location)
        {
            _entries.Add(new Entry
            {
                packageName = packageName,
                location = location
            });
        }
#endif
    }
}
#endif
