using System;
using System.Collections.Generic;
using System.IO;
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
        /// 安装模块。
        /// </summary>
        /// <param name="packageId">包名，如 com.coffeebean.events（仅用于日志）。</param>
        /// <param name="gitUrl">git 仓库 URL，如 https://github.com/Herschy0829/com.coffeebean.events.git</param>
        /// <param name="versionTag">版本 tag，如 v1.0.0（为空则使用默认分支）。</param>
        /// <param name="onCompleted">完成回调 (成功, 消息)。</param>
        public static void Install(string packageId, string gitUrl, string versionTag, Action<bool, string> onCompleted = null)
        {
            string url = ModuleDependencyResolver.BuildUrl(gitUrl, versionTag);
            AddRequest request = Client.Add(url);
            PollUntilCompleted(request, ok => OnInstallCompleted(packageId, versionTag, ok, request, onCompleted));
        }

        /// <summary>
        /// 安装模块并**自动补装缺失依赖**（含传递依赖与第三方依赖）。
        ///
        /// CoffeeBean 模块以 git 包分发、不在任何 registry 里，所以 UPM 无法把模块 package.json 里的
        /// <c>"com.coffeebean.tools": "0.5.0"</c> 解析成地址 —— 不补装就会解析失败。本方法先按
        /// <see cref="ModuleDependencyResolver"/> 算出「第三方 → CoffeeBean 依赖 → 目标模块」的顺序，
        /// 逐个串行 Client.Add（串行是为了让每次 UPM 解析都基于上一个已就位的结果），最后刷新资源库。
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

        /// <summary>按已算好的计划执行安装（UI 可先展示计划再调用）。</summary>
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
            if (plan.Packages.Count == 0)
            {
                onCompleted?.Invoke(true, $"{targetId} 无需安装。");
                return;
            }

            var installed = new List<string>();
            InstallSequentially(plan, 0, installed, targetId, onCompleted);
        }

        /// <summary>
        /// 卸载模块。
        /// </summary>
        /// <param name="packageId">包名，如 com.coffeebean.events。</param>
        /// <param name="onCompleted">完成回调 (成功, 消息)。</param>
        public static void Uninstall(string packageId, Action<bool, string> onCompleted = null)
        {
            RemoveRequest request = Client.Remove(packageId);
            PollUntilCompleted(request, ok => OnUninstallCompleted(packageId, ok, request, onCompleted));
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

        // ========== 内部 ==========

        private static void InstallSequentially(ModuleInstallPlan plan, int index, List<string> installed,
            string targetId, Action<bool, string> onCompleted)
        {
            if (index >= plan.Packages.Count)
            {
                AssetDatabase.Refresh();
                onCompleted?.Invoke(true, BuildSummary(targetId, plan, installed));
                return;
            }

            PlannedPackage pkg = plan.Packages[index];
            AddRequest request = Client.Add(pkg.Url);
            PollUntilCompleted(request, ok =>
            {
                if (!ok)
                {
                    string done = installed.Count > 0 ? $"（已装好：{string.Join("、", installed)}）" : string.Empty;
                    onCompleted?.Invoke(false,
                        $"安装 {pkg.Id} 失败: {request.Error?.message ?? "unknown error"}{done}");
                    return;
                }
                installed.Add(pkg.Id);
                InstallSequentially(plan, index + 1, installed, targetId, onCompleted);
            });
        }

        private static string BuildSummary(string targetId, ModuleInstallPlan plan, List<string> installed)
        {
            var deps = new List<string>();
            foreach (PlannedPackage p in plan.Packages)
            {
                if (!p.IsTarget) deps.Add(p.ToString());
            }

            string head = deps.Count == 0
                ? $"已安装 {targetId}（无缺失依赖）。"
                : $"已安装 {targetId}，并自动补装 {deps.Count} 个依赖：{string.Join("、", deps)}。";

            if (installed.Count == 0) head = $"{targetId} 未发生变更。";

            if (plan.Warnings.Count > 0)
            {
                head += " 注意：" + string.Join(" ", plan.Warnings);
            }
            return head;
        }

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

        private static void OnInstallCompleted(string packageId, string versionTag, bool ok,
            AddRequest request, Action<bool, string> onCompleted)
        {
            string message = ok
                ? $"Installed {packageId} ({versionTag ?? "default branch"})."
                : $"Install failed: {request.Error?.message ?? "unknown error"}";
            if (ok) AssetDatabase.Refresh();
            onCompleted?.Invoke(ok, message);
        }

        private static void OnUninstallCompleted(string packageId, bool ok,
            RemoveRequest request, Action<bool, string> onCompleted)
        {
            string message = ok
                ? $"Uninstalled {packageId}."
                : $"Uninstall failed: {request.Error?.message ?? "unknown error"}";
            if (ok) AssetDatabase.Refresh();
            onCompleted?.Invoke(ok, message);
        }
    }
}
