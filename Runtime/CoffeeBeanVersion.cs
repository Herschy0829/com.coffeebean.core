using System;

namespace CoffeeBean
{
    /// <summary>
    /// 语义化版本工具（框架内版本比较的统一入口）。
    ///
    /// 支持 "v" 前缀与 1~4 段数字（缺段按 0 补），例如 "0.1.10" / "v1.0" / "1.2.3.4"。
    /// 用途：模块声明的 <see cref="CoffeeBeanModuleAttribute.MinCoreVersion"/> 与已安装 Core 版本比较、
    /// Module Manager 的 git 引用 tag 比较。
    /// </summary>
    public static class CoffeeBeanVersion
    {
        /// <summary>
        /// 解析版本字符串为数字段数组；非法输入返回 false。
        /// </summary>
        public static bool TryParse(string version, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrEmpty(version)) return false;

            string s = version;
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);

            string[] segments = s.Split('.');
            if (segments.Length == 0 || segments.Length > 4) return false;

            parts = new int[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                if (!int.TryParse(segments[i], out parts[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// 比较两个版本字符串：a &lt; b 返回负值，相等返回 0，a &gt; b 返回正值。
        /// 任一侧非法时退化为字典序比较（保证全序，便于排序 / 判断）。
        /// </summary>
        public static int Compare(string a, string b)
        {
            if (!TryParse(a, out int[] pa) || !TryParse(b, out int[] pb))
                return string.CompareOrdinal(a ?? string.Empty, b ?? string.Empty);

            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length ? pa[i] : 0;
                int vb = i < pb.Length ? pb[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        /// <summary>
        /// 当前版本是否满足最低版本要求（currentVersion &gt;= minVersion）。
        /// minVersion 为空视为无要求（恒满足）；currentVersion 为空时视为不满足。
        /// </summary>
        public static bool IsSatisfied(string currentVersion, string minVersion)
        {
            if (string.IsNullOrEmpty(minVersion)) return true;
            if (string.IsNullOrEmpty(currentVersion)) return false;
            return Compare(currentVersion, minVersion) >= 0;
        }
    }
}
