# YooAsset scene synchronization test

This example mirrors the built-in `AddressablesTest` scripts while using YooAsset packages and locations.

## Setup

1. Install/resolve YooAsset 3.0.5 and initialize the same package on the server and every client.
2. Add `YooAssetSceneTester` to a bootstrap scene containing a PurrNet `NetworkManager`.
3. Set `Package Name` and the YooAsset scene `Location`. The target scene does not need to be in Build Settings.
4. To test network spawning, add `YooAssetObjectTester` to a spawned `NetworkIdentity`, set a prefab location, and register that prefab in PurrNet's normal `NetworkPrefabs` provider.
5. Build/update the YooAsset manifest for every peer before starting the test.

## Checks

- Use **Load YooAsset Scene** on the server and verify both peers report the same `SceneID`.
- Connect a late client and verify it automatically loads the scene and receives existing network objects.
- Disconnect/reconnect and verify no duplicate scene is loaded.
- Run as host and verify the scene is loaded and unloaded only once.
- Use **Unload YooAsset Scene**, then load it again.
- Use `YooAssetObjectTester` to load a prefab by location, network-spawn it, despawn it, and invoke an observers RPC.

The test scripts expect package initialization and remote download policy to remain owned by the consuming project.

# YooAsset network prefabs test (YooAssetNetworkPrefabs provider)

This folder also tests the `YooAssetNetworkPrefabs` registry integration (parallel to the Addressables one).

## Setup

1. The shared `Assets/PlayModeTests/BundleCollectorSetting.asset` already collects
   `Assets/Examples/YooAssetTest/Prefabs` into the `PurrNetTests` package (address = file name).
2. Add `YooAssetTestBootstrap` to the bootstrap scene next to the `NetworkManager`.
   It initializes the package (editor simulate mode) and preloads the registry.
   Assign the bundled `YooAssetNetworkPrefabs.asset` registry to it.
3. Assign the same registry to the NetworkManager's **Yoo Asset Network Prefabs** field.
4. Add `YooAssetNetworkPrefabsTester` to any active `NetworkIdentity` in the scene
   (package `PurrNetTests`, location `TestYooAssetNetworkPrefab` are the defaults).

## Checks

- **Spawn via SpawnYooAssetAsync** on the host: clones spawn `TestYooAssetNetworkPrefab`
  only after reporting the prefab loaded (load-state gating).
- **InstantiateSync/InstantiateAsync via handle (auto-spawn)**: IL interception network-spawns
  the instance without an explicit `Spawn` call.
- **Despawn last / Destroy last**: all peers despawn the instance.
- The spawned prefab runs `TestYooAsset`, which logs an observers RPC on spawn.
