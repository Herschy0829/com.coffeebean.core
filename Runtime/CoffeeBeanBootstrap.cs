using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 入口场景组件：Awake 时引导框架，应用退出时反序关闭。
    ///
    /// 跨场景常驻：使用 DontDestroyOnLoad，从 Loading 场景跳转 Main 场景时不会被销毁，
    /// 框架上下文（服务注册表/模块）全程保持存活。带单例保护：场景中重复挂载时自动销毁多余实例。
    ///
    /// 使用：在入口场景（或常驻场景）中创建一个空物体挂上此组件即可。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoffeeBeanBootstrap : MonoBehaviour
    {
        private static CoffeeBeanBootstrap _instance;

        [Tooltip("运行期配置；留空则使用默认（启用全部模块）")]
        [SerializeField] private CoffeeBeanConfig config;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[CoffeeBean] 检测到重复的 CoffeeBeanBootstrap，已销毁多余实例。");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject); // 跨场景常驻，场景切换不销毁框架

            CoffeeBeanBootstrapper.Load(config);
        }

        private void OnDestroy()
        {
            // 常驻物体只在应用退出/域重载/显式销毁时触发；Shutdown 幂等，可安全重复调用
            CoffeeBeanBootstrapper.Shutdown();
            if (_instance == this) _instance = null;
        }
    }
}
