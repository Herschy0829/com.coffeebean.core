using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 框架引导器（静态入口）：扫描模块 → 校验缺失依赖 → 应用配置开关 → 拓扑排序 →
    /// 按序 OnLoad（注册服务）→ 统一 OnStart → 退出按反序 OnShutdown。
    /// 入口场景放置 <see cref="CoffeeBeanBootstrap"/> 组件，或自行调用 Load()/Shutdown()。
    /// </summary>
    public static class CoffeeBeanBootstrapper
    {
        /// <summary>当前上下文；未加载时为 null。</summary>
        public static CoffeeBeanContext Context { get; private set; }

        public static bool IsLoaded => Context != null;

        public static CoffeeBeanContext Load(CoffeeBeanConfig config = null)
        {
            if (IsLoaded) return Context;

            var context = new CoffeeBeanContext(config);
            context.Modules.Scan();

            // 校验缺失依赖（只告警，不中断）
            foreach (CoffeeBeanModuleInfo info in context.Modules.Modules)
            {
                foreach (string dep in info.Dependencies)
                {
                    if (!context.Modules.IsInstalled(dep))
                        context.LogWarning($"Module {info.Id} depends on missing module '{dep}'.");
                }
            }

            // 应用配置开关
            foreach (CoffeeBeanModuleInfo info in context.Modules.Modules)
                info.Enabled = context.Config.IsModuleEnabled(info.Id);

            List<CoffeeBeanModuleInfo> order = ResolveLoadOrder(context.Modules.Modules);

            // OnLoad（拓扑序，依赖在前）
            foreach (CoffeeBeanModuleInfo info in order)
            {
                if (!info.Enabled) continue;
                ICoffeeBeanModule instance = info.CreateInstance();
                if (instance != null)
                {
                    context.Log($"Loading module {info.DisplayName} ({info.Id} {info.Version})...");
                    instance.OnLoad(context);
                }
            }

            // OnStart
            foreach (CoffeeBeanModuleInfo info in order)
            {
                if (!info.Enabled) continue;
                info.Instance?.OnStart();
            }

            Context = context;
            context.Log($"CoffeeBean bootstrap complete. {context.Modules.Modules.Count} module(s) discovered.");
            return context;
        }

        public static void Shutdown()
        {
            if (Context == null) return;

            List<CoffeeBeanModuleInfo> order = ResolveLoadOrder(Context.Modules.Modules);
            for (int i = order.Count - 1; i >= 0; i--)
            {
                CoffeeBeanModuleInfo info = order[i];
                if (!info.Enabled) continue;
                try
                {
                    info.Instance?.OnShutdown();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CoffeeBean] Module {info.Id} OnShutdown failed: {e}");
                }
            }
            Context = null;
        }

        /// <summary>
        /// 依赖拓扑排序（Kahn 算法）。存在环时抛出 <see cref="InvalidOperationException"/> 并附上环内模块。
        /// 依赖未安装的模块会被跳过（不参与排序），由调用方负责告警。
        /// </summary>
        public static List<CoffeeBeanModuleInfo> ResolveLoadOrder(IReadOnlyList<CoffeeBeanModuleInfo> modules)
        {
            var byId = new Dictionary<string, CoffeeBeanModuleInfo>();
            foreach (CoffeeBeanModuleInfo m in modules) byId[m.Id] = m;

            var indegree = new Dictionary<string, int>();
            var dependents = new Dictionary<string, List<string>>();
            foreach (CoffeeBeanModuleInfo m in modules)
            {
                indegree[m.Id] = 0;
                dependents[m.Id] = new List<string>();
            }

            foreach (CoffeeBeanModuleInfo m in modules)
            {
                foreach (string dep in m.Dependencies)
                {
                    if (!byId.ContainsKey(dep)) continue; // 依赖未安装：跳过
                    dependents[dep].Add(m.Id);
                    indegree[m.Id]++;
                }
            }

            var queue = new Queue<CoffeeBeanModuleInfo>();
            foreach (CoffeeBeanModuleInfo m in modules)
                if (indegree[m.Id] == 0) queue.Enqueue(m);

            var order = new List<CoffeeBeanModuleInfo>();
            while (queue.Count > 0)
            {
                CoffeeBeanModuleInfo m = queue.Dequeue();
                order.Add(m);
                foreach (string depId in dependents[m.Id])
                {
                    if (--indegree[depId] == 0) queue.Enqueue(byId[depId]);
                }
            }

            if (order.Count != modules.Count)
            {
                var cyclic = new List<string>();
                foreach (CoffeeBeanModuleInfo m in modules)
                    if (indegree[m.Id] > 0) cyclic.Add(m.Id);
                throw new InvalidOperationException(
                    "CoffeeBean module dependency cycle detected: " + string.Join(" -> ", cyclic));
            }
            return order;
        }
    }
}
