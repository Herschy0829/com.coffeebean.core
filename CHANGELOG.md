# Changelog

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
