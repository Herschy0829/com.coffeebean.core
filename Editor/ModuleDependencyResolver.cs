using System;
using System.Collections.Generic;

namespace CoffeeBean.EditorTools
{
    /// <summary>安装计划中的单个包。</summary>
    public sealed class PlannedPackage
    {
        /// <summary>包名（UPM id）。</summary>
        public string Id;

        /// <summary>完整 UPM 引用：CoffeeBean 模块为 <c>repo#tag</c>，第三方为其 registry 中登记的原样 url。</summary>
        public string Url;

        /// <summary>展示用版本 tag（第三方可能为空）。</summary>
        public string VersionTag;

        /// <summary>是否为本次用户点名要装的模块（其余都是被它带出来的依赖）。</summary>
        public bool IsTarget;

        /// <summary>是否为 registry 之外的第三方包。</summary>
        public bool IsExternal;

        public override string ToString()
            => IsExternal ? $"{Id} (第三方)" : $"{Id}@{VersionTag}";
    }

    /// <summary>一次安装的完整计划：依赖在前、目标在后。</summary>
    public sealed class ModuleInstallPlan
    {
        /// <summary>按安装顺序排列的包（第三方 → CoffeeBean 依赖 → 目标模块）。</summary>
        public readonly List<PlannedPackage> Packages = new List<PlannedPackage>();

        /// <summary>非致命问题（如 registry 未登记的依赖 id），供 UI 提示。</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>是否存在致命问题（此时 <see cref="Packages"/> 不可用）。</summary>
        public bool HasErrors;

        /// <summary>致命问题描述。</summary>
        public string Error;

        /// <summary>除目标模块外、本次需要新装的包。</summary>
        public List<PlannedPackage> RequiredDependencies
        {
            get
            {
                var list = new List<PlannedPackage>();
                foreach (PlannedPackage p in Packages)
                {
                    if (!p.IsTarget) list.Add(p);
                }
                return list;
            }
        }
    }

    /// <summary>
    /// 安装依赖闭包解析（纯逻辑，不触碰 UPM，便于单测）。
    ///
    /// **为什么需要它**：CoffeeBean 模块之间用 UPM 的 <c>dependencies</c> 声明依赖，而模块本身以 git 包形式
    /// 分发、并不在任何 registry 里 —— UPM 无法把 <c>"com.coffeebean.tools": "0.5.0"</c> 解析成 git 地址，
    /// 于是解析直接失败。所以「装 save」这件事必须由框架先补装 tools，再装 save。
    ///
    /// 解析规则：
    /// · <c>dependencies</c>：CoffeeBean 模块 id，在 registry 内递归展开（传递依赖）；
    /// · <c>externalDependencies</c>：非 registry 包，靠条目里登记的原样 UPM url 安装；
    /// · 已是工程一部分的包（<paramref name="presentPackageIds"/>）跳过 —— 除非它是本次的目标模块，
    ///   目标永远进计划，这样「更新到指定 tag」与「首次安装」共用同一条路径；
    /// · 出现环、目标不在 registry ⇒ 致命错误；引用了 registry 里没有的模块 ⇒ 警告并跳过。
    /// </summary>
    public static class ModuleDependencyResolver
    {
        /// <summary>
        /// Core 自身永远视为「已存在」：它不登记在 registry 里（否则用户能把它当普通模块装卸，
        /// 而 ModuleManager 就住在 Core 内），但 events 等模块声明了依赖它。
        /// </summary>
        public const string CorePackageId = "com.coffeebean.core";

        public static ModuleInstallPlan Resolve(CoffeeBeanRegistryData registry, string targetId,
            ICollection<string> presentPackageIds)
        {
            var plan = new ModuleInstallPlan();
            var present = new HashSet<string>(presentPackageIds ?? new string[0], StringComparer.OrdinalIgnoreCase);
            present.Add(CorePackageId);

            if (registry == null || string.IsNullOrEmpty(targetId))
            {
                plan.HasErrors = true;
                plan.Error = "registry 为空或未指定模块 id。";
                return plan;
            }

            var map = new Dictionary<string, CoffeeBeanRegistryEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (CoffeeBeanRegistryEntry e in registry.modules)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                map[e.id] = e; // 重复 id 以最后一条为准
            }

