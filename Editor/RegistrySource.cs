using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// registry 之外的第三方依赖（如 com.cysharp.memorypack）。
    /// 这类包不在任何 registry 里，UPM 无法按版本号解析，必须给完整 UPM 引用才能自动安装。
    /// </summary>
    [Serializable]
    public sealed class CoffeeBeanExternalDependency
    {
        /// <summary>包名，如 com.cysharp.memorypack。</summary>
        public string id;

        /// <summary>完整 UPM 引用：git URL（可带 ?path= 与 #tag）、file: 路径或 包名@版本。</summary>
        public string url;
    }

    [Serializable]
    public sealed class CoffeeBeanRegistryEntry
    {
        public string id;
        public string repo;
        public string latest;

        /// <summary>
        /// 依赖的 CoffeeBean 模块 id（必须在同一 registry 内，可传递展开）。
        /// 与模块自身 package.json 的 dependencies 保持一致 —— 后者是 UPM 用来解析的，
        /// 前者是框架用来在安装时「自动补装」的。
        /// </summary>
        public string[] dependencies;

        /// <summary>依赖的第三方包（不在 registry 内，需显式 UPM 地址）。</summary>
        public CoffeeBeanExternalDependency[] externalDependencies;
    }

    [Serializable]
    public sealed class CoffeeBeanRegistryData
    {
        public int version;
        public List<CoffeeBeanRegistryEntry> modules = new List<CoffeeBeanRegistryEntry>();
    }

    /// <summary>
    /// 官方模块目录：默认读取内置 Resources 中的 coffeebean.registry.json（保证离线可用）；
    /// 同时有一个**默认远程地址**指向官方仓库 main 上的同一份 json，可用于覆盖内置目录，
    /// 实现"目录不随 Core 版本更新"。见 <see cref="DefaultRemoteUrl"/>。
    /// </summary>
    public static class RegistrySource
    {
        public const string DefaultResourcePath = "coffeebean.registry";
        public const string RemoteUrlPrefKey = "CoffeeBean.RegistryUrl";

        /// <summary>
        /// **默认**远程目录地址（官方仓库 main 分支上的 registry.json）。
        ///
        /// 为什么需要它：内置目录是**编译进 Core 包**的，所以它的新鲜度 == 用户装的 Core 版本。
        /// 于是"发版只改 registry 里的指针"这类更新对用户完全不可见 —— 实测踩到过：
        /// Core 0.1.56 的内置目录里写着 tools v0.9.0，用户点「检查更新」得到
        /// "所有模块已是最新版本"，而 tools v0.10.0 早就发出去了。
        /// 想看到更新得先更新 Core，想更新 Core 又得先看到更新 —— 死锁。
        ///
        /// 所以默认就指向官方 main 上的目录：**目录随发布走，不随 Core 版本走**。
        /// 用户可用 EditorPrefs("CoffeeBean.RegistryUrl") 覆盖（内网镜像 / 锁定分支）。
        /// </summary>
        public const string DefaultRemoteUrl =
            "https://raw.githubusercontent.com/Herschy0829/com.coffeebean.core/main/Editor/Resources/coffeebean.registry.json";

        /// <summary>实际生效的远程目录地址：EditorPrefs 有值就用它，否则用官方默认地址。</summary>
        public static string ResolveUrl()
        {
            string configured = EditorPrefs.GetString(RemoteUrlPrefKey, string.Empty);
            return string.IsNullOrEmpty(configured) ? DefaultRemoteUrl : configured;
        }

        public static CoffeeBeanRegistryData LoadBuiltIn()
        {
            TextAsset asset = Resources.Load<TextAsset>(DefaultResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[CoffeeBean] Built-in registry resource not found: " + DefaultResourcePath);
                return new CoffeeBeanRegistryData();
            }
            return Parse(asset.text);
        }

        public static CoffeeBeanRegistryData Parse(string json)
        {
            try
            {
                return JsonUtility.FromJson<CoffeeBeanRegistryData>(json) ?? new CoffeeBeanRegistryData();
            }
            catch (Exception e)
            {
                Debug.LogError($"[CoffeeBean] Failed to parse registry json: {e.Message}");
                return new CoffeeBeanRegistryData();
            }
        }

        /// <summary>异步拉取远程 registry（结果为空表示失败）。</summary>
        public static void LoadRemote(string url, Action<CoffeeBeanRegistryData> onCompleted)
        {
            if (string.IsNullOrEmpty(url))
            {
                onCompleted?.Invoke(null);
                return;
            }

            var request = UnityWebRequest.Get(url);
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                CoffeeBeanRegistryData data = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    data = Parse(request.downloadHandler.text);
                }
                else
                {
                    // 只是 Warning：拉不到远程目录是**预期内**的（离线、被墙、镜像没起来），
                    // 内置目录照常可用。调用方（Module Manager）会在界面上明确写出
                    // "这份清单可能漏报更新"，不需要再用红色报错吓人。
                    Debug.LogWarning($"[CoffeeBean] 远程模块目录拉取失败（{url}）：{request.error}。" +
                                     "已退回内置目录 —— 内置目录只随 Core 版本更新，可能漏报更新。");
                }
                request.Dispose();
                onCompleted?.Invoke(data);
            };
        }
    }
}
