using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
// Unity 中同时存在 UnityEditor.PackageInfo（旧）与 UnityEditor.PackageManager.PackageInfo，
// 用别名消除歧义。
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// CoffeeBean 工具中心窗口（Window &gt; CoffeeBean，唯一入口）：
    /// - 左侧：工具导航 —— 各模块注册的 Editor 工具（Excel/Purchase 等，反射发现）一键打开；
    ///   以及本窗口内置的"模块管理"。
    /// - 右侧：内容区 —— 模块管理（已安装 Installed / 可安装 Available、检查更新、远程 registry）。
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
        private string _status = "就绪。";
        private bool _busy;

        // ========== 工具导航 ==========
        private List<CoffeeBeanToolRegistry.ToolEntry> _tools = new List<CoffeeBeanToolRegistry.ToolEntry>();
        private Vector2 _toolsScroll;
        private string _selectedTool; // 当前选中工具标题（"模块管理"为内置项）

        private const string BuiltinModuleManager = "模块管理";

        [MenuItem("Window/CoffeeBean")]
        public static void Open()
        {
            var window = GetWindow<ModuleManagerWindow>("CoffeeBean");
            window.minSize = new Vector2(880, 480);
            window.position = new Rect(100, 100, 1020, 620);
            window.Refresh();
        }

        private void OnEnable()
        {
            _remoteUrl = EditorPrefs.GetString(RegistrySource.RemoteUrlPrefKey, string.Empty);
            _tools = CoffeeBeanToolRegistry.Scan();
            _selectedTool = BuiltinModuleManager;
            Refresh();
        }

        private void Refresh()
        {
            _registry = RegistrySource.LoadBuiltIn();
            ReloadInstalled();
        }

        /// <summary>
        /// 刷新已安装模块列表。用 Client.List 轮询获取最新注册快照
        /// （PackageInfo.GetAllRegisteredPackages 是缓存，安装/更新后可能滞后导致版本号不刷新）。
        /// </summary>
        private void ReloadInstalled(Action onCompleted = null)
        {
            ListRequest request = Client.List();
            EditorApplication.update += Poll;

            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                if (request.Status == StatusCode.Success && request.Result != null)
                {
                    // 只管理 CoffeeBean 模块（com.coffeebean.*），不管理其他来源的包
                    _installed = request.Result
                        .Where(p => p.name.StartsWith("com.coffeebean."))
                        .OrderBy(p => p.name)
                        .ToList();
                }
                ReloadInstalledTags();
                onCompleted?.Invoke();
                Repaint();
            }
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
                _status = "远程 registry 地址为空。";
                return;
            }
            EditorPrefs.SetString(RegistrySource.RemoteUrlPrefKey, _remoteUrl);
            _busy = true;
            _status = "正在拉取远程 registry...";
            RegistrySource.LoadRemote(_remoteUrl, data =>
            {
                _busy = false;
                if (data == null || data.modules.Count == 0)
                {
                    _status = "远程 registry 为空或拉取失败。";
                    return;
                }
                _registry = data;
                _status = $"远程 registry 已加载（{data.modules.Count} 个模块）。";
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
                    ReloadInstalled(ShowUpdateResult);
                });
            }
            else
            {
                ReloadInstalled(ShowUpdateResult);
            }
        }

        private void ShowUpdateResult()
        {
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
            => CoffeeBeanVersion.TryParse(tag, out parts);

        internal static int CompareTags(string a, string b)
            => CoffeeBeanVersion.Compare(a, b);

        // ========== GUI ==========

        private const string NavGroupManage = "管理";
        private const string NavGroupTools = "工具";

        /// <summary>框架当前版本（显示用；发布时随 ModuleMarker 同步）。</summary>
        private const string FrameworkVersion = "0.1.33";

        private void OnGUI()
        {
            DrawBrandBar();     // 顶部品牌 + 概览徽章
            DrawToolbar();      // 操作栏
            DrawBody();         // 主体：左侧导航 + 右侧内容
            DrawStatusBar();    // 底部状态
        }

        /// <summary>顶部品牌区：标题 + 版本 + 概览徽章（已装/可装/可更新）。</summary>
        private void DrawBrandBar()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();

            // 标题
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            GUILayout.Label("☕ CoffeeBean", titleStyle);
            GUILayout.Space(6);
            GUILayout.Label($"框架工具中心 v{FrameworkVersion}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            // 概览徽章
            int outdatedCount = _installed.Count(p => IsOutdated(p.name, out _));
            DrawBadge($"已装 {_installed.Count}");
            DrawBadge($"可装 {_registry.modules.Count(m => !_installed.Any(p => p.name == m.id))}");
            if (outdatedCount > 0)
                DrawBadgeWarn($"可更新 {outdatedCount}");
            else
                DrawBadge("已是最新");

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>概览徽章（小标签）。</summary>
        private void DrawBadge(string text)
        {
            var box = new GUIStyle("HelpBox") { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 2, 2) };
            GUILayout.Label(text, box);
        }

        /// <summary>概览徽章（警示色：有更新）。</summary>
        private void DrawBadgeWarn(string text)
        {
            var style = new GUIStyle("HelpBox") { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 2, 2) };
            style.normal.textColor = new Color(0.9f, 0.55f, 0.1f);
            GUILayout.Label(text, style);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.enabled = !_busy;
            if (GUILayout.Button("检查更新", EditorStyles.toolbarButton)) CheckForUpdates();
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
            {
                CoffeeBeanToolRegistry.RefreshCache();
                _tools = CoffeeBeanToolRegistry.Scan();
                Refresh();
                _status = "已刷新。";
            }
            if (GUILayout.Button("加载远程 registry", EditorStyles.toolbarButton)) LoadRemoteRegistry();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            _remoteUrl = EditorGUILayout.TextField(_remoteUrl, GUILayout.MinWidth(220));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBody()
        {
            EditorGUILayout.BeginHorizontal();
            DrawToolNav();        // 左侧：分组导航
            DrawContentPanel();   // 右侧：内容区
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.LabelField(_status, EditorStyles.helpBox);
        }

        /// <summary>左侧：分组导航（管理 / 工具），选中高亮 + 描述 tooltip。</summary>
        private void DrawToolNav()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(240));
            _toolsScroll = EditorGUILayout.BeginScrollView(_toolsScroll);

            // —— 管理组 ——
            DrawNavGroupLabel(NavGroupManage);
            if (DrawNavButton("模块管理", "安装 / 卸载 / 更新 CoffeeBean 模块", _selectedTool == BuiltinModuleManager))
            {
                _selectedTool = BuiltinModuleManager;
            }

            // —— 工具组 ——
            DrawNavGroupLabel(NavGroupTools);
            if (_tools.Count == 0)
            {
                EditorGUILayout.LabelField("（未发现模块工具）", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                string lastModule = null;
                foreach (CoffeeBeanToolRegistry.ToolEntry tool in _tools)
                {
                    string moduleLabel = string.IsNullOrEmpty(tool.Module) ? "其他" : tool.Module;
                    if (lastModule != null && moduleLabel != lastModule)
                    {
                        EditorGUILayout.Space(2);
                    }
                    lastModule = moduleLabel;

                    bool selected = _selectedTool == tool.Title;
                    if (DrawNavButton($"{moduleLabel} · {tool.Title}", tool.Description, selected))
                    {
                        _selectedTool = tool.Title;
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>导航分组标题（小字 + 分隔）。</summary>
        private static void DrawNavGroupLabel(string groupName)
        {
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.5f, 0.6f, 0.8f) } };
            EditorGUILayout.LabelField(groupName, style);
        }

        /// <summary>导航项（选中态高亮）。</summary>
        private static bool DrawNavButton(string text, string tooltip, bool selected)
        {
            var content = new GUIContent(text, tooltip);
            if (selected)
            {
                var selectedStyle = new GUIStyle("SelectionRect") { richText = true, alignment = TextAnchor.MiddleLeft };
                selectedStyle.padding = new RectOffset(8, 4, 4, 4);
                GUILayout.Label("▸ " + text, selectedStyle, GUILayout.Height(30));
                return false;
            }
            var style = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
            style.padding = new RectOffset(8, 4, 4, 4);
            return GUILayout.Button(content, style, GUILayout.Height(30));
        }

        /// <summary>右侧：内容区（模块管理 或 工具卡片）。</summary>
        private void DrawContentPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            if (_selectedTool == BuiltinModuleManager)
            {
                DrawModuleManager();
            }
            else
            {
                DrawToolCard();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>工具卡片视图：选中工具时显示详情 + 大"打开"按钮（替代空占位）。</summary>
        private void DrawToolCard()
        {
            CoffeeBeanToolRegistry.ToolEntry tool = _tools.FirstOrDefault(t => t.Title == _selectedTool);
            if (tool == null)
            {
                _selectedTool = BuiltinModuleManager;
                return;
            }

            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
            EditorGUILayout.Space(6);

            // 图标 + 标题
            EditorGUILayout.LabelField("🧰 " + tool.Title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });
            EditorGUILayout.Space(4);

            // 描述
            if (!string.IsNullOrEmpty(tool.Description))
            {
                EditorGUILayout.LabelField(tool.Description, EditorStyles.wordWrappedLabel);
            }

            // 元信息
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("所属模块", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(tool.Module) ? "（未标注）" : tool.Module, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("程序集", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(tool.WindowType?.Assembly?.GetName()?.Name ?? "?", EditorStyles.miniLabel);

            EditorGUILayout.Space(12);

            // 大打开按钮
            if (GUILayout.Button("打开「" + tool.Title + "」", GUILayout.Height(44)))
            {
                tool.Open();
            }
            EditorGUILayout.HelpBox("工具在独立窗口打开，本窗口保持为统一入口。", MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawModuleManager()
        {
            EditorGUILayout.LabelField("模块管理", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            DrawInstalledPanel();
            DrawAvailablePanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInstalledPanel()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(320), GUILayout.MaxWidth(480));
            EditorGUILayout.LabelField("已安装", EditorStyles.boldLabel);
            _installedScroll = EditorGUILayout.BeginScrollView(_installedScroll, GUILayout.Height(320));
            foreach (PackageInfo pkg in _installed)
            {
                bool outdated = IsOutdated(pkg.name, out string latest);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(pkg.name, EditorStyles.boldLabel);
                // 版本以 manifest 引用里的 tag 为准（每次重读必然最新）；无 tag（file/embedded）才用 pkg.version
                string versionText = _installedTags.TryGetValue(pkg.name, out string tag) && !string.IsNullOrEmpty(tag)
                    ? tag
                    : "v" + pkg.version;
                string latestInfo = outdated ? $"  → 有更新 {latest}" : "";
                EditorGUILayout.LabelField($"{versionText}  [{pkg.source}]{latestInfo}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (outdated)
                {
                    CoffeeBeanRegistryEntry entry = FindRegistryEntry(pkg.name);
                    if (entry != null && GUILayout.Button("更新", GUILayout.Width(60))) UpdateFromEntry(entry);
                }
                if (GUILayout.Button("卸载", GUILayout.Width(60))) ConfirmUninstallByName(pkg.name);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }
            if (_installed.Count == 0)
                EditorGUILayout.LabelField("未安装任何 com.coffeebean.* 包。", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawAvailablePanel()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(320));
            EditorGUILayout.LabelField("可安装（Available）", EditorStyles.boldLabel);
            _availableScroll = EditorGUILayout.BeginScrollView(_availableScroll, GUILayout.Height(320));

            int shown = 0;
            foreach (CoffeeBeanRegistryEntry entry in _registry.modules)
            {
                // 已安装的模块不显示在这里（在已安装面板管理：更新/卸载）
                if (_installed.Any(p => p.name == entry.id)) continue;
                shown++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(entry.id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("最新: " + (entry.latest ?? "?"), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (GUILayout.Button("安装", GUILayout.Width(60))) InstallFromEntry(entry);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            if (shown == 0 && _registry.modules.Count > 0)
                EditorGUILayout.LabelField("全部模块已安装。", EditorStyles.centeredGreyMiniLabel);
            else if (_registry.modules.Count == 0)
                EditorGUILayout.LabelField("registry 中没有模块。", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ========== 安装 / 更新 / 卸载 ==========

        /// <summary>更新已安装模块（带确认；更新本质 = 用新 tag 重新 Add，UPM 会替换引用）。</summary>
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
            _status = isUpdate ? $"正在更新 {entry.id} → {entry.latest}..." : $"正在安装 {entry.id}...";
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
                EditorUtility.DisplayDialog("无法卸载",
                    $"'{packageId}' 被以下模块依赖：\n- {string.Join("\n- ", dependents)}\n\n请先卸载这些模块。",
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("卸载模块",
                    $"从当前工程移除 '{packageId}'？", "卸载", "取消")) return;

            _busy = true;
            _status = $"正在卸载 {packageId}...";
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
