using UnityEngine;

namespace CoffeeBean
{
    /// <summary>框架运行期上下文，随 Bootstrap 创建，供模块在生命周期中访问。</summary>
    public sealed class CoffeeBeanContext
    {
        internal CoffeeBeanContext(CoffeeBeanConfig config)
        {
            Config = config != null ? config : CoffeeBeanConfig.CreateDefault();
            Services = new ServiceRegistry();
            Modules = new CoffeeBeanRegistry();
        }

        /// <summary>服务注册表：模块间解耦的服务定位。</summary>
        public ServiceRegistry Services { get; }

        /// <summary>模块注册表：已安装模块清单与依赖查询。</summary>
        public CoffeeBeanRegistry Modules { get; }

        /// <summary>运行期配置（模块开关等）。</summary>
        public CoffeeBeanConfig Config { get; }

        public void Log(string message) => Debug.Log($"[CoffeeBean] {message}");

        public void LogWarning(string message) => Debug.LogWarning($"[CoffeeBean] {message}");

        public void LogError(string message) => Debug.LogError($"[CoffeeBean] {message}");
    }
}
