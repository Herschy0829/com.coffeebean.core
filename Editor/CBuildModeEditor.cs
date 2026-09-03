using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 构建模式（Beta/Release）符号管理器 —— CoffeeBean Hub 切换工具的核心逻辑（可测试）。
    /// - Beta    ：symbols 含 COFFEEBEAN_DEV_TOOLS + COFFEEBEAN_LOG（测试工具 + 日志进包）
    /// - Release ：移除这两个宏（工具剔除 + 日志剥离；默认安全态）
    /// 仅增删模式宏，**保留既有符号**（如 COFFEEBEAN_CORE）。
    /// 注意：Editor 编译恒带 UNITY_EDITOR，日志在编辑器里始终可用（见 CLog/CGameBuild）。
    /// </summary>
    public static class CBuildModeEditor
    {
        public const string DevToolsDefine = "COFFEEBEAN_DEV_TOOLS";
        public const string LogDefine = "COFFEEBEAN_LOG";

        public enum Mode { Beta, Release }

        /// <summary>Beta 模式的两个宏。</summary>
        public static readonly string[] BetaDefines = { DevToolsDefine, LogDefine };

        /// <summary>查询目标组当前模式（含任一 Beta 宏即 Beta）。</summary>
        public static Mode CurrentMode(NamedBuildTarget target)
        {
            var symbols = GetSymbolSet(target);
            return symbols.Contains(DevToolsDefine) || symbols.Contains(LogDefine) ? Mode.Beta : Mode.Release;
        }

        /// <summary>把目标组切到指定模式（保留既有符号）。</summary>
        public static void ApplyMode(NamedBuildTarget target, Mode mode)
        {
            var symbols = GetSymbolSet(target);
            foreach (var d in BetaDefines)
            {
                if (mode == Mode.Beta) symbols.Add(d);
                else symbols.Remove(d);
            }
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
        }

        /// <summary>应用到当前激活平台组。</summary>
        public static void ApplyModeCurrent(Mode mode)
            => ApplyMode(ActiveTarget(), mode);

        /// <summary>应用到所有可用目标组（跳过 Unknown 与已弃用组）。</summary>
        public static int ApplyModeAll(Mode mode)
        {
            int n = 0;
            foreach (var t in AllTargets())
            {
                ApplyMode(t, mode);
                n++;
            }
            return n;
        }

        /// <summary>当前激活平台组（编辑器编译用的 symbols 组）。</summary>
        public static NamedBuildTarget ActiveTarget()
            => NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);

        /// <summary>所有可用目标组（跳过 Unknown/Obsolete）。</summary>
        public static IEnumerable<NamedBuildTarget> AllTargets()
        {
            foreach (BuildTargetGroup g in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (g == BuildTargetGroup.Unknown) continue;
                if (IsObsolete(g)) continue;
                yield return NamedBuildTarget.FromBuildTargetGroup(g);
            }
        }

        static HashSet<string> GetSymbolSet(NamedBuildTarget target)
        {
            string symbols = PlayerSettings.GetScriptingDefineSymbols(target);
            return new HashSet<string>(symbols.Split(';').Where(s => !string.IsNullOrEmpty(s)));
        }

        static bool IsObsolete(BuildTargetGroup g)
        {
            var field = typeof(BuildTargetGroup).GetField(g.ToString());
            if (field == null) return true;
            return field.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0;
        }

        /// <summary>描述文本（Hub 提示用）。</summary>
        public static string Describe(Mode mode)
            => mode == Mode.Beta
                ? "Beta：测试工具 + 日志进包（开发/测试包）"
                : "Release：工具剔除 + 日志剥离（提审/上架包）";
    }
}
