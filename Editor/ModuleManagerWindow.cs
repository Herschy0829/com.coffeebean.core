using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
// Unity 中同时存在 UnityEditor.PackageInfo（旧）与 UnityEditor.PackageManager.PackageInfo，
// 用别名消除歧义。
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// CoffeeBean 模块管理器窗口（Window &gt; CoffeeBean &gt; Module Manager）。
    /// Installed：从 UPM 注册的 com.coffeebean.* 包读取；Available：来自内置/远程 registry。
    /// 支持：检查已安装模块是否有更新（比对 registry 的 latest tag）→ 窗口内一键更新。
    /// 安装 / 卸载 / 升级通过 <see cref="ModuleInstaller"/> 驱动 Unity Package Manager。
    /// </summary>
    public sealed class ModuleManagerWindow : EditorWindow
    {
        private CoffeeBeanRegistryData _registry = new CoffeeBeanRegistryData();
        private List<PackageInfo> _installed = new List<PackageInfo>();
        private readonly Dictionary<string, string> _installedTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 注意：EditorWindow 是 ScriptableObject，不能在字段初始化器里调用 EditorPrefs（原生调用），
        // _remoteUrl 在 OnEnable 中加载。
        private string _remoteUrl;
        private Vector2 _installedScroll;
        private Vector2 _availableScroll;
        private string _status = "Ready.";
        private bool _busy;

        [MenuItem("Window/CoffeeBean/Module Manager")]
        public static void Open()
        {
            var window = GetWindow<ModuleManagerWindow>("CoffeeBean Module Manager");
            window.minSize = new Vector2(760, 420);
            window.position = new Rect(100, 100, 900, 560);
            window.Refresh();
        }

        private void OnEnable()
        {
            _remoteUrl = EditorPrefs.GetString(RegistrySource.RemoteUrlPrefKey, string.Empty);
            Refresh();
        }

        private void Refresh()
        {
            _registry = RegistrySource.LoadBuiltIn();
            ReloadInstalled();
        }

        private void ReloadInstalled()
        {
            // 只管理 CoffeeBean 模块（com.coffeebean.*），不管理其他来源的包
            _installed = PackageInfo.GetAllRegisteredPackages()
                .Where(p => p.name.StartsWith("com.coffeebean."))
                .OrderBy(p => p.name)
                .ToList();
            ReloadInstalledTags();
        }

        /// <summary>
        /// 从项目 manifest.json 读取每个已安装模块的 git 引用 tag（最可靠：精确匹配引用字符串）。
        /// 例如 "com.coffeebean.core": "https://github.com/...git#v0.1.2" → tag = "v0.1.2"。
        /// </summary>
        private void ReloadInstalledTags()
        {
            _installedTags.Clear();
            try
            {
                string manifestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "manifest.json");
                if (!File.Exists(manifestPath)) return;
                string json = File.ReadAllText(manifestPath);

                foreach (PackageInfo pkg in _installed)
                {
                    var match = Regex.Match(json, "\"" + Regex.Escape(pkg.name) + "\"\\s*:\\s*\"([^\"]*)\"");
                    if (!match.Success) continue;
                    string reference = match.Groups[1].Value;
                    if (!reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    int hashIdx = reference.LastIndexOf('#');
                    if (hashIdx >= 0 && hashIdx < reference.Length - 1)
                        _installedTags[pkg.name] = reference.Substring(hashIdx + 1);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CoffeeBean] Failed to read manifest for update check: " + e.Message);
            }
        }

        private void LoadRemoteRegistry()
        {
            if (string.IsNullOrEmpty(_remoteUrl))
            {
                _status = "Remote registry URL is empty.";
                return;
            }
            EditorPrefs.SetString(RegistrySource.RemoteUrlPrefKey, _remoteUrl);
            _busy = true;
            _status = "Fetching remote registry...";
            RegistrySource.LoadRemote(_remoteUrl, data =>
            {
                _busy = false;
                if (data == null || data.modules.Count == 0)
                {
                    _status = "Remote registry empty or failed.";
                    return;
                }
                _registry = data;
                _status = $"Remote registry loaded ({data.modules.Count} modules).";
                Repaint();
            });
        }

        // ========== 检查更新 ==========

        /// <summary>检查已安装模块是否有更新：registry 优先用远程（若配置了 URL），否则用内置。</summary>
        private void CheckForUpdates()
        {
            if (!string.IsNullOrEmpty(_remoteUrl))
            {
                _busy = true;
                _status = "检查更新（远程 registry）...";
                RegistrySource.LoadRemote(_remoteUrl, data =>
                {
                    _busy = false;
                    if (data != null && data.modules.Count > 0) _registry = data;
                    FinishUpdateCheck();
                });
            }
            else
            {
                FinishUpdateCheck();
            }
        }

        private void FinishUpdateCheck()
        {
            ReloadInstalled();
            var updatable = _installed.Where(p => IsOutdated(p.name, out _)).ToList();

            if (updatable.Count == 0)
            {
                _status = "所有模块已是最新版本。";
                EditorUtility.DisplayDialog("检查更新", "所有已安装模块已是最新版本。", "OK");
                Repaint();
                return;
            }

            var lines = updatable.Select(p =>
            {
                _installedTags.TryGetValue(p.name, out string cur);
                IsOutdated(p.name, out string latest);
                return $"- {p.name}: {(string.IsNullOrEmpty(cur) ? "?" : cur)} → {latest}";
            });
            _status = $"发现 {updatable.Count} 个可更新模块。";
            bool updateAll = EditorUtility.DisplayDialog("发现更新",
                string.Join("\n", lines) + "\n\n是否立即全部更新？", "全部更新", "稍后");
            if (updateAll)
            {
                foreach (PackageInfo p in updatable)
                {
                    CoffeeBeanRegistryEntry entry = FindRegistryEntry(p.name);
                    if (entry != null) InstallFromEntry(entry, confirmed: true);
                }
            }
            Repaint();
        }

        private CoffeeBeanRegistryEntry FindRegistryEntry(string packageId)
            => _registry.modules.FirstOrDefault(e => string.Equals(e.id, packageId, StringComparison.OrdinalIgnoreCase));

        /// <summary>是否可更新：registry 有 latest，且已安装 tag 是语义化版本且低于 latest。</summary>
        private bool IsOutdated(string packageId, out string latestTag)
        {
            latestTag = null;
            CoffeeBeanRegistryEntry entry = FindRegistryEntry(packageId);
            if (entry == null || string.IsNullOrEmpty(entry.latest)) return false;
            latestTag = entry.latest;
            if (!_installedTags.TryGetValue(packageId, out string installedTag) || string.IsNullOrEmpty(installedTag)) return false;
            // 非 tag 引用（分支/提交）无法对比，不算可更新
            if (!TryParseVersion(installedTag, out _)) return false;
            return CompareTags(installedTag, latestTag) < 0;
        }

        internal static bool TryParseVersion(string tag, out int[] parts)
        {
            parts = null;
            string s = tag;
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            string[] segments = s.Split('.');
            if (segments.Length == 0 || segments.Length > 4) return false;
            parts = new int[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                if (!int.TryParse(segments[i], out parts[i])) return false;
            }
            return true;
        }

        internal static int CompareTags(string a, string b)
        {
            if (!TryParseVersion(a, out int[] pa) || !TryParseVersion(b, out int[] pb))
                return string.CompareOrdinal(a, b);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length ? pa[i] : 0;
                int vb = i < pb.Length ? pb[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        // ========== GUI ==========

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawInstalledPanel();
            DrawAvailablePanel();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Status: " + _status, EditorStyles.helpBox);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.enabled = !_busy;
            if (GUILayout.Button("检查更新", EditorStyles.toolbarButton)) CheckForUpdates();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            {
                Refresh();
                _status = "Refreshed.";
            }
            if (GUILayout.Button("Load Remote Registry", EditorStyles.toolbarButton)) LoadRemoteRegistry();
            GUI.enabled = true;
            _remoteUrl = EditorGUILayout.TextField(_remoteUrl, GUILayout.MinWidth(220));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInstalledPanel()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(320), GUILayout.MaxWidth(480));
            EditorGUILayout.LabelField("Installed", EditorStyles.boldLabel);
            _installedScroll = EditorGUILayout.BeginScrollView(_installedScroll, GUILayout.Height(320));
            foreach (PackageInfo pkg in _installed)
            {
                bool outdated = IsOutdated(pkg.name, out string latest);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(pkg.name, EditorStyles.boldLabel);
                string tagInfo = _installedTags.TryGetValue(pkg.name, out string tag) ? "  ref:" + tag : "";
                string latestInfo = outdated ? $"  → 有更新 {latest}" : "";
                EditorGUILayout.LabelField($"v{pkg.version}  [{pkg.source}]{tagInfo}{latestInfo}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (outdated)
                {
                    CoffeeBeanRegistryEntry entry = FindRegistryEntry(pkg.name);
                    if (entry != null && GUILayout.Button("Update", GUILayout.Width(70))) UpdateFromEntry(entry);
                }
                if (GUILayout.Button("Uninstall", GUILayout.Width(80))) ConfirmUninstallByName(pkg.name);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }
            if (_installed.Count == 0)
                EditorGUILayout.LabelField("No com.coffeebean.* packages installed.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawAvailablePanel()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(320));
            EditorGUILayout.LabelField("Available (registry)", EditorStyles.boldLabel);
            _availableScroll = EditorGUILayout.BeginScrollView(_availableScroll, GUILayout.Height(320));
            foreach (CoffeeBeanRegistryEntry entry in _registry.modules)
            {
                bool installed = _installed.Any(p => p.name == entry.id);
                bool outdated = IsOutdated(entry.id, out _);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(entry.id, EditorStyles.boldLabel);
                string latestInfo = "latest: " + (entry.latest ?? "?");
                if (installed && outdated) latestInfo += "  ← 当前非最新";
                EditorGUILayout.LabelField(latestInfo, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (installed)
                {
                    if (outdated && GUILayout.Button("Update", GUILayout.Width(70))) UpdateFromEntry(entry);
                    if (GUILayout.Button("Uninstall", GUILayout.Width(80))) ConfirmUninstallByName(entry.id);
                }
                else
                {
                    if (GUILayout.Button("Install", GUILayout.Width(70))) InstallFromEntry(entry);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }
            if (_registry.modules.Count == 0)
                EditorGUILayout.LabelField("No modules in registry.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ========== 安装 / 更新 / 卸载 ==========

        /// <summary>更新已安装模块（带确认；Update 本质 = 用新 tag 重新 Add，UPM 会替换引用）。</summary>
        private void UpdateFromEntry(CoffeeBeanRegistryEntry entry)
        {
            _installedTags.TryGetValue(entry.id, out string current);
            if (!EditorUtility.DisplayDialog("更新模块",
                    $"更新 {entry.id}\n  当前: {(string.IsNullOrEmpty(current) ? "?" : current)}\n  最新: {entry.latest}\n\n确定更新？",
                    "更新", "取消")) return;
            InstallFromEntry(entry, confirmed: true);
        }

        private void InstallFromEntry(CoffeeBeanRegistryEntry entry, bool confirmed = false)
        {
            if (!confirmed && _installed.Any(p => p.name == entry.id))
            {
                UpdateFromEntry(entry);
                return;
            }

            bool isUpdate = _installed.Any(p => p.name == entry.id);
            _busy = true;
            _status = isUpdate ? $"Updating {entry.id} → {entry.latest}..." : $"Installing {entry.id}...";
            ModuleInstaller.Install(entry.id, entry.repo, entry.latest, (ok, message) =>
            {
                _busy = false;
                _status = message;
                ReloadInstalled();
                Repaint();
            });
        }

        private void ConfirmUninstallByName(string packageId)
        {
            List<string> dependents = FindDependents(packageId);
            if (dependents.Count > 0)
            {
                EditorUtility.DisplayDialog("Cannot uninstall",
                    $"'{packageId}' is required by:\n- {string.Join("\n- ", dependents)}\n\nUninstall those first.",
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Uninstall module",
                    $"Remove '{packageId}' from this project?", "Uninstall", "Cancel")) return;

            _busy = true;
            _status = $"Uninstalling {packageId}...";
            ModuleInstaller.Uninstall(packageId, (ok, message) =>
            {
                _busy = false;
                _status = message;
                ReloadInstalled();
                Repaint();
            });
        }

        /// <summary>查找已安装包中直接依赖指定包的。</summary>
        private List<string> FindDependents(string packageId)
        {
            var result = new List<string>();
            foreach (PackageInfo pkg in _installed)
            {
                if (pkg.name == packageId) continue;
                if (pkg.dependencies != null && pkg.dependencies.Any(d => d.name == packageId))
                    result.Add($"{pkg.name} (v{pkg.version})");
            }
            return result;
        }
    }
}
