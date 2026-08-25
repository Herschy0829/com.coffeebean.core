using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 安装 / 卸载 / 升级通过 <see cref="ModuleInstaller"/> 驱动 Unity Package Manager。
    /// </summary>
    public sealed class ModuleManagerWindow : EditorWindow
    {
        private CoffeeBeanRegistryData _registry = new CoffeeBeanRegistryData();
        private List<PackageInfo> _installed = new List<PackageInfo>();
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
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(pkg.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"v{pkg.version}  [{pkg.source}]", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
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
                bool outdated = installed && !string.IsNullOrEmpty(entry.latest) &&
                                _installed.Any(p => p.name == entry.id && p.git != null && p.git.revision != entry.latest);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(entry.id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("latest: " + (entry.latest ?? "?"), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (installed)
                {
                    if (outdated && GUILayout.Button("Update", GUILayout.Width(70))) InstallFromEntry(entry);
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

        private void InstallFromEntry(CoffeeBeanRegistryEntry entry)
        {
            _busy = true;
            _status = $"Installing {entry.id}...";
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
