using System.Collections.Generic;
using System.Linq;
using CoffeeBean.EditorTools;
using NUnit.Framework;

namespace CoffeeBean.Tests
{
    /// <summary>
    /// 依赖闭包解析测试（纯逻辑，不触发 UPM）。
    /// 覆盖：传递依赖顺序、已存在跳过、第三方依赖、环/未知目标报错、registry 自洽性。
    /// </summary>
    public class ModuleDependencyResolverTests
    {
        // ========== 构造工具 ==========

        private static CoffeeBeanRegistryEntry Entry(string id, string latest = "v1.0.0",
            string[] deps = null, CoffeeBeanExternalDependency[] ext = null)
        {
            return new CoffeeBeanRegistryEntry
            {
                id = id,
                repo = "https://example.com/" + id + ".git",
                latest = latest,
                dependencies = deps,
                externalDependencies = ext
            };
        }

        private static CoffeeBeanRegistryData Registry(params CoffeeBeanRegistryEntry[] entries)
        {
            var data = new CoffeeBeanRegistryData();
            data.modules.AddRange(entries);
            return data;
        }

        private static List<string> Ids(ModuleInstallPlan plan)
            => plan.Packages.Select(p => p.Id).ToList();

        // ========== 基本行为 ==========

        [Test]
        public void Resolve_NoDependencies_PlansOnlyTarget()
        {
            var registry = Registry(Entry("com.a", "v1.2.0"));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.a", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.a" }, Ids(plan));
            Assert.AreEqual("https://example.com/com.a.git#v1.2.0", plan.Packages[0].Url);
            Assert.AreEqual("v1.2.0", plan.Packages[0].VersionTag);
            Assert.IsTrue(plan.Packages[0].IsTarget);
            Assert.IsEmpty(plan.RequiredDependencies);
        }

        [Test]
        public void Resolve_TransitiveDependencies_ComeBeforeTarget()
        {
            // ui → asset → tools，期望顺序 tools, asset, ui
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.asset", deps: new[] { "com.tools" }),
                Entry("com.ui", deps: new[] { "com.tools", "com.asset" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.ui", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.tools", "com.asset", "com.ui" }, Ids(plan));
            Assert.AreEqual(2, plan.RequiredDependencies.Count);
            Assert.IsTrue(plan.Packages.Last().IsTarget, "最后一个必须是目标模块");
        }

        [Test]
        public void Resolve_DiamondDependency_NotDuplicated()
        {
            // top → (left, right)，left/right 都依赖 base
            var registry = Registry(
                Entry("com.base"),
                Entry("com.left", deps: new[] { "com.base" }),
                Entry("com.right", deps: new[] { "com.base" }),
                Entry("com.top", deps: new[] { "com.left", "com.right" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.top", new string[0]);

            List<string> ids = Ids(plan);
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "依赖不应重复出现：" + string.Join(",", ids));
            Assert.Less(ids.IndexOf("com.base"), ids.IndexOf("com.left"));
            Assert.Less(ids.IndexOf("com.base"), ids.IndexOf("com.right"));
            Assert.AreEqual("com.top", ids.Last());
        }

        // ========== 已在工程中的依赖 ==========

        [Test]
        public void Resolve_PresentDependency_Skipped()
        {
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.save", deps: new[] { "com.tools" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.save", new[] { "com.tools" });

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.save" }, Ids(plan));
            Assert.IsEmpty(plan.RequiredDependencies);
        }

        [Test]
        public void Resolve_PresentDependency_MatchedCaseInsensitively()
        {
            var registry = Registry(
                Entry("com.Tools"),
                Entry("com.save", deps: new[] { "com.tools" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.save", new[] { "COM.TOOLS" });

            Assert.IsEmpty(plan.RequiredDependencies);
        }

        [Test]
        public void Resolve_TargetAlreadyPresent_StillPlanned_SoUpdateWorks()
        {
            var registry = Registry(Entry("com.save", "v0.3.0"));

            // 目标已装也要进计划：更新本质是用新 tag 重新 Add
            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.save", new[] { "com.save" });

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.save" }, Ids(plan));
            Assert.AreEqual("https://example.com/com.save.git#v0.3.0", plan.Packages[0].Url);
        }

        // ========== 第三方依赖 ==========

        [Test]
        public void Resolve_ExternalDependency_PlannedFirst_WithExplicitUrl()
        {
            var ext = new[]
            {
                new CoffeeBeanExternalDependency
                {
                    id = "com.cysharp.memorypack",
                    url = "https://github.com/Cysharp/MemoryPack.git?path=src/MemoryPack.Unity/Assets/MemoryPack.Unity#1.21.4"
                }
            };
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.save", deps: new[] { "com.tools" }, ext: ext));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.save", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            List<string> ids = Ids(plan);
            Assert.AreEqual(0, ids.IndexOf("com.cysharp.memorypack"), "第三方依赖应排在最前：" + string.Join(",", ids));
            Assert.Less(ids.IndexOf("com.tools"), ids.IndexOf("com.save"));

            PlannedPackage mp = plan.Packages[0];
            Assert.IsTrue(mp.IsExternal);
            Assert.AreEqual("https://github.com/Cysharp/MemoryPack.git?path=src/MemoryPack.Unity/Assets/MemoryPack.Unity#1.21.4", mp.Url);
        }

        [Test]
        public void Resolve_ExternalDependencyPresent_Skipped()
        {
            var ext = new[] { new CoffeeBeanExternalDependency { id = "com.cysharp.memorypack", url = "https://x.git" } };
            var registry = Registry(Entry("com.save", ext: ext));

            ModuleInstallPlan plan =
                ModuleDependencyResolver.Resolve(registry, "com.save", new[] { "com.cysharp.memorypack" });

            Assert.IsEmpty(plan.RequiredDependencies);
        }

        [Test]
        public void Resolve_ExternalDependencyWithoutUrl_WarnsAndSkips()
        {
            var ext = new[] { new CoffeeBeanExternalDependency { id = "com.unknown.pkg", url = null } };
            var registry = Registry(Entry("com.save", ext: ext));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.save", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.save" }, Ids(plan));
            Assert.IsNotEmpty(plan.Warnings);
        }

        [Test]
        public void Resolve_SharedExternalDependency_NotDuplicated()
        {
            var mk = new CoffeeBeanExternalDependency { id = "com.shared", url = "https://shared.git" };
            var registry = Registry(
                Entry("com.a", ext: new[] { mk }),
                Entry("com.b", ext: new[] { new CoffeeBeanExternalDependency { id = "com.shared", url = "https://shared.git" } }),
                Entry("com.top", deps: new[] { "com.a", "com.b" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.top", new string[0]);

            Assert.AreEqual(1, Ids(plan).Count(id => id == "com.shared"));
        }

        // ========== Core 恒定存在 ==========

        [Test]
        public void Resolve_CoreDependency_TreatedAsPresent_NoWarning()
        {
            var registry = Registry(Entry("com.events", deps: new[] { "com.coffeebean.core" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.events", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.events" }, Ids(plan));
            Assert.IsEmpty(plan.Warnings, "Core 不应触发「未登记」告警：" + string.Join(" ", plan.Warnings));
        }

        // ========== 错误路径 ==========

        [Test]
        public void Resolve_UnknownTarget_ReturnsError()
        {
            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(Registry(), "com.nope", new string[0]);

            Assert.IsTrue(plan.HasErrors);
            Assert.IsNotEmpty(plan.Error);
            Assert.IsEmpty(plan.Packages);
        }

        [Test]
        public void Resolve_NullRegistry_ReturnsError()
        {
            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(null, "com.a", new string[0]);

            Assert.IsTrue(plan.HasErrors);
            Assert.IsEmpty(plan.Packages);
        }

        [Test]
        public void Resolve_Cycle_ReturnsError_NamingTheRing()
        {
            var registry = Registry(
                Entry("com.a", deps: new[] { "com.b" }),
                Entry("com.b", deps: new[] { "com.a" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.a", new string[0]);

            Assert.IsTrue(plan.HasErrors);
            StringAssert.Contains("循环", plan.Error);
            StringAssert.Contains("com.a", plan.Error);
            StringAssert.Contains("com.b", plan.Error);
        }

        [Test]
        public void Resolve_SelfCycle_ReturnsError()
        {
            var registry = Registry(Entry("com.a", deps: new[] { "com.a" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.a", new string[0]);

            Assert.IsTrue(plan.HasErrors);
        }

        [Test]
        public void Resolve_UnknownDependency_WarnsButContinues()
        {
            var registry = Registry(Entry("com.save", deps: new[] { "com.coffeebean.ghost" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.save", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.save" }, Ids(plan));
            Assert.IsNotEmpty(plan.Warnings);
            StringAssert.Contains("com.coffeebean.ghost", string.Join(" ", plan.Warnings));
        }

        [Test]
        public void Resolve_DependencyWithoutRepo_WarnsButContinues()
        {
            var registry = Registry(
                new CoffeeBeanRegistryEntry { id = "com.broken", repo = null, latest = "v1.0.0" },
                Entry("com.save", deps: new[] { "com.broken" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.save", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.save" }, Ids(plan));
            Assert.IsNotEmpty(plan.Warnings);
        }

        [Test]
        public void Resolve_NullDependencyArrays_DoNotThrow()
        {
            var registry = Registry(
                new CoffeeBeanRegistryEntry { id = "com.a", repo = "https://a.git", latest = "v1.0.0" });
            registry.modules.Add(new CoffeeBeanRegistryEntry
            {
                id = "com.b",
                repo = "https://b.git",
                latest = "v1.0.0",
                dependencies = new[] { (string)null }
            });

            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, "com.b", new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.b" }, Ids(plan));
        }

        // ========== BuildUrl ==========

        [Test]
        public void BuildUrl_AppendsTag()
        {
            Assert.AreEqual("https://a.git#v1.0.0", ModuleDependencyResolver.BuildUrl("https://a.git", "v1.0.0"));
            Assert.AreEqual("https://a.git", ModuleDependencyResolver.BuildUrl("https://a.git", null));
            Assert.AreEqual("https://a.git", ModuleDependencyResolver.BuildUrl("https://a.git", ""));
        }

        // ========== 多目标安装计划（一键安装所有依赖） ==========

        [Test]
        public void ResolveMany_MergesTargets_DependencyFirst()
        {
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.asset", deps: new[] { "com.tools" }),
                Entry("com.ui", deps: new[] { "com.tools", "com.asset" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                registry, new[] { "com.asset", "com.ui" }, new string[0]);

            Assert.IsFalse(plan.HasErrors, plan.Error);
            Assert.AreEqual(new List<string> { "com.tools", "com.asset", "com.ui" }, Ids(plan),
                "共享依赖只装一次，且排在被依赖者之前");
        }

        [Test]
        public void ResolveMany_MarksEveryRequestedTarget()
        {
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.asset", deps: new[] { "com.tools" }),
                Entry("com.ui", deps: new[] { "com.tools", "com.asset" }));

            // asset 先被当成 ui 的依赖进了计划，但它同样是本次的目标 → IsTarget 必须为 true
            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                registry, new[] { "com.asset", "com.ui" }, new string[0]);

            var targets = new List<string>();
            foreach (PlannedPackage p in plan.Packages)
            {
                if (p.IsTarget) targets.Add(p.Id);
            }
            CollectionAssert.AreEquivalent(new[] { "com.asset", "com.ui" }, targets);
        }

        [Test]
        public void ResolveMany_UnknownTarget_ReturnsErrorAndNoPackages()
        {
            var registry = Registry(Entry("com.a"));

            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                registry, new[] { "com.a", "com.nope" }, new string[0]);

            Assert.IsTrue(plan.HasErrors, "有一个目标解析不了就整体失败，避免装了一半");
            Assert.IsEmpty(plan.Packages);
        }

        [Test]
        public void ResolveMany_EmptyTargets_ReturnsEmptyPlan()
        {
            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                Registry(Entry("com.a")), new string[0], new string[0]);

            Assert.IsFalse(plan.HasErrors);
            Assert.IsEmpty(plan.Packages);
        }

        [Test]
        public void ResolveMany_NullTargets_ReturnsEmptyPlan()
        {
            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                Registry(Entry("com.a")), null, new string[0]);

            Assert.IsFalse(plan.HasErrors);
            Assert.IsEmpty(plan.Packages);
        }

        [Test]
        public void ResolveMany_SkipsPresentDependencies()
        {
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.ui", deps: new[] { "com.tools" }));

            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                registry, new[] { "com.ui" }, new[] { "com.tools" });

            Assert.AreEqual(new List<string> { "com.ui" }, Ids(plan));
        }

        [Test]
        public void ResolveMany_TargetAlreadyPresent_StillPlanned()
        {
            var registry = Registry(Entry("com.a", "v2.0.0"));

            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                registry, new[] { "com.a" }, new[] { "com.a" });

            Assert.AreEqual(new List<string> { "com.a" }, Ids(plan), "目标恒进计划，便于批量更新");
            Assert.AreEqual("https://example.com/com.a.git#v2.0.0", plan.Packages[0].Url);
        }

        [Test]
        public void ResolveMany_OrderNeverViolatesDependencies()
        {
            // 强不变式：计划里任何包，其"也在计划里的依赖"必须排在它前面
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.asset", deps: new[] { "com.tools" }),
                Entry("com.ui", deps: new[] { "com.tools", "com.asset" }),
                Entry("com.purchase", deps: new[] { "com.excel" }),
                Entry("com.excel"));

            var targets = new[] { "com.ui", "com.purchase", "com.asset" };
            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(registry, targets, new string[0]);

            List<string> ids = Ids(plan);
            foreach (string id in ids)
            {
                CoffeeBeanRegistryEntry entry = registry.modules.First(m => m.id == id);
                foreach (string dep in entry.dependencies ?? new string[0])
                {
                    if (!ids.Contains(dep)) continue;
                    Assert.Less(ids.IndexOf(dep), ids.IndexOf(id),
                        $"{id} 的依赖 {dep} 必须排在它前面");
                }
            }
        }

        [Test]
        public void ResolveMany_ExternalDependencies_Deduplicated()
        {
            var ext = new[] { new CoffeeBeanExternalDependency { id = "com.shared", url = "https://shared.git" } };
            var registry = Registry(
                Entry("com.a", ext: ext),
                Entry("com.b", ext: new[] { new CoffeeBeanExternalDependency { id = "com.shared", url = "https://shared.git" } }));

            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                registry, new[] { "com.a", "com.b" }, new string[0]);

            Assert.AreEqual(1, Ids(plan).Count(id => id == "com.shared"));
        }

        // ========== 卸载顺序（一键卸载所有依赖） ==========

        [Test]
        public void ResolveUninstallOrder_DependentsComeFirst()
        {
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.asset", deps: new[] { "com.tools" }),
                Entry("com.ui", deps: new[] { "com.tools", "com.asset" }));

            List<string> order = ModuleDependencyResolver.ResolveUninstallOrder(
                registry, new[] { "com.tools", "com.asset", "com.ui" });

            Assert.AreEqual(3, order.Count);
            Assert.Less(order.IndexOf("com.ui"), order.IndexOf("com.asset"), "ui 依赖 asset → ui 先卸");
            Assert.Less(order.IndexOf("com.asset"), order.IndexOf("com.tools"), "asset 依赖 tools → asset 先卸");
        }

        [Test]
        public void ResolveUninstallOrder_OnlyReturnsRequestedIds()
        {
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.ui", deps: new[] { "com.tools" }));

            List<string> order = ModuleDependencyResolver.ResolveUninstallOrder(registry, new[] { "com.ui" });

            Assert.AreEqual(new List<string> { "com.ui" }, order);
        }

        [Test]
        public void ResolveUninstallOrder_UnknownIdsGoFirst()
        {
            var registry = Registry(Entry("com.known"));

            List<string> order = ModuleDependencyResolver.ResolveUninstallOrder(
                registry, new[] { "com.known", "com.vendor.unknown" });

            Assert.AreEqual(2, order.Count);
            Assert.AreEqual("com.vendor.unknown", order[0], "registry 里查不到的排最前，免得它反过来卡住顺序");
        }

        [Test]
        public void ResolveUninstallOrder_EmptyOrNull_ReturnsEmpty()
        {
            Assert.IsEmpty(ModuleDependencyResolver.ResolveUninstallOrder(Registry(), new string[0]));
            Assert.IsEmpty(ModuleDependencyResolver.ResolveUninstallOrder(Registry(), null));
        }

        [Test]
        public void ResolveUninstallOrder_EachIdReturnedExactlyOnce()
        {
            var registry = Registry(
                Entry("com.tools"),
                Entry("com.asset", deps: new[] { "com.tools" }),
                Entry("com.ui", deps: new[] { "com.tools", "com.asset" }));

            List<string> order = ModuleDependencyResolver.ResolveUninstallOrder(
                registry, new[] { "com.ui", "com.asset", "com.tools", "com.ui" });

            Assert.AreEqual(3, order.Count);
            CollectionAssert.AreEquivalent(new[] { "com.ui", "com.asset", "com.tools" }, order);
        }

        [Test]
        public void ResolveUninstallOrder_CycleDoesNotHang()
        {
            var registry = Registry(
                Entry("com.a", deps: new[] { "com.b" }),
                Entry("com.b", deps: new[] { "com.a" }));

            List<string> order = ModuleDependencyResolver.ResolveUninstallOrder(
                registry, new[] { "com.a", "com.b" });

            Assert.AreEqual(2, order.Count, "成环时也不能卡死，按原顺序卸完即可");
        }

        [Test]
        public void ResolveUninstallOrder_WholeBuiltInCatalogIsConsistent()
        {
            // 对内置 registry 全量校验：卸载顺序里被依赖者绝不能排在依赖方之前
            CoffeeBeanRegistryData registry = RegistrySource.LoadBuiltIn();
            var all = registry.modules.Select(m => m.id).ToList();

            List<string> order = ModuleDependencyResolver.ResolveUninstallOrder(registry, all);

            Assert.AreEqual(all.Count, order.Count);
            var byId = registry.modules.ToDictionary(m => m.id, m => m);
            foreach (string id in order)
            {
                foreach (string dep in byId[id].dependencies ?? new string[0])
                {
                    if (dep == ModuleDependencyResolver.CorePackageId) continue;
                    if (!order.Contains(dep)) continue;
                    Assert.Greater(order.IndexOf(dep), order.IndexOf(id),
                        $"{id} 依赖 {dep}：卸载时必须先卸 {id}");
                }
            }
        }

        // ========== 内置 registry 自洽性 ==========

        [Test]
        public void BuiltInRegistry_IsSelfConsistent()
        {
            CoffeeBeanRegistryData registry = RegistrySource.LoadBuiltIn();
            Assert.Greater(registry.modules.Count, 0, "内置 registry 未加载到任何模块");

            var byId = new Dictionary<string, CoffeeBeanRegistryEntry>();
            foreach (CoffeeBeanRegistryEntry e in registry.modules)
            {
                Assert.IsNotEmpty(e.id, "存在 id 为空的条目");
                Assert.IsFalse(byId.ContainsKey(e.id), "registry 存在重复 id：" + e.id);
                Assert.IsNotEmpty(e.repo, e.id + " 缺少 repo");
                Assert.IsNotEmpty(e.latest, e.id + " 缺少 latest");
                byId[e.id] = e;
            }

            // 每个声明的依赖都必须能解析（Core 恒定存在，无需登记）
            foreach (CoffeeBeanRegistryEntry e in registry.modules)
            {
                foreach (string dep in e.dependencies ?? new string[0])
                {
                    if (dep == ModuleDependencyResolver.CorePackageId) continue;
                    Assert.IsTrue(byId.ContainsKey(dep), $"{e.id} 依赖的 '{dep}' 未登记在 registry 中");
                }
                foreach (CoffeeBeanExternalDependency x in e.externalDependencies ?? new CoffeeBeanExternalDependency[0])
                {
                    Assert.IsNotEmpty(x.id, e.id + " 存在 id 为空的第三方依赖");
                    Assert.IsNotEmpty(x.url, $"{e.id} 的第三方依赖 {x.id} 缺少 url");
                }
            }

            // 任意模块都能解析出计划且无环
            foreach (CoffeeBeanRegistryEntry e in registry.modules)
            {
                ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, e.id, new string[0]);
                Assert.IsFalse(plan.HasErrors, $"{e.id} 依赖解析失败：{plan.Error}");
                Assert.AreEqual(e.id, plan.Packages.Last().Id, $"{e.id} 的安装顺序末尾不是它自己");
            }
        }

        [Test]
        public void BuiltInRegistry_SaveDeclaresMemoryPackAndTools()
        {
            CoffeeBeanRegistryData registry = RegistrySource.LoadBuiltIn();
            CoffeeBeanRegistryEntry save = registry.modules.FirstOrDefault(e => e.id == "com.coffeebean.save");
            Assert.IsNotNull(save, "registry 中缺少 com.coffeebean.save");

            CollectionAssert.Contains(save.dependencies, "com.coffeebean.tools");

            Assert.IsNotNull(save.externalDependencies, "save 必须声明 memorypack 为第三方依赖，否则装 save 会因缺依赖而解析失败");
            CoffeeBeanExternalDependency mp =
                save.externalDependencies.FirstOrDefault(x => x.id == "com.cysharp.memorypack");
            Assert.IsNotNull(mp, "save 未声明 com.cysharp.memorypack");
            StringAssert.Contains("MemoryPack", mp.url);
            StringAssert.Contains("#1.21.4", mp.url);
        }

        [Test]
        public void BuiltInRegistry_EveryModulePackageJsonDependencyIsDeclared()
        {
            // 防止 registry 元数据与各模块 package.json 漂移：
            // registry 里的 dependencies 至少覆盖 package.json 里声明的 com.coffeebean.* 依赖。
            CoffeeBeanRegistryData registry = RegistrySource.LoadBuiltIn();
            foreach (CoffeeBeanRegistryEntry e in registry.modules)
            {
                foreach (string dep in e.dependencies ?? new string[0])
                {
                    if (dep == ModuleDependencyResolver.CorePackageId) continue;
                    Assert.IsTrue(registry.modules.Any(m => m.id == dep),
                        $"{e.id} 依赖 {dep}，但 registry 里没有它 —— 装 {e.id} 时会缺依赖");
                }
            }
        }
    }
}
