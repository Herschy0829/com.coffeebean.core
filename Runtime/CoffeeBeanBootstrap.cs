using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 入口场景组件：Awake 时引导框架，OnDestroy 时反序关闭。
    /// 在入口场景中创建一个空物体挂上此组件即可。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoffeeBeanBootstrap : MonoBehaviour
    {
        [Tooltip("运行期配置；留空则使用默认（启用全部模块）")]
        [SerializeField] private CoffeeBeanConfig config;

        private void Awake()
        {
            CoffeeBeanBootstrapper.Load(config);
        }

        private void OnDestroy()
        {
            CoffeeBeanBootstrapper.Shutdown();
        }
    }
}
