using System;
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
            string url = gitUrl;
            if (!string.IsNullOrEmpty(versionTag)) url += "#" + versionTag;

            AddRequest request = Client.Add(url);
            PollUntilCompleted(request, ok => OnInstallCompleted(packageId, versionTag, ok, request, onCompleted));
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
