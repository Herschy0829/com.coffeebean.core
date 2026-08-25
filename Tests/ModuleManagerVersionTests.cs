using CoffeeBean.EditorTools;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    /// <summary>Module Manager 更新检测逻辑测试（版本 tag 解析与比较）。</summary>
    public class ModuleManagerVersionTests
    {
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
