# Changelog

## [0.1.0] - 2025-xx-xx

### Added
- 初始骨架：模块标识特性 `CoffeeBeanModuleAttribute`
- 模块注册表 `CoffeeBeanRegistry`（程序集扫描 / 依赖查询）
- 引导器 `CoffeeBeanBootstrapper`（依赖拓扑排序、环检测、生命周期）
- 服务注册表 `ServiceRegistry`（模块间解耦）
- 运行期配置 `CoffeeBeanConfig`（模块启用/禁用开关）
- 编辑器 `Module Manager` 窗口与安装/卸载 API
- 官方模块目录 `RegistrySource`（内置 + 远程覆盖）
