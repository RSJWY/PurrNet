# PurrNet × YooAsset 网络场景同步集成需求

> 基线：`release` 分支，v1.22.0（commit `36f65b29`）。
> 文中所有行号均基于此基线，升级基线后需重新核对。
> 业务侧消费方：tengine-purrnet 工程（YooAsset `com.tuyoogame.yooasset` 3.0.5）。

## 1. 背景与目标

PurrNet 的 ScenesModule 默认场景同步链路深度绑定 Unity `SceneManager` + Build Settings：

- 服务端 `LoadSceneAsync(buildIndex)` 直接调 `SceneManager.LoadSceneAsync`；
- 客户端收到 `SceneActionsBatch` 后按 `scenePathHash` 反查 Build Settings 取 buildIndex，再本地加载——**场景不在 Build Settings 则丢弃动作**；
- 对任意资源系统的唯一官方扩展是 Addressables 集成（编译期 `#if ADDRESSABLES_PURRNET_SUPPORT`，无运行时钩子）。

业务项目场景走 YooAsset 热更（location 寻址、远程 bundle），不进 Build Settings。目标：**在 fork 内以与官方 Addressables 集成完全同构的方式接入 YooAsset**，使网络场景同步（服务器权威加载、SceneID 一致、按场景可见性、中途加入重放、host 镜像、断线清理）全部继承 PurrNet 原生能力，业务层零补偿代码。

## 2. 官方参照模式（本需求的模板）

`ScenesModule.Addressables.cs`（release 上 700 行）确立的模式：

1. **网络身份 = 字符串**（Addressables 用资源 GUID，YooAsset 用 location），随动作经网络同步；
2. 服务端 `LoadAddressableSceneAsync`：`GetNextID()` 分配 SceneID → `_history.AddLoadAddressableAction` 记历史 → 本地加载 → 注册 pending；
3. 客户端 `ProcessLoadAddressableAction`：防重检查 → **各自调** `Addressables.LoadSceneAsync(guid)` → 完成后 `AddScene(scene, settings, idToAssign)` 绑定 SceneID；
4. `AddScene` 之后的一切（HierarchyV2 创建、ScenePlayersModule 玩家进出、spawn 可见性、中途加入重放）由核心模块自动驱动，**无需任何额外代码**；
5. host 模式：client 模块直接镜像 server 模块状态（共享加载 handle，`ownsHandle=false` 防双卸载）；
6. 重连重复投递防重（sceneID 已存在或 pending 则跳过加载）。

## 3. 设计决策

| 决策点 | 结论 | 理由 |
|---|---|---|
| 网络身份 | 复用 `LoadAddressableSceneAction`，其 `guid` string 字段装 YooAsset location | `SceneHistory.cs` **零改动**（结构体/枚举/序列化/Add 方法均无条件编译）；history 重放天然兼容 |
| 编译门控 | 新宏 `YOOASSET_PURRNET_SUPPORT`，asmdef `versionDefines` 绑定 `com.tuyoogame.yooasset` | 模仿官方对 Addressables 的做法；不装 YooAsset 的项目使用本 fork 照常编译 |
| 卸载语义 | YooAsset `SceneHandle.UnloadSceneAsync()` 对应 `Addressables.UnloadSceneAsync` | 模式同构 |
| 事件命名 | **待决策**：复用 `onAddressableSceneStartLoading/Loaded`（diff 最小）或新增 `onYooAssetScene*` 别名（语义清晰） | 倾向复用 + 注释说明 guid 字段实为 location |
| 对 TEngine 依赖 | **禁止**。只调 YooAsset 静态 API（`YooAssets.GetPackage(...)`） | 保持 fork 对任意项目通用 |
| 包名/版本声明 | `package.json` dependencies 增加 `"com.tuyoogame.yooasset": "3.0.5"` | 消费方自动解析依赖 |

## 4. 改动清单（精确到行号，基线 v1.22.0）

### 4.1 新增 `Assets/PurrNet/Runtime/CoreModules/Scenes/ScenesModule.YooAsset.cs`

整体 `#if YOOASSET_PURRNET_SUPPORT` 包裹，模板 = `ScenesModule.Addressables.cs`（700 行），预计 400~500 行。需实现：

