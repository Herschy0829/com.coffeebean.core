using System.Collections.Generic;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 运行期模块开关。列表为空 = 启用全部模块；否则只启用列出的模块 id。
    /// 注意：这不卸载包，只控制启动时是否 Load（用于性能裁剪 / A/B / 灰度）。
    /// </summary>
    [CreateAssetMenu(fileName = "CoffeeBeanConfig", menuName = "CoffeeBean/Config")]
    public sealed class CoffeeBeanConfig : ScriptableObject
    {
        [Tooltip("留空 = 启用所有已安装模块；否则只启用列表中的模块 id")]
        public List<string> enabledModules = new List<string>();

        public bool IsModuleEnabled(string moduleId)
            => enabledModules == null || enabledModules.Count == 0 || enabledModules.Contains(moduleId);

        /// <summary>生成一个启用全部模块的默认配置。</summary>
        public static CoffeeBeanConfig CreateDefault()
        {
            var config = CreateInstance<CoffeeBeanConfig>();
            config.enabledModules = new List<string>();
            return config;
        }
    }
}
