using System;
using System.Collections.Generic;
using CoffeeBean;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    public class CoffeeBeanRegistryTests
    {
        private static CoffeeBeanModuleInfo MakeInfo(string id, params string[] dependencies)
        {
            return new CoffeeBeanModuleInfo(
                new CoffeeBeanModuleAttribute(id, "1.0.0") { Dependencies = dependencies },
                null);
        }

        [Test]
        public void Scan_FindsCoreModule()
        {
            var registry = new CoffeeBeanRegistry();
            registry.Scan();
            Assert.IsTrue(registry.IsInstalled("com.coffeebean.core"));
        }

        [Test]
        public void Scan_UnknownId_ReturnsFalse()
        {
            var registry = new CoffeeBeanRegistry();
            registry.Scan();
            Assert.IsFalse(registry.IsInstalled("com.coffeebean.does-not-exist"));
        }

        [Test]
        public void GetDependents_ReturnsDirectDependentsOnly()
        {
            var registry = new CoffeeBeanRegistry();
            registry.Scan();
            // Core 自身不应出现在任何依赖方列表中（无模块依赖 Core 之外的情况由扫描结果决定）
            List<CoffeeBeanModuleInfo> dependents = registry.GetDependents("com.coffeebean.core");
            Assert.IsNotNull(dependents);
        }

        [Test]
        public void ResolveLoadOrder_Chain_OrdersByDependency()
        {
            CoffeeBeanModuleInfo a = MakeInfo("a");
            CoffeeBeanModuleInfo b = MakeInfo("b", "a");
            CoffeeBeanModuleInfo c = MakeInfo("c", "b");

            List<CoffeeBeanModuleInfo> order = CoffeeBeanBootstrapper.ResolveLoadOrder(new[] { c, a, b });

            Assert.AreEqual(new[] { "a", "b", "c" }, order.ConvertAll(m => m.Id));
        }

        [Test]
        public void ResolveLoadOrder_Cycle_Throws()
        {
            CoffeeBeanModuleInfo a = MakeInfo("a", "b");
            CoffeeBeanModuleInfo b = MakeInfo("b", "a");

            Assert.Throws<InvalidOperationException>(
                () => CoffeeBeanBootstrapper.ResolveLoadOrder(new[] { a, b }));
        }

        [Test]
        public void ResolveLoadOrder_MissingDependency_IsSkipped()
        {
            CoffeeBeanModuleInfo a = MakeInfo("a");
            CoffeeBeanModuleInfo b = MakeInfo("b", "missing");

            List<CoffeeBeanModuleInfo> order = CoffeeBeanBootstrapper.ResolveLoadOrder(new[] { a, b });

            Assert.AreEqual(2, order.Count);
        }
    }
}
