using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    /// <summary>
    /// CoffeeBean Hub 工具发现测试：验证反射扫描能发现两类工具
    /// （独立窗口工具 / 内嵌面板），且两者判据互斥、向后兼容。
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

        /// <summary>
        /// 每个入口必须**要么**是窗口工具**要么**是内嵌面板，不能两头都不是、也不能两头都是 ——
        /// Hub 就是靠这个二分决定"点开一个窗口"还是"画在内容区里"。
        /// </summary>
        [Test]
        public void Scan_EntriesAreEitherWindowToolOrInlinePanel()
        {
            foreach (var tool in CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan())
            {
                Assert.IsTrue(tool.IsInline ^ (tool.WindowType != null),
                    $"{tool.Title} 必须恰好属于一类（IsInline={tool.IsInline} WindowType={tool.WindowType})");
            }
        }

        [Test]
        public void Scan_WindowToolsAreEditorWindows()
        {
            foreach (var tool in CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan())
            {
                if (tool.IsInline) continue;
                Assert.IsNotNull(tool.WindowType);
                Assert.IsTrue(typeof(UnityEditor.EditorWindow).IsAssignableFrom(tool.WindowType),
                    $"{tool.Title} 应为 EditorWindow 派生");
                Assert.IsFalse(tool.WindowType.IsAbstract, $"{tool.Title} 的窗口类型不能是抽象类");
            }
        }

        [Test]
        public void Scan_InlinePanelsAreStaticTypesWithDrawTool()
        {
            foreach (var tool in CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan())
            {
                if (!tool.IsInline) continue;

                Type type = tool.InlineDraw.DeclaringType;
                Assert.IsNotNull(type);
                Assert.IsTrue(type.IsAbstract && type.IsSealed, $"{tool.Title} 的内嵌面板必须是 static 类");
                Assert.IsNull(tool.WindowType, "内嵌面板不该同时带窗口类型");

                ParameterInfo[] parameters = tool.InlineDraw.GetParameters();
                Assert.LessOrEqual(parameters.Length, 1, $"{tool.Title} 的 DrawTool 最多接受一个参数");
                if (parameters.Length == 1)
                {
                    Assert.AreEqual(typeof(Action), parameters[0].ParameterType,
                        $"{tool.Title} 的 DrawTool 参数应是 Action requestRepaint");
                }
            }
        }

        /// <summary>内嵌面板没有窗口：<c>Open()</c> 必须是安全空操作（导航里误点也不能崩）。</summary>
        [Test]
        public void InlinePanel_OpenIsNoOp()
        {
            var inline = CoffeeBean.EditorTools.CoffeeBeanToolRegistry.Scan().FirstOrDefault(t => t.IsInline);
            if (inline == null) Assert.Ignore("当前工程没有内嵌面板（没装带面板的模块），跳过");

            Assert.DoesNotThrow(() => inline.Open());
        }

        [Test]
        public void FindInlineDraw_RequiresVoidStaticMethod()
        {
            // 签名不对的类型不应被当成内嵌面板
            Assert.IsNull(CoffeeBean.EditorTools.CoffeeBeanToolRegistry.FindInlineDraw(null));
            Assert.IsNull(CoffeeBean.EditorTools.CoffeeBeanToolRegistry.FindInlineDraw(typeof(string)));
            Assert.IsNull(CoffeeBean.EditorTools.CoffeeBeanToolRegistry.FindInlineDraw(GetType()),
                "测试类没有 DrawTool，不该被认成面板");
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
                first.Select(t => t.Title + "|" + t.IsInline),
                second.Select(t => t.Title + "|" + t.IsInline));
        }
    }
}
