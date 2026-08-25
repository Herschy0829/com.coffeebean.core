# Bootstrap Demo（框架引导示例）

演示 CoffeeBean Core 的引导机制：

- 手动引导 / 关闭框架（`CoffeeBeanBootstrapper.Load / Shutdown`）
- 已发现模块列表（模块 ID / 版本 / 依赖 / 启用状态）
- 运行期配置 `CoffeeBeanConfig`（模块启用/禁用）
- 入口组件常驻说明（`CoffeeBeanBootstrap` 使用 DontDestroyOnLoad 跨场景存活）

## 使用

1. Package Manager → `com.coffeebean.core` → **Samples → Bootstrap Demo → Import**
2. 场景中新建空物体，挂上 **`BootstrapDemo`**
3. 运行（Play），点「引导框架」→ 观察模块列表 → 点「关闭框架」

> 若你的入口场景已挂 `CoffeeBeanBootstrap`，框架会自动引导，这里会显示"已引导"并直接列出模块。
> 若还装了 events/purchase 模块，引导后它们会出现在模块列表里（其服务可通过 `Context.Services.Get<...>()` 获取）。

## 文件

| 文件 | 说明 |
|------|------|
| `BootstrapDemo.cs` | 主演示组件：引导/关闭 + 模块清单 + 自定义配置演示 |
