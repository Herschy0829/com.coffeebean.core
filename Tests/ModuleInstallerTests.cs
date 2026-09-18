using System.Collections.Generic;
using CoffeeBean.EditorTools;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    /// <summary>
    /// ModuleInstaller 的**结果核对**测试。
    ///
    /// 来历：安装器原来只按"计划"报成功 —— 计划是空操作时照样打印"已安装 N 个模块"。
    /// 实测踩到：远程目录命中 CDN 缓存（max-age=300）拿到旧目录 → 计划里的版本与现状相同
    /// → 请求空操作 → 界面显示"已安装"，manifest 却纹丝不动，白等几分钟。
    /// 现在装完会回读 manifest 比对，对不上就明确说是"未生效 + 可能拿到旧目录"。
    /// </summary>
    public class ModuleInstallerTests
    {
        /// <summary>manifest 里真实存在的引用（dev 工程用 file: 引用 tools）不该被判为缺失。</summary>
        [Test]
        public void FindMissingManifestUrls_PresentUrl_IsNotMissing()
        {
            string present = CThirdPartyProbeUrl();
            if (present == null) Assert.Ignore("dev 工程的 manifest 里没有可用的探针条目");

            List<string> missing = ModuleInstaller.FindMissingManifestUrls(new[] { present });
            Assert.IsEmpty(missing);
        }

        [Test]
        public void FindMissingManifestUrls_AbsentUrl_IsReported()
        {
            string bogus = "https://github.com/Herschy0829/definitely-not-installed.git#v9.9.9";

            List<string> missing = ModuleInstaller.FindMissingManifestUrls(new[] { bogus });
            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(bogus, missing[0]);
        }

        [Test]
        public void FindMissingManifestUrls_MixedAndEmptyInputs()
        {
            string present = CThirdPartyProbeUrl();
            if (present == null) Assert.Ignore("dev 工程的 manifest 里没有可用的探针条目");

            List<string> missing = ModuleInstaller.FindMissingManifestUrls(new[]
            {
                present,
                "https://github.com/Herschy0829/definitely-not-installed.git#v9.9.9",
                null,
                string.Empty,
            });

            Assert.AreEqual(1, missing.Count, "只有真正缺失的那条该被报出来（null/空串跳过）");
        }

        /// <summary>从真实 manifest 里取一条 URL 当探针（tools 一定在）。</summary>
        private static string CThirdPartyProbeUrl()
        {
            string value = null;
            try
            {
                const string path = "Packages/manifest.json";
                if (!System.IO.File.Exists(path)) return null;
                string text = System.IO.File.ReadAllText(path);
                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
                    text, "\"com\\.coffeebean\\.tools\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success) value = match.Groups[1].Value;
            }
            catch
            {
                // 读不到就让调用方跳过
            }
            return value;
        }
    }
}
