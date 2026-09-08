#if YOOASSET_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Modules
{
    public class YooAssetSyncModule : INetworkModule
    {
        public delegate void OnClientYooAssetLoadStateChanged(PlayerID player, string key, bool loaded);

        private readonly NetworkManager _manager;
        private readonly PlayersManager _playersManager;

        private readonly Dictionary<PlayerID, HashSet<string>> _clientLoadedKeys = new();

        /// <summary>
        /// Fired on the server when a client reports that a YooAsset prefab has been loaded or unloaded.
        /// </summary>
        public event OnClientYooAssetLoadStateChanged onClientLoadStateChanged;

        public YooAssetSyncModule(NetworkManager manager, PlayersManager playersManager)
        {
            _manager = manager;
            _playersManager = playersManager;
        }

        public void Enable(bool asServer)
        {
            if (asServer)
            {
                _playersManager.Subscribe<YooAssetLoadStatePacket>(OnLoadStateReceived);
                _playersManager.onPlayerLeft += OnPlayerLeft;
            }
            else
            {
                if (_manager.networkRules && _manager.networkRules.YooAssetSyncLoadState &&
                    _manager.yooAssetNetworkPrefabs)
                {
                    _playersManager.Subscribe<YooAssetLoadRequestPacket>(OnLoadRequestReceived);
                    YooAssetNetworkPrefabs.onLoadStateChanged += OnClientLoadStateChanged;
                    foreach (var key in _manager.yooAssetNetworkPrefabs.GetLoadedKeys())
                        SendLoadState(key, true);
                }
            }
        }

        public void Disable(bool asServer)
        {
            if (asServer)
            {
                _playersManager.Unsubscribe<YooAssetLoadStatePacket>(OnLoadStateReceived);
                _playersManager.onPlayerLeft -= OnPlayerLeft;
                _clientLoadedKeys.Clear();
            }
            else
            {
                _playersManager.Unsubscribe<YooAssetLoadRequestPacket>(OnLoadRequestReceived);
                YooAssetNetworkPrefabs.onLoadStateChanged -= OnClientLoadStateChanged;
            }
        }

        private async void OnLoadRequestReceived(PlayerID sender, YooAssetLoadRequestPacket packet, bool asServer)
        {
            if (asServer || !_manager.yooAssetNetworkPrefabs)
                return;

            var key = packet.key.value ?? string.Empty;
            if (string.IsNullOrEmpty(key))
                return;

            var prefabData = await _manager.yooAssetNetworkPrefabs.LoadPrefabByKeyAsync(key);
            if (prefabData.prefab)
                SendLoadState(key, true);
        }

        private void OnClientLoadStateChanged(string key, bool loaded)
        {
            if (!_manager.networkRules || !_manager.networkRules.YooAssetSyncLoadState)
                return;

            SendLoadState(key, loaded);
        }

        private void SendLoadState(string key, bool loaded)
        {
            _playersManager.SendToServer(new YooAssetLoadStatePacket
            {
                key = new StringUTF8(key ?? string.Empty),
                loaded = loaded
            });
        }

        private void OnLoadStateReceived(PlayerID player, YooAssetLoadStatePacket packet, bool asServer)
        {
            if (!asServer)
                return;

            if (!_clientLoadedKeys.TryGetValue(player, out var set))
            {
                set = new HashSet<string>();
                _clientLoadedKeys[player] = set;
            }

            var key = packet.key.value ?? string.Empty;

            if (packet.loaded)
                set.Add(key);
            else
                set.Remove(key);

            onClientLoadStateChanged?.Invoke(player, key, packet.loaded);

            if (packet.loaded && _manager.TryGetModule<HierarchyFactory>(true, out var factory))
                factory.EvaluateVisibilityForPlayer(player);
        }

        /// <summary>
        /// Gets which keys the given client has loaded
        /// </summary>
        /// <param name="player">Player to search for loaded keys</param>
        /// <returns>Which keys the player has confirmed loaded</returns>
        public IReadOnlyCollection<string> GetLoadedKeysForPlayer(PlayerID player)
        {
            return _clientLoadedKeys.TryGetValue(player, out var set) ? set : (IReadOnlyCollection<string>)Array.Empty<string>();
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!asServer)
                return;

            _clientLoadedKeys.Remove(player);
        }

        /// <summary>
        /// Returns all players that have reported the given YooAsset key as loaded.
        /// </summary>
        public IReadOnlyList<PlayerID> GetPlayersWithKeyLoaded(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Array.Empty<PlayerID>();

            var result = new List<PlayerID>();
            foreach (var (player, keys) in _clientLoadedKeys)
            {
                if (keys.Contains(key))
                    result.Add(player);
            }
            return result;
        }

        /// <summary>
        /// Checks whether a client has loaded a specific key
        /// </summary>
        public bool ClientHasLoaded(PlayerID player, string key)
        {
            if (string.IsNullOrEmpty(key))
                return true;

            if (!_clientLoadedKeys.TryGetValue(player, out var set))
                return false;

            return set.Contains(key);
        }

        /// <summary>
        /// Sends the request to a player to prepare/warmup/fetch a specific key
        /// </summary>
        public void RequestPlayerToLoad(PlayerID player, string key)
        {
            if (string.IsNullOrEmpty(key) || player.isServer)
                return;

            _playersManager.Send(player, new YooAssetLoadRequestPacket
            {
                key = new StringUTF8(key)
            });
        }
    }
}
#endif
