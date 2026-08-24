using System;

namespace CoffeeBean
{
    /// <summary>
    /// 标记一个程序集为 CoffeeBean 模块。每个模块的 Runtime 程序集顶部必须声明一次：
    /// <code>
    /// [assembly: CoffeeBeanModule(
    ///     "com.coffeebean.events",
    ///     "0.1.0",
    ///     DisplayName = "Events",
    ///     Dependencies = new[] { "com.coffeebean.core" })]
    /// </code>
    /// 注意：Id/Version 是构造参数（位置参数），必须与 package.json 的 name/version 一致。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class CoffeeBeanModuleAttribute : Attribute
    {
        public CoffeeBeanModuleAttribute(string id, string version)
        {
            Id = id;
            Version = version;
        }

        /// <summary>模块唯一标识，与 package.json 的 name 一致。</summary>
        public string Id { get; }

        /// <summary>模块版本，与 package.json 的 version 一致。</summary>
        public string Version { get; }

        /// <summary>显示名称（默认取 Id）。</summary>
        public string DisplayName { get; set; }

        /// <summary>模块描述。</summary>
        public string Description { get; set; }

        /// <summary>依赖的其他 CoffeeBean 模块 id（与 package.json 的 dependencies 保持一致，只声明直接依赖）。</summary>
        public string[] Dependencies { get; set; } = Array.Empty<string>();

        /// <summary>所需 Core 的最低版本（可选，运行时校验）。</summary>
        public string MinCoreVersion { get; set; }
    }
}
