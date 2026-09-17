using System;
using System.Collections.Generic;
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
    /// 可通过 EditorPrefs("CoffeeBean.RegistryUrl") 配置远程地址（如 raw.githubusercontent）覆盖，
    /// 实现"目录不随 Core 版本更新"。
    /// </summary>
    public static class RegistrySource
    {
        public const string DefaultResourcePath = "coffeebean.registry";
        public const string RemoteUrlPrefKey = "CoffeeBean.RegistryUrl";

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
                    Debug.LogError($"[CoffeeBean] Failed to fetch remote registry: {request.error}");
                }
                request.Dispose();
                onCompleted?.Invoke(data);
            };
        }
    }
}
