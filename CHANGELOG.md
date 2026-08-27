# Changelog

## [0.1.22] - 2026-08-27

### Added
- 模块目录新增 `com.coffeebean.save`（v0.1.0，存档模块：MemoryPack 序列化 + AES 加密 + 原子写 / 损坏回退 / 自动存档节流 / 版本迁移）

## [0.1.21] - 2025-xx-xx

### Changed
- 模块目录同步（统一命名空间）：events → v0.3.0、tools → v0.5.0、net/pooling/fsm/excel/purchase → v0.2.0
- **统一命名空间**：全部类型迁移到 `CoffeeBean` 根命名空间（业务只需 `using CoffeeBean;` 即可使用所有模块主类型），模块内部辅助 / 测试 / 示例保留 `CoffeeBean.X` 子命名空间（父命名空间自动可见）
- **破坏性变更**：旧 `using CoffeeBean.X;` 需移除（类型已上移到根命名空间）

## [0.1.20] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.5（多语言表加密无乱码确认）

## [0.1.19] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.4（配置 JSON 混淆加密，默认开启）

## [0.1.18] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.3（JSON 生成进 Resources，运行时加载修复）

## [0.1.17] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.2（文件夹批量 + 增量生成 + 二级预览窗口）

## [0.1.16] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.1（多 Sheet / 分章节 / 列说明注释）

## [0.1.15] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.excel`（v0.1.0，Excel 配置表工具链，Editor-only）

### Changed
- 模块目录同步：`com.coffeebean.purchase` latest → v0.1.6（Excel 解析迁移到 excel 模块）

## [0.1.14] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.fsm`（v0.1.0，状态机：泛型 CStateMachine + 全局状态，独立无依赖）

## [0.1.13] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.pooling`（v0.1.0，对象池：CPool 纯 C# 泛型池 + CGameObjectPool Prefab 池，独立无依赖）

## [0.1.12] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.net`（v0.1.0，网络模块：HTTP / TCP / WebSocket，依赖 tools）

## [0.1.11] - 2025-xx-xx

### Added
- **MinCoreVersion 运行时校验**：模块声明的 Core 最低版本不满足时，该模块 fail-fast（不加载）并输出明确错误日志
  （新工具类 `CoffeeBeanVersion`：语义化版本解析 / 比较 / 最低版本判断，Module Manager 与运行期校验共用同一套版本逻辑）
- 单元测试：`CoffeeBeanVersionTests`（解析 / 比较 / IsSatisfied，含缺段补零、非法输入退化字典序）

### Changed
- Module Manager 的版本解析 / 比较收敛到 `CoffeeBeanVersion`（去重，行为不变）
- 模块目录同步：`com.coffeebean.tools` latest → v0.4.1、`com.coffeebean.events` latest → v0.2.1、`com.coffeebean.purchase` latest → v0.1.5

## [0.1.10] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.tools` latest → v0.4.0

## [0.1.9] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.tools` latest → v0.3.0

## [0.1.8] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.tools` latest → v0.2.0

## [0.1.7] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.tools`（v0.1.0，工具模块，公开仓库）

## [0.1.6] - 2025-xx-xx

### Added
- **示例：Bootstrap Demo**（`Samples~/BootstrapDemo`）：手动引导/关闭、已发现模块清单、运行期配置演示
- 模块目录更新：`com.coffeebean.events` latest → v0.2.0、`com.coffeebean.purchase` latest → v0.1.4（Module Manager 会提示更新）

## [0.1.5] - 2025-xx-xx

### Fixed
- `CoffeeBeanBootstrap` 跨场景常驻：`DontDestroyOnLoad` + 单例保护
  （修复 Loading 场景跳转 Main 时框架被 OnDestroy 关闭、Context 丢失的问题；
  场景中重复挂载自动销毁多余实例）

## [0.1.4] - 2025-xx-xx

### Changed
- Module Manager：已安装的模块不再出现在 Available（未安装）列表，更新/卸载统一在 Installed 面板
- Module Manager：已安装列表改用 `Client.List` 异步拉取最新注册快照（修复安装/更新后版本号不刷新），
  版本显示以 manifest 引用 tag 为准

## [0.1.3] - 2025-xx-xx

### Added
- Module Manager 新增**检查更新**：比对已安装模块的 git 引用 tag 与 registry 的 latest
  （语义化版本比较，v0.1.9 < v0.1.10），可在窗口内一键更新 / 全部更新
- Installed 面板显示当前 ref 与"有更新"提示，逐行 Update 按钮
- 单元测试：版本 tag 解析与比较逻辑

## [0.1.2] - 2025-xx-xx

### Fixed
- 修复 Module Manager 窗口创建时报错：EditorWindow 字段初始化器里调用 `EditorPrefs.GetString`
  违反 ScriptableObject 序列化规则（UnityException）→ 移到 `OnEnable()` 加载

## [0.1.1] - 2025-xx-xx

### Fixed
- Module Manager 窗口默认尺寸过小（现在默认 900x560，最小 760x420）
- Module Manager 只管理 `com.coffeebean.*` 模块，不再列出其他来源的 git 包
- 模块目录随版本更新：`v0.1.1` 起内置 registry 包含 events + purchase

## [0.1.0] - 2025-xx-xx

### Added
- 初始骨架：模块标识特性 `CoffeeBeanModuleAttribute`
- 模块注册表 `CoffeeBeanRegistry`（程序集扫描 / 依赖查询）
- 引导器 `CoffeeBeanBootstrapper`（依赖拓扑排序、环检测、生命周期）
- 服务注册表 `ServiceRegistry`（模块间解耦）
- 运行期配置 `CoffeeBeanConfig`（模块启用/禁用开关）
- 编辑器 `Module Manager` 窗口与安装/卸载 API
- 官方模块目录 `RegistrySource`（内置 + 远程覆盖）