| 成员 | 对应模板 | 说明 |
|---|---|---|
| `PendingYooAssetSceneOperation` struct | `PendingAddressableSceneOperation` | `location: string`、`handle: YooAsset.SceneHandle`、`idToAssign`、`settings`、`ownsHandle` |
| `LoadYooAssetSceneAsync(string location, PurrSceneSettings settings)` | `LoadAddressableSceneAsync(string guid, ...)`（:512） | 服务端入口；Single 模式校验 NM 在 DDOL；`GetNextID` + `_history.AddLoadAddressableAction`（guid 填 location）+ 本地 `package.LoadSceneAsync(location, mode, physicsMode, ...)` + pending 注册 + host 镜像 |
| `ProcessLoadYooAssetAction(LoadAddressableSceneAction action)` | `ProcessLoadAddressableAction`（:93） | 客户端处理；防重 → YooAsset 加载 → pending 注册 → host 镜像 |
| `TryUnloadYooAssetScene(SceneID, UnloadSceneOptions)` | `TryUnloadAddressableScene`（:181 一带） | `handle.UnloadSceneAsync()` |
| `UnloadYooAssetSceneAsync(...)` | `UnloadAddressableSceneAsync`（:177） | 公开卸载 API |
| `IsScenePendingYooAsset(SceneID)` | `IsScenePendingAddressable` | 供核心 :577 分支 |
| `TryReconcileLoadedYooAssetTransferScene` / `RemoveStaleYooAssetTransferScenes` | 同名 Addressables 版（:300 一带） | 服务器迁移/重连 reconcile，供核心 :781/:803 分支 |
| `partial void ProcessCompletedYooAssetLoads()` | `ProcessCompletedAddressableLoads`（实现 :59） | 完成回调轮询：handle 完成后 `AddScene` + 触发事件 |
| `partial void RebuildYooAssetHistoryFromLoadedScenes()` | 同名（实现 :306） | Enable 时从已加载场景重建注册表 |
| 查询 API | `IsAddressableSceneLoaded`（:601）等 | `IsYooAssetSceneLoaded(location)`、`IsYooAssetSceneLoading`、`TryGetSceneIdByYooAssetLocation` |
| 断线清理 | `DiscardPendingAddressableOperations` | 尊重 `networkRules.ShouldCleanupScenesOnDisconnect()` |

### 4.2 修改 `Assets/PurrNet/Runtime/CoreModules/Scenes/ScenesModule.cs`（1656 行）

7 处 `#if ADDRESSABLES_PURRNET_SUPPORT` 旁增加 `#elif YOOASSET_PURRNET_SUPPORT` 分支 + 2 处 partial 声明/调用：

| 行号 | 位置 | YooAsset 分支内容 |
|---|---|---|
| :577 | `IsScenePending()` | 追加 `IsScenePendingYooAsset(sceneId)` |
| :670 | `HandleNextSceneAction` Load case | `ProcessLoadYooAssetAction(action.loadAddressableSceneAction)` |
| :698 | `HandleNextSceneAction` Unload case | `TryUnloadYooAssetScene(idx, options)` 成功则 Dequeue |
| :742 | 过滤（transfer/rejoin） | `targetYooAssetScenes` 字典声明 |
| :781 | 同上循环内 | 记录 target + `TryReconcileLoadedYooAssetTransferScene` |
| :803 | 同上 | `RemoveStaleYooAssetTransferScenes` |
| :1262 | 服务端 `UnloadSceneAsync` | `TryUnloadYooAssetScene` 命中则提前返回 |
| :328 一带 | `Enable()` 调用点 | 追加 `RebuildYooAssetHistoryFromLoadedScenes();` |
| :1348 一带 | partial 声明区 | 追加两个 `partial void` 声明；:1353 调用点追加 `ProcessCompletedYooAssetLoads();` |

### 4.3 零改动文件

- `SceneHistory.cs`（253 行）：`LoadAddressableSceneAction`（:92）、`SceneActionType.LoadAddressable`（:11）、序列化（:34-35）、`AddLoadAddressableAction`（:231）全部无条件编译，直接复用。
- `ScenesModule.Addressables.cs`：**不碰**（官方高频改动区，保持 rebase 零冲突）。

### 4.4 修改 `Assets/PurrNet/Runtime/PurrNet.Runtime.asmdef`

- `versionDefines` 参照 :60 的 addressables 条目，增加：
  ```json
  { "name": "com.tuyoogame.yooasset", "expression": "", "define": "YOOASSET_PURRNET_SUPPORT" }
  ```
- references 增加 YooAsset 运行时程序集（以包内 `Runtime/YooAsset.asmdef` 名称为准，通常为 `YooAsset`）。

### 4.5 修改 `Assets/PurrNet/package.json`

`dependencies` 增加 `"com.tuyoogame.yooasset": "3.0.5"`。

## 5. 集成后完整加载时序（验收基准）

```
服务器                                     客户端
  │ LoadYooAssetSceneAsync("battle_01", {mode:Single, isPublic:true})
  │ ├ GetNextID()=5；history 记 {guid:"battle_01", sceneID:5}
  │ ├ 服务器自己 YooAsset 加载 ─完成→ AddScene(5) → HierarchyV2 就绪
  │ │                                     （触发 scenesModule.onSceneLoaded, asServer=true）
  │ ├──(下一 tick) SceneActionsBatch────→  入 _actionsQueue → FixedUpdate 串行处理
  │ │                                     防重 → YooAsset.LoadSceneAsync("battle_01")
  │ │                                       （内部：查 manifest→缺 bundle 走 CDN 下载→加载）
  │ │                                     ─完成→ AddScene(5) → HierarchyV2 就绪
  │ │                                       （本地触发 scenePlayers.onPlayerLoadedScene, asServer=false）
  │ │←──(内部消息 ClientFinishedLoadingScene)─┘
  │ │                                     （服务端触发 scenePlayers.onPlayerLoadedScene, asServer=true）
  │ 业务等待：IsPlayerLoadedInScene 全 true
  │ ├ networkManager.Instantiate(...)       场景内 TryGetSceneID 命中 → 自动网络生成
  │ ├──(spawn 包，场景可见者收)──────────→  场景内网络对象生成
```

