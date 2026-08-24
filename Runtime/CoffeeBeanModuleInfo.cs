using System;
using System.Collections.Generic;

namespace CoffeeBean
{
    /// <summary>运行期模块信息（由程序集扫描得到）。</summary>
    public sealed class CoffeeBeanModuleInfo
    {
        internal CoffeeBeanModuleInfo(CoffeeBeanModuleAttribute attribute, Type moduleType)
        {
            Id = attribute.Id;
            Version = attribute.Version;
            DisplayName = string.IsNullOrEmpty(attribute.DisplayName) ? attribute.Id : attribute.DisplayName;
            Description = attribute.Description ?? string.Empty;
            Dependencies = attribute.Dependencies ?? Array.Empty<string>();
            MinCoreVersion = attribute.MinCoreVersion;
            ModuleType = moduleType;
            Enabled = true;
        }

        /// <summary>模块唯一标识（package.json 的 name）。</summary>
        public string Id { get; }

        /// <summary>模块版本（package.json 的 version）。</summary>
        public string Version { get; }

        public string DisplayName { get; }

        public string Description { get; }

        /// <summary>直接依赖的其他模块 id。</summary>
        public IReadOnlyList<string> Dependencies { get; }

        /// <summary>所需 Core 的最低版本（可为空）。</summary>
        public string MinCoreVersion { get; }

        /// <summary>是否启用（由 CoffeeBeanConfig 控制，默认启用）。</summary>
        public bool Enabled { get; internal set; }

        /// <summary>模块实例（实现 ICoffeeBeanModule 时才有）。</summary>
        public ICoffeeBeanModule Instance { get; private set; }

        /// <summary>是否实现了 ICoffeeBeanModule（有生命周期回调）。</summary>
        public bool HasLifecycle => ModuleType != null;

        internal Type ModuleType { get; }

        internal ICoffeeBeanModule CreateInstance()
        {
            if (Instance == null && ModuleType != null)
                Instance = (ICoffeeBeanModule)Activator.CreateInstance(ModuleType);
            return Instance;
        }

        public override string ToString() => $"{DisplayName} ({Id} {Version})";
    }
}
