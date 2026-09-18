using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CoffeeBean.EditorTools;
using NUnit.Framework;
using UnityEditor;
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

        // ========== Core 不能被卸载 ==========

        /// <summary>
        /// Core 可以更新，但**永远不能**被卸载：框架工具中心（窗口）与模块安装器都住在它里面。
        /// 这里是按钮与兜底拦截共用的判定。
        /// </summary>
        [Test]
        public void IsCorePackage_MatchesOnlyCore()
        {
            Assert.IsTrue(ModuleManagerWindow.IsCorePackage("com.coffeebean.core"));
            Assert.IsTrue(ModuleManagerWindow.IsCorePackage("COM.COFFEEBEAN.CORE"), "包名比较应忽略大小写");

            Assert.IsFalse(ModuleManagerWindow.IsCorePackage("com.coffeebean.tools"));
            Assert.IsFalse(ModuleManagerWindow.IsCorePackage("com.coffeebean.corex"), "前缀相似不是同一个包");
            Assert.IsFalse(ModuleManagerWindow.IsCorePackage("core"));
            Assert.IsFalse(ModuleManagerWindow.IsCorePackage(null));
            Assert.IsFalse(ModuleManagerWindow.IsCorePackage(string.Empty));
        }

        // ========== 目录来源 ==========

        /// <summary>
        /// 默认远程目录必须指向官方 Core 仓库 main 上的 registry.json。
        ///
        /// 这条锁的是那个"检查更新谎报最新"的根因：内置目录只随 Core 版本更新，
        /// 一旦默认地址丢了/写错，用户又会退回到"只能看到自己装的 Core 那一版的清单"。
        /// </summary>
        [Test]
        public void DefaultRemoteUrl_PointsAtOfficialRegistryOnMain()
        {
            StringAssert.StartsWith("https://", RegistrySource.DefaultRemoteUrl);
            StringAssert.Contains("Herschy0829/com.coffeebean.core", RegistrySource.DefaultRemoteUrl);
            StringAssert.Contains("/main/", RegistrySource.DefaultRemoteUrl);
            StringAssert.EndsWith("Editor/Resources/coffeebean.registry.json", RegistrySource.DefaultRemoteUrl);
        }

        /// <summary>ResolveUrl：EditorPrefs 有配置就用配置，没有才用官方默认地址。</summary>
        [Test]
        public void ResolveUrl_PrefOverridesDefault()
        {
            string original;
            bool hadPref = EditorPrefs.HasKey(RegistrySource.RemoteUrlPrefKey);
            original = EditorPrefs.GetString(RegistrySource.RemoteUrlPrefKey, string.Empty);

            try
            {
                EditorPrefs.DeleteKey(RegistrySource.RemoteUrlPrefKey);
                Assert.AreEqual(RegistrySource.DefaultRemoteUrl, RegistrySource.ResolveUrl(),
                    "没有配置时应回落到官方默认地址（而不是空 → 退回内置目录）");

                EditorPrefs.SetString(RegistrySource.RemoteUrlPrefKey, "https://internal.example/registry.json");
                Assert.AreEqual("https://internal.example/registry.json", RegistrySource.ResolveUrl(),
                    "配置过就一律以配置为准（内网镜像）");

                EditorPrefs.SetString(RegistrySource.RemoteUrlPrefKey, string.Empty);
                Assert.AreEqual(RegistrySource.DefaultRemoteUrl, RegistrySource.ResolveUrl(),
                    "显式清空也应回落到默认地址，而不是退回内置目录");
            }
            finally
            {
                if (hadPref) EditorPrefs.SetString(RegistrySource.RemoteUrlPrefKey, original);
                else EditorPrefs.DeleteKey(RegistrySource.RemoteUrlPrefKey);
            }
        }

        // ========== 缓存穿透 ==========

        /// <summary>
        /// 远程目录请求必须带缓存穿透参数。
        ///
        /// 实测踩过：`raw.githubusercontent.com` 的 `Cache-Control: max-age=300`，
        /// 刚发完新版本，编辑器读到的仍是**旧目录** → 计划里的版本与现状相同 → UPM 请求成了空操作，
        /// 界面却显示"已安装"。加了穿透参数（目录才几 KB）这个问题就没了。
        /// </summary>
        [Test]
        public void WithCacheBuster_AppendsTimestampQuery()
        {
            string result = RegistrySource.WithCacheBuster("https://raw.githubusercontent.com/a/b/registry.json");

            StringAssert.StartsWith("https://raw.githubusercontent.com/a/b/registry.json", result);
            Assert.AreNotEqual("https://raw.githubusercontent.com/a/b/registry.json", result, "必须带上穿透参数");
            StringAssert.Contains("?t=", result);
        }

        [Test]
        public void WithCacheBuster_UsesAmpersandWhenQueryAlreadyExists()
        {
            string result = RegistrySource.WithCacheBuster("https://example.com/registry.json?branch=main");

            StringAssert.Contains("?branch=main&t=", result);
        }

        [Test]
        public void WithCacheBuster_LeavesNonHttpAlone()
        {
            Assert.AreEqual("file:///x/registry.json", RegistrySource.WithCacheBuster("file:///x/registry.json"));
            Assert.AreEqual("Packages/com.coffeebean.core/Editor/Resources/coffeebean.registry.json",
                RegistrySource.WithCacheBuster("Packages/com.coffeebean.core/Editor/Resources/coffeebean.registry.json"));
            Assert.IsNull(RegistrySource.WithCacheBuster(null));
            Assert.AreEqual(string.Empty, RegistrySource.WithCacheBuster(string.Empty));
        }
    }
}