中途加入：新玩家连接 → 服务器下发过滤后的完整 history（`FirstSceneActionsBatch`）→ 按同一流程加载 `battle_01` → 绑同一 SceneID → 场景已有网络对象随成员关系自动 spawn。重连防重由核心保证。

## 6. 事件与通知对照（业务侧可用钩子）

| 时机 | 事件 | 说明 |
|---|---|---|
| 任一端场景加载完成 | `scenesModule.onPreSceneLoaded / onSceneLoaded / onPostSceneLoaded` | `(SceneID, bool asServer)`，模块本地事件 |
| YooAsset 加载开始/完成 | `onAddressableSceneStartLoading / onAddressableSceneLoaded`（复用，guid 参数实为 location） | `(SceneID, string, bool)`，驱动加载 UI 与进度 |
| 客户端上报加载完成 | 内部消息 `ClientFinishedLoadingScene`，服务端事件 `scenePlayersModule.onPlayerLoadedScene` | `(PlayerID, SceneID, asServer=true)`，"等人齐"用 |
| 玩家进出场景（成员/可见性） | `scenePlayersModule.onPlayerJoinedScene / onPlayerLeftScene` | 决定 spawn 包接收范围 |
| 玩家对对象可见 | HierarchyV2 `onObserverAdded` | 细粒度确认，一般不需要 |

注意：模块事件为本地 C# 事件，非网络 Broadcast；游戏语义广播（"人齐了，开始倒计时"）由业务层 `[ObserversRpc]`/Broadcast 自行发送。host 双模块并存，订阅 server 模块实例并过滤 `asServer==true` 防双计。`onPlayerLoadedScene` 无内置超时，业务需自加踢人/降级保护。

## 7. 约束与注意事项

1. **不依赖 TEngine**：fork 内只调 YooAsset API；加载进度 UI 等业务包装留在业务工程热更层。
2. **headless 服务器也必须初始化 YooAsset** 并能解析同一 location（含资源清单）；所有端清单版本一致由业务强更流程保证。
3. **AOT 侧代码**：本文件位于 PurrNet.Runtime 程序集（partial class 必须同程序集），不参与 HybridCLR 热更；改动需重新出包。何时切场景、传什么 location 属业务层，可热更。
4. **physicsMode 映射**：`PurrSceneSettings.physicsMode` 直接透传 YooAsset `LoadSceneAsync` 的 `LocalPhysicsMode` 参数。
5. **SceneHandle 生命周期**：卸载必须走 handle（不得直调 `SceneManager.UnloadScene`），host 镜像共享 handle 时以 `ownsHandle` 判定归属，防双卸载。
6. **预下载**：切换前业务层可用 YooAsset 下载器预拉 bundle，`LoadSceneAsync` 即为纯本地加载。
7. 与 TEngine `GameModule.Scene` 双轨并存：网络场景走本集成（PurrNet 持有 handle），非网络场景（主菜单等）仍走 GameSceneModule。

## 8. 验收用例（改造 `Assets/Examples/AddressablesTest` → `YooAssetTest`）

1. 服务端 `LoadYooAssetSceneAsync(location, Single+isPublic)`：双端（ParRelSync 多开）各自经 YooAsset 加载，`TryGetSceneID` 双端返回一致 SceneID；
2. 场景内 `networkManager.Instantiate` 生成的对象双端可见、状态同步正常；
3. 中途加入：第三客户端连接后自动加载同场景并收到场景内已有对象；
4. 重连：断线重连不重复加载（防重生效），SceneID 不漂移；
5. host 模式：host 即服务器，场景切换正常、无双重加载/双重卸载；
6. 卸载：`UnloadYooAssetSceneAsync` 后对象清理、事件链完整、再次加载正常；
7. 断线清理：客户端断开按 `NetworkRules` 配置清理或保留场景；
8. 不装 YooAsset 的纯净工程编译通过（`YOOASSET_PURRNET_SUPPORT` 未定义时零影响）；
9. Addressables 原路径回归：装 Addressables 的工程中原 `AddressablesTest` 行为不变。

## 9. 维护策略

- **远端**：`origin = RSJWY/PurrNet`（fork，推这里）；`upstream = PurrNet/PurrNet`（官方，**push 已禁用**，仅拉取）。
- **基线**：只跟 `release` 稳定线（当前 v1.22.0）；beta 线（master/dev）不跟。
- **升级流程**：`git fetch upstream` → 确认新 release tag → `yooasset` 分支 rebase → 核对 4.2 表格行号漂移 → 跑第 8 节验收回归。
- **冲突面控制**：diff 限定为"一个新文件 + 若干 `#elif` 守卫行 + asmdef/package.json"；`ScenesModule.Addressables.cs` 永不改。
- 业务工程消费：`Packages/manifest.json` 引用
  `"dev.purrnet.purrnet": "https://github.com/RSJWY/PurrNet.git?path=/Assets/PurrNet#<分支或tag>"`。
