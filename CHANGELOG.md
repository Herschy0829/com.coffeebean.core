# Changelog

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
