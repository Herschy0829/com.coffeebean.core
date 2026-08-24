using System;
using System.Collections.Generic;
using System.Reflection;

namespace CoffeeBean
{
    /// <summary>
    /// 模块注册表：扫描已加载程序集中的 [CoffeeBeanModule] 特性，构建已安装模块清单。
    /// </summary>
    public sealed class CoffeeBeanRegistry
    {
        private readonly List<CoffeeBeanModuleInfo> _modules = new List<CoffeeBeanModuleInfo>();
        private readonly Dictionary<string, CoffeeBeanModuleInfo> _byId = new Dictionary<string, CoffeeBeanModuleInfo>();

        /// <summary>所有发现的模块（按程序集扫描顺序）。</summary>
        public IReadOnlyList<CoffeeBeanModuleInfo> Modules => _modules;

        /// <summary>扫描当前 AppDomain 已加载程序集。重复调用会先清空再重建。</summary>
        public void Scan()
        {
            _modules.Clear();
            _byId.Clear();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                CoffeeBeanModuleAttribute attribute = assembly.GetCustomAttribute<CoffeeBeanModuleAttribute>();
                if (attribute == null) continue;

                Type moduleType = FindModuleType(assembly);
                var info = new CoffeeBeanModuleInfo(attribute, moduleType);
                _modules.Add(info);
                _byId[info.Id] = info;
            }
        }

        public bool TryGet(string id, out CoffeeBeanModuleInfo info) => _byId.TryGetValue(id, out info);

        public bool IsInstalled(string id) => _byId.ContainsKey(id);

        /// <summary>查找所有直接依赖指定模块的已安装模块。</summary>
        public List<CoffeeBeanModuleInfo> GetDependents(string moduleId)
        {
            var result = new List<CoffeeBeanModuleInfo>();
            foreach (CoffeeBeanModuleInfo module in _modules)
            {
                foreach (string dep in module.Dependencies)
                {
                    if (dep == moduleId)
                    {
                        result.Add(module);
                        break;
                    }
                }
            }
            return result;
        }

        private static Type FindModuleType(Assembly assembly)
        {
            try
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(ICoffeeBeanModule).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract)
                        return type;
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 忽略无法完整加载类型的程序集
            }
            return null;
        }
    }
}
