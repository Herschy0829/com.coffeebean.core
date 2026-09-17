using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CoffeeBean.EditorTools;
using NUnit.Framework;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace CoffeeBean.Tests
{
    /// <summary>Module Manager 更新检测逻辑测试（版本 tag 解析与比较 + 框架版本一致性）。</summary>
    public class ModuleManagerVersionTests
    {
        /// <summary>
        /// 框架版本一致性回归锁。
        ///
        /// 这条测试是有来历的：窗口品牌栏原先硬编码 <c>const string FrameworkVersion = "0.1.43"</c>，
        /// 而 core 后来一路发到 0.1.55 —— 界面上却一直显示 0.1.43，漂移了 12 个版本。
        /// 现在用**三个独立来源**交叉验证，任何一处漏改都会红：
        /// 1) package.json 文件里的 version；
        /// 2) 模块标记 [CoffeeBeanModule] 的 version（Core 用于 MinCoreVersion 比较的那个）；
        /// 3) 窗口实际显示的 FrameworkVersion。
        /// </summary>
        [Test]
        public void FrameworkVersion_MatchesPackageJsonAndModuleMarker()
        {
            ModuleManagerWindow.ResetVersionCache();

            PackageInfo info = PackageInfo.FindForAssembly(typeof(CoffeeBeanVersion).Assembly);
            Assert.IsNotNull(info, "应能解析到 core 包（dev 项目通过 file: 引用它）");
            Assert.IsNotEmpty(info.resolvedPath);

            // 来源 1：直接读 package.json 文件（不复用生产代码的解析结果，避免自证）
            string packageJson = Path.Combine(info.resolvedPath, "package.json");
            Assert.IsTrue(File.Exists(packageJson), "找不到 package.json：" + packageJson);
            string jsonVersion = Regex.Match(File.ReadAllText(packageJson), "\"version\"\\s*:\\s*\"([^\"]+)\"")
                .Groups[1].Value;
            Assert.IsNotEmpty(jsonVersion, "package.json 里没解析出版本号");

            // 来源 2：模块标记
            var attr = typeof(CoffeeBeanVersion).Assembly.GetCustomAttribute<CoffeeBeanModuleAttribute>();
            Assert.IsNotNull(attr, "core 程序集上应有 [CoffeeBeanModule] 标记");
            Assert.IsNotEmpty(attr.Version);

            // 来源 3：窗口显示值
            Assert.AreEqual(jsonVersion, attr.Version,
                "package.json 与模块标记的版本必须一致（发版时要同步改）");
            Assert.AreEqual(jsonVersion, ModuleManagerWindow.FrameworkVersion,
                "Module Manager 显示的框架版本必须等于实际包版本（不要再硬编码）");
            Assert.IsFalse(ModuleManagerWindow.HasVersionDrift, "品牌栏不应出现版本漂移警告");
        }

        [Test]
        public void FrameworkVersion_IsNotEmpty()
        {
            ModuleManagerWindow.ResetVersionCache();
            Assert.IsNotEmpty(ModuleManagerWindow.FrameworkVersion);
            Assert.AreNotEqual("?", ModuleManagerWindow.FrameworkVersion, "拿不到版本会退化成一个问号");
        }

        [Test]
        public void TryParseVersion_ValidTags_Parsed()
        {
            Assert.IsTrue(ModuleManagerWindow.TryParseVersion("v0.1.0", out var parts));
            Assert.AreEqual(new[] { 0, 1, 0 }, parts);
            Assert.IsTrue(ModuleManagerWindow.TryParseVersion("1.2.3", out var parts2));
            Assert.AreEqual(new[] { 1, 2, 3 }, parts2);
        }

        [Test]
        public void TryParseVersion_InvalidTags_Rejected()
        {
            Assert.IsFalse(ModuleManagerWindow.TryParseVersion("main", out _));
            Assert.IsFalse(ModuleManagerWindow.TryParseVersion("develop", out _));
            Assert.IsFalse(ModuleManagerWindow.TryParseVersion("v1.x", out _));
            Assert.IsFalse(ModuleManagerWindow.TryParseVersion("", out _));
        }

        [Test]
        public void CompareTags_NumericNotLexicographic()
        {
            // v0.1.9 < v0.1.10（字典序会判反，必须按数字比较）
            Assert.Less(ModuleManagerWindow.CompareTags("v0.1.9", "v0.1.10"), 0);
            Assert.Greater(ModuleManagerWindow.CompareTags("v0.1.10", "v0.1.9"), 0);
        }

        [Test]
        public void CompareTags_Equal_ReturnsZero()
        {
            Assert.AreEqual(0, ModuleManagerWindow.CompareTags("v0.1.0", "v0.1.0"));
            Assert.AreEqual(0, ModuleManagerWindow.CompareTags("v1.0", "v1.0.0")); // 缺段按 0 补
        }

        [Test]
        public void CompareTags_MajorDominates()
        {
            Assert.Less(ModuleManagerWindow.CompareTags("v0.9.9", "v1.0.0"), 0);
            Assert.Greater(ModuleManagerWindow.CompareTags("v2.0.0", "v1.99.99"), 0);
        }
    }
}
