# com.coffeebean.core

CoffeeBean 框架核心模块：模块发现、引导（依赖拓扑排序 + 生命周期）、服务注册表，以及模块安装 / 卸载 / 升级的编辑器管理能力。

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.core": "https://github.com/Herschy0829/com.coffeebean.core.git#v0.1.0"
  }
}
```

## 快速开始

1. 安装本模块（或通过 Module Manager 一键安装其他模块）
2. 入口场景创建一个空物体，挂上 `CoffeeBeanBootstrap` 组件
3. 框架自动扫描所有带 `[CoffeeBeanModule]` 特性的程序集，按依赖顺序引导

```csharp
// 在任意代码中获取框架上下文
var context = CoffeeBeanBootstrapper.Context;

// 通过服务注册表获取其他模块提供的服务（解耦模块间依赖）
var eventBus = context.Services.Get<CoffeeBean.Events.EventBus>();
```

## 模块管理（编辑器）

`Window > CoffeeBean > Module Manager`

- **Installed**：已安装的 `com.coffeebean.*` 模块，一键卸载（自动检查依赖方）
- **Available**：官方模块目录（内置 registry，可用远程 URL 覆盖），一键安装 / 升级

## 模块规范速查

每个模块的 Runtime 程序集顶部声明：

```csharp
using CoffeeBean;

[assembly: CoffeeBeanModule(
    "com.coffeebean.events",
    "0.1.0",
    DisplayName = "Events",
    Dependencies = new[] { "com.coffeebean.core" }
)]
```

可选实现生命周期接口 `ICoffeeBeanModule`（`OnLoad` 注册服务 → `OnStart` → 退出反序 `OnShutdown`）。

详见框架设计文档 `docs/design.md`。

## License

[MIT](LICENSE.md)
