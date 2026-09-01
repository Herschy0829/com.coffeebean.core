// CoffeeBean 模块标识：Core 自身也是一个模块（框架的根模块，无依赖）。
// 注意：Id/Version 是构造参数（位置参数），DisplayName 等是可选命名参数。
using CoffeeBean;

[assembly: CoffeeBeanModule(
    "com.coffeebean.core",
    "0.1.24",
    DisplayName = "Core",
    Description = "CoffeeBean framework core: module discovery, bootstrap, service registry and module manager."
)]
