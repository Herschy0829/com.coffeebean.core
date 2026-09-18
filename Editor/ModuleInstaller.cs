using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// 模块安装 / 卸载 API。底层驱动 Unity Package Manager：
    /// 安装 = Client.Add(gitUrl#tag)，写入 Packages/manifest.json 并触发重解析编译；
    /// 卸载 = Client.Remove(id)，从 manifest 移除并重编译。
    /// 完成回调通过轮询 Request.IsCompleted 实现（Unity 6 的 Request 无 completed 事件）。
    ///
    /// 依赖自动补装见 <see cref="InstallWithDependencies"/>。
    /// </summary>
    public static class ModuleInstaller
    {
        /// <summary>
        /// 安装模块（单包）。优先用 <see cref="InstallWithDependencies"/>，它会先补齐依赖。
        /// </summary>
        public static void Install(string packageId, string gitUrl, string versionTag, Action<bool, string> onCompleted = null)
        {
            string url = ModuleDependencyResolver.BuildUrl(gitUrl, versionTag);
            RunAddAndRemove(new[] { url }, null,
                $"已安装 {packageId}（{versionTag ?? "default branch"}）。", onCompleted);
        }

        /// <summary>
        /// 安装模块并**自动补装缺失依赖**（含传递依赖与第三方依赖）。
        ///
        /// CoffeeBean 模块以 git 包分发、不在任何 registry 里，所以 UPM 无法把模块 package.json 里的
        /// <c>"com.coffeebean.tools": "0.5.0"</c> 解析成地址 —— 不补装就会解析失败。
        /// 本方法先算出「第三方 → CoffeeBean 依赖 → 目标模块」的完整清单，再用**一次**
        /// <c>Client.AddAndRemove</c> 提交（为什么必须一次见 <see cref="RunAddAndRemove"/>）。
        ///
        /// 已在工程中的依赖会被跳过；目标模块无论是否已安装都会重装一次，
        /// 因此「首次安装」与「更新到指定 tag」共用同一条路径。
        /// </summary>
        /// <param name="registry">模块目录（内置或远程）。</param>
        /// <param name="targetId">要安装的模块 id。</param>
        /// <param name="onCompleted">完成回调 (成功, 消息)。消息里含本次补装了哪些依赖。</param>
        public static void InstallWithDependencies(CoffeeBeanRegistryData registry, string targetId,
            Action<bool, string> onCompleted = null)
        {
            ModuleInstallPlan plan = ModuleDependencyResolver.Resolve(registry, targetId, GetRegisteredPackageIds());
            InstallPlan(plan, targetId, onCompleted);
        }

        /// <summary>按已算好的计划执行安装（UI 可先展示计划再调用）。<paramref name="targetId"/> 传 null 表示批量。</summary>
        public static void InstallPlan(ModuleInstallPlan plan, string targetId, Action<bool, string> onCompleted = null)
        {
            if (plan == null)
            {
                onCompleted?.Invoke(false, "安装计划为空。");
                return;
            }
            if (plan.HasErrors)
            {
                onCompleted?.Invoke(false, plan.Error);
                return;
            }

            RunAddAndRemove(UrlsOf(plan.Packages), null, DescribeInstall(plan, targetId), onCompleted);
        }

        /// <summary>
        /// 安装**多个**目标模块（各自依赖闭包合并成一份计划，**一次** UPM 请求提交）。
        /// </summary>
        /// <param name="includePresentTargets">
        /// 已安装的目标是否也重新 Add 一次：
        /// · <c>false</c>（一键安装所有依赖）：只装**缺的**，已装的不动 —— 否则会把整个目录原样重装一遍；
        /// · <c>true</c>（批量更新）：已装的也重装，从而升级到 registry 里的 latest tag。
        /// </param>
        /// <param name="onProgress">进度回调；一次 UPM 请求没有细粒度进度，只在开始时报一次总量。</param>
        public static void InstallMany(CoffeeBeanRegistryData registry, IList<string> targetIds,
            bool includePresentTargets = false,
            Action<bool, string> onCompleted = null, Action<int, int, string> onProgress = null)
        {
            ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(registry, targetIds,
                GetRegisteredPackageIds(), includePresentTargets);
            if (plan.HasErrors)
            {
                onCompleted?.Invoke(false, plan.Error);
                return;
            }

            List<string> urls = UrlsOf(plan.Packages);
            onProgress?.Invoke(0, urls.Count, string.Empty);
            RunAddAndRemove(urls, null, DescribeInstall(plan, null), onCompleted);
        }

        /// <summary>
        /// 卸载多个包。**传入顺序会被忽略** —— 内部按"依赖方先卸"重排，
        /// 因为 UPM 不允许移除一个仍被其它包依赖的包（会直接失败）。
        /// 整批用**一次** <c>Client.AddAndRemove</c> 提交。
        /// </summary>
        public static void UninstallMany(CoffeeBeanRegistryData registry, IList<string> packageIds,
            Action<bool, string> onCompleted = null, Action<int, int, string> onProgress = null)
        {
            List<string> order = ModuleDependencyResolver.ResolveUninstallOrder(registry, packageIds);
            onProgress?.Invoke(0, order.Count, string.Empty);
            RunAddAndRemove(null, order, DescribeUninstall(order), onCompleted);
        }

        /// <summary>
        /// 卸载模块。
        /// </summary>
        /// <param name="packageId">包名，如 com.coffeebean.events。</param>
        /// <param name="onCompleted">完成回调 (成功, 消息)。</param>
        public static void Uninstall(string packageId, Action<bool, string> onCompleted = null)
        {
            RunAddAndRemove(null, new[] { packageId }, $"已卸载 {packageId}。", onCompleted);
        }

        /// <summary>
        /// 工程当前「已有」的包名集合：manifest 里显式声明的 + UPM 解析出来的（含间接依赖）。
        /// 依赖解析据此决定哪些需要补装。
        /// </summary>
        public static HashSet<string> GetRegisteredPackageIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) manifest 顶层 dependencies 的键（解析失败也不影响，后面还有 RegisteredPackages 兜底）
            try
            {
                const string manifestPath = "Packages/manifest.json";
                if (File.Exists(manifestPath))
                {
                    string text = File.ReadAllText(manifestPath);
                    // 包名形如 com.foo.bar：至少两段、只含小写字母/数字/点/连字符，
                    // 借此排除 "dependencies" 这类非包名键。
                    foreach (Match m in Regex.Matches(text, "\"([a-z][a-z0-9]*(?:\\.[a-z0-9\\-]+)+)\"\\s*:"))
                    {
                        ids.Add(m.Groups[1].Value);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoffeeBean] 读取 manifest 失败，仅按已解析包判断依赖: {e.Message}");
            }

            // 2) UPM 已解析的全部包（含间接依赖）
            try
            {
                foreach (UnityEditor.PackageManager.PackageInfo p in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    if (p != null && !string.IsNullOrEmpty(p.name)) ids.Add(p.name);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoffeeBean] 获取已注册包失败: {e.Message}");
            }

            return ids;
        }

        /// <summary>
        /// 只读 **manifest 顶层 dependencies** 里直接声明的 <c>com.coffeebean.*</c> 包名。
        ///
        /// 卸载必须用这个而不是"已解析的全部包"：<c>Client.Remove</c> 只能移除
        /// manifest 里显式声明的包；对只作为间接依赖出现的包会直接失败。
        /// </summary>
        public static List<string> GetManifestCoffeeBeanModules()
        {
            var result = new List<string>();
            try
            {
                const string manifestPath = "Packages/manifest.json";
                if (!File.Exists(manifestPath)) return result;

                string text = File.ReadAllText(manifestPath);
                foreach (Match m in Regex.Matches(text, "\"([a-z][a-z0-9]*(?:\\.[a-z0-9\\-]+)+)\"\\s*:"))
                {
                    string id = m.Groups[1].Value;
                    if (id.StartsWith("com.coffeebean.", StringComparison.OrdinalIgnoreCase)
                        && !result.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(id);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoffeeBean] 读取 manifest 直接依赖失败: {e.Message}");
            }
            return result;
        }

        // ========== 内部 ==========

        /// <summary>
        /// 用**一次** <c>Client.AddAndRemove</c> 提交整批增删。
        ///
        /// **为什么必须一次，而不是逐个 Client.Add**：
        /// Unity 文档对 <c>AddAndRemove</c> 的原话是它 "only has to solve the dependency list once,
        /// instead of constructing a new dependency graph after each call"。逐个发在批量场景下有两个后果：
        ///
        /// 1. **慢到像卡死**：N 个包 = N 次完整依赖图求解 + N 次脚本导入/域重载；
        /// 2. **更糟的是会断链**：逐个发时的完成通知挂在 <c>EditorApplication.update</c> 上，
        ///    而域重载会丢掉旧域里注册的回调 —— 链一断，完成回调永不触发，
        ///    界面上残留的"进行中"状态就再也清不掉了（表现就是 Unity 卡死）。
        ///
        /// 一次提交只有一个请求、一次解析，两个问题一起消失。
        /// </summary>
        private static void RunAddAndRemove(IEnumerable<string> addUrls, IEnumerable<string> removeIds,
            string summary, Action<bool, string> onCompleted)
        {
            string[] toAdd = addUrls == null
                ? new string[0]
                : addUrls.Where(u => !string.IsNullOrEmpty(u)).ToArray();
            string[] toRemove = removeIds == null
                ? new string[0]
                : removeIds.Where(id => !string.IsNullOrEmpty(id)).ToArray();

            if (toAdd.Length == 0 && toRemove.Length == 0)
            {
                onCompleted?.Invoke(true, summary);
                return;
            }

            AddAndRemoveRequest request;
            try
            {
                // 文档要求：调用前确保没有别的 Client 操作在飞行中（由调用方的批次互斥保证）
                request = Client.AddAndRemove(toAdd, toRemove);
            }
            catch (Exception e)
            {
                onCompleted?.Invoke(false, $"提交 UPM 请求失败: {e.Message}");
                return;
            }

            PollUntilCompleted(request, ok =>
            {
                if (!ok)
                {
                    var attempt = new List<string>();
                    if (toAdd.Length > 0) attempt.Add("添加 " + string.Join("、", toAdd));
                    if (toRemove.Length > 0) attempt.Add("移除 " + string.Join("、", toRemove));
                    onCompleted?.Invoke(false,
                        $"模块变更失败: {request.Error?.message ?? "unknown error"}\n（本次尝试：{string.Join("；", attempt)}）");
                    return;
                }

                AssetDatabase.Refresh();

                // UPM 说成功不等于**真的变了**：如果计划里的地址与 manifest 现状相同，
                // 这次请求就是个空操作，而上面的 summary 照样会说"已安装 N 个模块"。
                // 实测踩到过：远程目录命中 CDN 缓存（raw.githubusercontent max-age=300），
                // 拿到的还是旧目录 → 计划 = 现状 → 空操作 → 界面显示"已安装"，manifest 却纹丝不动。
                if (toAdd.Length > 0)
                {
                    List<string> missing = FindMissingManifestUrls(toAdd);
                    if (missing.Count > 0)
                    {
                        string detail =
                            $"UPM 报告成功，但 Packages/manifest.json 里没有出现这 {missing.Count} 个地址：\n  " +
                            string.Join("\n  ", missing) +
                            "\n最常见的原因：远程模块目录命中了 CDN 缓存（raw.githubusercontent 的 " +
                            "Cache-Control 是 max-age=300），拿到的还是旧目录 —— 于是「要装的版本」与现状相同，" +
                            "这次请求成了空操作。稍等片刻再试即可（框架已给目录请求加了缓存穿透参数）。";
                        Debug.LogWarning($"[CoffeeBean] {detail}");
                        onCompleted?.Invoke(false, summary + "\n\n⚠ " + detail);
                        return;
                    }
                }

                onCompleted?.Invoke(true, summary);
            });
        }

        /// <summary>计划要装的 URL 是否真的进了 manifest（读不到 manifest 就不下结论，返回空）。</summary>
        internal static List<string> FindMissingManifestUrls(IEnumerable<string> urls)
        {
            var missing = new List<string>();
            string manifest = null;
            try
            {
                const string manifestPath = "Packages/manifest.json";
                if (File.Exists(manifestPath)) manifest = File.ReadAllText(manifestPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoffeeBean] 核对 manifest 失败（不影响安装结果判断）: {e.Message}");
            }
            if (string.IsNullOrEmpty(manifest)) return missing;

            foreach (string url in urls)
            {
                if (string.IsNullOrEmpty(url)) continue;
                if (manifest.IndexOf(url, StringComparison.Ordinal) < 0) missing.Add(url);
            }
            return missing;
        }

        private static List<string> UrlsOf(List<PlannedPackage> packages)
        {
            var urls = new List<string>(packages.Count);
            foreach (PlannedPackage p in packages)
            {
                if (!string.IsNullOrEmpty(p.Url)) urls.Add(p.Url);
            }
            return urls;
        }

        private static List<string> NamesOf(List<PlannedPackage> packages)
        {
            var names = new List<string>(packages.Count);
            foreach (PlannedPackage p in packages) names.Add(p.Id);
            return names;
        }

        private static string DescribeInstall(ModuleInstallPlan plan, string targetId)
        {
            if (plan.Packages.Count == 0)
            {
                return string.IsNullOrEmpty(targetId) ? "没有需要安装的模块（都已安装）。" : $"{targetId} 无需安装。";
            }

            string text;
            if (string.IsNullOrEmpty(targetId))
            {
                // 批量：说清装了哪几个
                List<string> names = NamesOf(plan.Packages);
                var deps = new List<string>();
                foreach (PlannedPackage p in plan.Packages)
                {
                    if (!p.IsTarget) deps.Add(p.Id);
                }

                text = $"已安装 {plan.Packages.Count} 个模块：{string.Join("、", names)}。";
                if (deps.Count > 0) text += $"（其中 {deps.Count} 个是补装的依赖）";
            }
            else
            {
                var deps = new List<string>();
                foreach (PlannedPackage p in plan.Packages)
                {
                    if (!p.IsTarget) deps.Add(p.ToString());
                }
                text = deps.Count == 0
                    ? $"已安装 {targetId}（无缺失依赖）。"
                    : $"已安装 {targetId}，并自动补装 {deps.Count} 个依赖：{string.Join("、", deps)}。";
            }

            if (plan.Warnings.Count > 0) text += " 注意：" + string.Join(" ", plan.Warnings);
            return text;
        }

        private static string DescribeUninstall(List<string> order)
            => order.Count == 0
                ? "没有需要卸载的模块。"
                : $"已卸载 {order.Count} 个模块：{string.Join("、", order)}。";

        private static void PollUntilCompleted(Request request, Action<bool> onDone)
        {
            EditorApplication.update += Poll;

            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                bool ok = request.Status == StatusCode.Success;
                onDone(ok);
            }
        }
    }
}