            if (!map.TryGetValue(targetId, out CoffeeBeanRegistryEntry target))
            {
                plan.HasErrors = true;
                plan.Error = $"registry 中没有模块 '{targetId}'，无法自动解析依赖。";
                return plan;
            }

            // 依赖优先的拓扑序（DFS 后序），顺带做环检测。
            var ordered = new List<CoffeeBeanRegistryEntry>();
            var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 1=访问中 2=已完成
            string cyclePath = null;

            Visit(target, new List<string>());

            void Visit(CoffeeBeanRegistryEntry entry, List<string> path)
            {
                state[entry.id] = 1;
                path.Add(entry.id);

                string[] deps = entry.dependencies ?? new string[0];
                foreach (string depId in deps)
                {
                    if (string.IsNullOrEmpty(depId)) continue;
                    if (string.Equals(depId, CorePackageId, StringComparison.OrdinalIgnoreCase)) continue; // Core 恒定存在
                    if (!map.TryGetValue(depId, out CoffeeBeanRegistryEntry dep))
                    {
                        plan.Warnings.Add($"{entry.id} 声明的依赖 '{depId}' 未登记在 registry 中，已跳过。");
                        continue;
                    }

                    if (state.TryGetValue(depId, out int st))
                    {
                        if (st == 1 && cyclePath == null)
                        {
                            int from = path.IndexOf(depId);
                            var ring = path.GetRange(from < 0 ? 0 : from, (from < 0 ? 0 : path.Count - from));
                            ring.Add(depId);
                            cyclePath = string.Join(" → ", ring);
                        }
                        if (st == 2) continue;
                        if (st == 1) continue;
                    }

                    Visit(dep, path);
                }

                path.RemoveAt(path.Count - 1);
                state[entry.id] = 2;
                ordered.Add(entry);
            }

            if (cyclePath != null)
            {
                plan.HasErrors = true;
                plan.Error = $"模块依赖存在循环：{cyclePath}";
                return plan;
            }

            // 1) 第三方依赖：所有被访问到的条目（含目标）声明的 external，去重且只装缺的。
            var seenExternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CoffeeBeanRegistryEntry e in ordered)
            {
                CoffeeBeanExternalDependency[] extras = e.externalDependencies ?? new CoffeeBeanExternalDependency[0];
                foreach (CoffeeBeanExternalDependency x in extras)
                {
                    if (x == null || string.IsNullOrEmpty(x.id)) continue;
                    if (!seenExternal.Add(x.id)) continue;
                    if (present.Contains(x.id)) continue;
                    if (string.IsNullOrEmpty(x.url))
                    {
                        plan.Warnings.Add($"{e.id} 的第三方依赖 '{x.id}' 未登记 url，无法自动安装（请手动添加）。");
                        continue;
                    }
                    plan.Packages.Add(new PlannedPackage
                    {
                        Id = x.id,
                        Url = x.url,
                        VersionTag = string.Empty,
                        IsExternal = true
                    });
                }
            }

            // 2) CoffeeBean 依赖（依赖优先），只装工程里没有的。
            foreach (CoffeeBeanRegistryEntry e in ordered)
            {
                if (ReferenceEquals(e, target)) continue;
                if (present.Contains(e.id)) continue;
                if (string.IsNullOrEmpty(e.repo))
                {
                    plan.Warnings.Add($"{e.id} 未登记 repo，无法自动安装。");
                    continue;
                }
                plan.Packages.Add(new PlannedPackage
                {
                    Id = e.id,
                    Url = BuildUrl(e.repo, e.latest),
                    VersionTag = e.latest,
                    IsExternal = false
                });
            }

            // 3) 目标模块永远进计划：首次安装与"更新到指定 tag"共用一条路径。
            plan.Packages.Add(new PlannedPackage
            {
                Id = target.id,
                Url = BuildUrl(target.repo, target.latest),
                VersionTag = target.latest,
                IsTarget = true,
                IsExternal = false
            });

            return plan;
        }

        /// <summary>拼装 UPM git 引用：<c>repo#tag</c>（tag 为空则用默认分支）。</summary>
        public static string BuildUrl(string repo, string versionTag)
            => string.IsNullOrEmpty(versionTag) ? repo : repo + "#" + versionTag;
    }
}
