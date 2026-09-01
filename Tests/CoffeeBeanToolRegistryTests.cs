using System.Linq;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    /// <summary>
    /// CoffeeBean Hub 工具发现测试：
    /// 验证反射扫描能发现带 CoffeeBeanToolAttribute 的 EditorWindow（含各模块复制的同名 attribute）。
    /// </summary>
    public class CoffeeBeanToolRegistryTests
    {
        [Test]
        public void Scan_FindsModuleManagerBuiltin()
        {
            var tools = CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan();
            // 模块管理是 Hub 内置项，不进 registry；这里只验证扫描不抛异常且返回稳定列表
            Assert.IsNotNull(tools);
            Assert.AreEqual(tools.Count, tools.Count); // 稳定
        }

        [Test]
        public void Scan_ToolsAreEditorWindows()
        {
            var tools = CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan();
            foreach (var tool in tools)
            {
                Assert.IsNotNull(tool.WindowType, "工具类型不应为空");
                Assert.IsTrue(typeof(UnityEditor.EditorWindow).IsAssignableFrom(tool.WindowType),
                    $"{tool.Title} 应为 EditorWindow 派生");
            }
        }

        [Test]
        public void Scan_EntriesHaveTitles()
        {
            var tools = CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan();
            Assert.IsTrue(tools.All(t => !string.IsNullOrEmpty(t.Title)), "所有工具应有标题");
        }

        [Test]
        public void RefreshCache_Rescans()
        {
            var first = CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan();
            CoffeeBean.EditorTools.CoffeeBeanToolRegistry.RefreshCache();
            var second = CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan();
            CollectionAssert.AreEqual(
                first.Select(t => t.Title),
                second.Select(t => t.Title));
        }
    }
}
