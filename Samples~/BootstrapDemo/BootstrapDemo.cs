using System.Collections.Generic;
using UnityEngine;

namespace CoffeeBean.Samples
{
    /// <summary>
    /// CoffeeBean Core 引导演示：
    /// - 手动引导框架（CoffeeBeanBootstrapper.Load）并观察：已发现模块、模块依赖、服务注册情况
    /// - 展示运行期配置（CoffeeBeanConfig：模块启用/禁用）
    /// - 展示优雅关闭（Shutdown → 模块反序 OnShutdown）
    ///
    /// 使用：场景中新建空物体挂上本组件，运行后点界面按钮。
    /// 注意：如果场景中已挂 CoffeeBeanBootstrap（常驻引导），框架会自动加载，本演示的"引导"按钮会显示已加载。
    /// </summary>
    public sealed class BootstrapDemo : MonoBehaviour
    {
        private readonly List<string> _log = new List<string>();
        private Vector2 _scroll;

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 560, Screen.height - 20));

            GUILayout.Label("<b>CoffeeBean Core Bootstrap Demo</b>", GUILayout.Height(22));

            bool loaded = CoffeeBeanBootstrapper.IsLoaded;
            GUILayout.Label($"框架状态: {(loaded ? "已引导 ✅" : "未引导")}");
            GUILayout.Label($"入口组件常驻: {FindObjectOfType<CoffeeBeanBootstrap>() != null}（CoffeeBeanBootstrap 使用 DontDestroyOnLoad）");

            GUILayout.Space(6);
            if (!loaded)
            {
                if (GUILayout.Button("引导框架（Load）", GUILayout.Height(32))) LoadFramework();
                if (GUILayout.Button("使用自定义配置引导（只启用 core）", GUILayout.Height(32))) LoadFrameworkWithConfig();
            }
            else
            {
                if (GUILayout.Button("关闭框架（Shutdown）", GUILayout.Height(32))) ShutdownFramework();
                DrawModules();
            }

            GUILayout.Space(10);
            GUILayout.Label("日志:");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(220));
            int start = Mathf.Max(0, _log.Count - 20);
            for (int i = start; i < _log.Count; i++) GUILayout.Label(_log[i]);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawModules()
        {
            GUILayout.Space(8);
            GUILayout.Label("已发现模块（依赖拓扑序）:");
            foreach (CoffeeBeanModuleInfo info in CoffeeBeanBootstrapper.Context.Modules.Modules)
            {
                GUILayout.Label($"  {info.DisplayName}  {info.Id}@{info.Version}  启用={info.Enabled}  依赖=[{string.Join(",", info.Dependencies)}]");
            }
        }

        private void LoadFramework()
        {
            // 默认配置：启用全部已发现模块
            CoffeeBeanBootstrapper.Load();
            Log($"框架已引导，发现 {CoffeeBeanBootstrapper.Context.Modules.Modules.Count} 个模块");
        }

        private void LoadFrameworkWithConfig()
        {
            // 自定义配置：只启用 core（其他模块不 Load，但包仍在）
            var config = ScriptableObject.CreateInstance<CoffeeBeanConfig>();
            config.enabledModules = new List<string> { "com.coffeebean.core" };
            CoffeeBeanBootstrapper.Load(config);
            Log("框架已引导（仅启用 core 的自定义配置）");
        }

        private void ShutdownFramework()
        {
            CoffeeBeanBootstrapper.Shutdown();
            Log("框架已关闭（模块按依赖反序 OnShutdown）");
        }

        private void Log(string message)
        {
            Debug.Log("[BootstrapDemo] " + message);
            _log.Add(message);
            while (_log.Count > 40) _log.RemoveAt(0);
        }
    }
}
