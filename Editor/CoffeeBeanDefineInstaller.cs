using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// Core 集成宏安装器：确保 `COFFEEBEAN_CORE` 在 PlayerSettings 全局脚本定义中。
    ///
    /// 各模块的 Bridge 程序集用 `defineConstraints: ["COFFEEBEAN_CORE"]` 条件编译
    /// （安装 Core 时才参与编译，未安装则跳过）。此宏由 Core 自身在编辑器初始化时
    /// 自动加入全局 define symbols —— 装了 Core 才定义，未装不污染工程。
    /// </summary>
    [InitializeOnLoad]
    public static class CoffeeBeanDefineInstaller
    {
        private const string CoreDefine = "COFFEEBEAN_CORE";

        static CoffeeBeanDefineInstaller()
        {
            EditorApplication.delayCall += EnsureDefine;
        }

        /// <summary>确保宏存在于全局脚本定义（幂等；Core 模块自身存在时调用）。</summary>
        public static void EnsureDefine()
        {
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup);
            var set = new HashSet<string>(
                symbols.Split(';').Where(s => !string.IsNullOrEmpty(s)));

            if (set.Add(CoreDefine))
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup,
                    string.Join(";", set));
                UnityEngine.Debug.Log("[CoffeeBean] COFFEEBEAN_CORE 宏已启用（模块 Bridge 集成生效）。");
            }
        }

        /// <summary>测试用：查询宏是否已定义。</summary>
        internal static bool IsDefinePresent()
        {
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup);
            return symbols.Split(';').Contains(CoreDefine);
        }
    }
}
