using CoffeeBean;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    /// <summary>CoffeeBeanVersion 语义化版本比较测试（MinCoreVersion 校验 / Module Manager tag 比较共用）。</summary>
    public class CoffeeBeanVersionTests
    {
        [Test]
        public void TryParse_ValidVersions_Parsed()
        {
            Assert.IsTrue(CoffeeBeanVersion.TryParse("0.1.10", out var p1));
            Assert.AreEqual(new[] { 0, 1, 10 }, p1);

            Assert.IsTrue(CoffeeBeanVersion.TryParse("v1.0", out var p2));
            Assert.AreEqual(new[] { 1, 0 }, p2);

            Assert.IsTrue(CoffeeBeanVersion.TryParse("1.2.3.4", out var p3));
            Assert.AreEqual(new[] { 1, 2, 3, 4 }, p3);
        }

        [Test]
        public void TryParse_InvalidVersions_Rejected()
        {
            Assert.IsFalse(CoffeeBeanVersion.TryParse("main", out _));
            Assert.IsFalse(CoffeeBeanVersion.TryParse("v1.x", out _));
            Assert.IsFalse(CoffeeBeanVersion.TryParse("", out _));
            Assert.IsFalse(CoffeeBeanVersion.TryParse(null, out _));
            Assert.IsFalse(CoffeeBeanVersion.TryParse("1.2.3.4.5", out _)); // 最多 4 段
            Assert.IsFalse(CoffeeBeanVersion.TryParse("1..2", out _));
        }

        [Test]
        public void Compare_NumericNotLexicographic()
        {
            // v0.1.9 < v0.1.10（字典序会判反，必须按数字比较）
            Assert.Less(CoffeeBeanVersion.Compare("v0.1.9", "v0.1.10"), 0);
            Assert.Greater(CoffeeBeanVersion.Compare("v0.1.10", "v0.1.9"), 0);
        }

        [Test]
        public void Compare_MissingSegments_ZeroPadded()
        {
            Assert.AreEqual(0, CoffeeBeanVersion.Compare("v1.0", "v1.0.0"));
            Assert.Less(CoffeeBeanVersion.Compare("v1.0", "v1.0.1"), 0);
            Assert.Greater(CoffeeBeanVersion.Compare("v1.0.1", "v1.0"), 0);
        }

        [Test]
        public void Compare_InvalidInput_FallsBackToOrdinal()
        {
            // 非法版本（分支/提交名）不抛异常，退化为字典序比较（保证全序）
            Assert.AreEqual(string.CompareOrdinal("main", "v0.1.0"), CoffeeBeanVersion.Compare("main", "v0.1.0"));
            Assert.AreEqual(string.CompareOrdinal("release-1", "v0.1.0"), CoffeeBeanVersion.Compare("release-1", "v0.1.0"));
            Assert.AreEqual(string.CompareOrdinal("main", string.Empty), CoffeeBeanVersion.Compare("main", null));
            Assert.AreEqual(0, CoffeeBeanVersion.Compare("branch-x", "branch-x"));
            Assert.AreEqual(0, CoffeeBeanVersion.Compare(null, null));
        }

        [Test]
        public void IsSatisfied_CurrentMeetsMinimum()
        {
            Assert.IsTrue(CoffeeBeanVersion.IsSatisfied("0.1.11", "0.1.10"));
            Assert.IsTrue(CoffeeBeanVersion.IsSatisfied("0.1.10", "0.1.10")); // 相等满足
            Assert.IsTrue(CoffeeBeanVersion.IsSatisfied("1.0.0", "0.9.9"));   // 大版本跨级
            Assert.IsTrue(CoffeeBeanVersion.IsSatisfied("0.1.10", "0.1"));    // 缺段按 0 补
        }

        [Test]
        public void IsSatisfied_CurrentBelowMinimum()
        {
            Assert.IsFalse(CoffeeBeanVersion.IsSatisfied("0.1.9", "0.1.10"));
            Assert.IsFalse(CoffeeBeanVersion.IsSatisfied("0.2.0", "0.3.0"));
            Assert.IsFalse(CoffeeBeanVersion.IsSatisfied(null, "0.1.0"));
            Assert.IsFalse(CoffeeBeanVersion.IsSatisfied("", "0.1.0"));
        }

        [Test]
        public void IsSatisfied_NoMinimum_AlwaysTrue()
        {
            Assert.IsTrue(CoffeeBeanVersion.IsSatisfied("0.1.0", null));
            Assert.IsTrue(CoffeeBeanVersion.IsSatisfied("0.1.0", string.Empty));
            Assert.IsTrue(CoffeeBeanVersion.IsSatisfied(null, null));
        }
    }
}
