using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        // 目录来源与远程拉取状态（用于把"这份结论是哪来的"如实告诉用户）
        private string _registrySource = "内置（随 Core 版本）";
        private bool _remoteLoading;
        private bool _remoteRegistryFailed;
        private DateTime _lastRemoteAttemptUtc = DateTime.MinValue;

        // ========== 工具导航 ==========
        private List<CoffeeBeanToolRegistry.ToolEntry> _tools = new List<CoffeeBeanToolRegistry.ToolEntry>();
        private Vector2 _toolsScroll;
        private Vector2 _inlineScroll; // 内嵌工具面板（由模块提供 DrawTool）自己的滚动位置
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
            // 默认就是官方远程目录（见 RegistrySource.DefaultRemoteUrl）—— 内置目录只随 Core 版本更新，
            // 只信内置目录必然漏报"刚发出去的那些版本"。
            _remoteUrl = RegistrySource.ResolveUrl();
            _tools = CoffeeBeanToolRegistry.Scan();
            _selectedTool = BuiltinModuleManager;
            Refresh();
        }

        /// <summary>
        /// 先用内置目录把界面点亮（无网络也能用），紧接着异步拉一次远程目录覆盖它。
        /// </summary>
        private void Refresh()
        {
            _registry = RegistrySource.LoadBuiltIn();
            _registrySource = "内置（随 Core 版本）";
            ReloadInstalled();
            RefreshRegistryFromRemote();
        }

        /// <summary>
        /// 异步拉远程目录。失败**不抛也不静默**：记下状态，由工具栏徽章与「检查更新」如实说明。
        /// 60 秒内不重复拉（域重载/开窗会频繁触发，没必要每次都打网络）。
        /// </summary>
        private void RefreshRegistryFromRemote()
        {
            if (string.IsNullOrEmpty(_remoteUrl) || _remoteLoading) return;
            if ((DateTime.UtcNow - _lastRemoteAttemptUtc).TotalSeconds < 60) return;

            _lastRemoteAttemptUtc = DateTime.UtcNow;
            _remoteLoading = true;
            RegistrySource.LoadRemote(_remoteUrl, data =>
            {
                _remoteLoading = false;
                if (data != null && data.modules.Count > 0)
                {
                    _registry = data;
                    _registrySource = "远程（" + _remoteUrl + "）";
                    _remoteRegistryFailed = false;
                }
                else
                {
                    _remoteRegistryFailed = true;
                    _registrySource = "内置（随 Core 版本）· 远程拉取失败";
                }
                Repaint();
            });
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

        /// <summary>
        /// 检查已安装模块是否有更新：以**远程目录**为准。
        ///
        /// 内置目录的新鲜度 == 已装 Core 的版本，所以只按内置目录判断会得出
        /// "所有模块已是最新"这种**错误结论**（实测踩到：tools 已发 0.10.0，内置目录还写着 0.9.0）。
        /// 远程拉取失败时也**明说**，而不是拿一个可信度未知的结论去回答用户。
        /// </summary>
        private void CheckForUpdates()
        {
            if (string.IsNullOrEmpty(_remoteUrl))
            {
                ReloadInstalled(() => ShowUpdateResult("未配置远程目录地址，只能按内置目录判断（可能漏报更新）。"));
                return;
            }

            _busy = true;
            _status = "检查更新（远程目录）...";
            RegistrySource.LoadRemote(_remoteUrl, data =>
            {
                _busy = false;
                if (data != null && data.modules.Count > 0)
                {
                    _registry = data;
                    _registrySource = "远程（" + _remoteUrl + "）";
                    _remoteRegistryFailed = false;
                    ReloadInstalled(() => ShowUpdateResult(null));
                    return;
                }

                _remoteRegistryFailed = true;
                _registrySource = "内置（随 Core 版本）· 远程拉取失败";
                ReloadInstalled(() => ShowUpdateResult(
                    $"远程目录拉取失败：{_remoteUrl}\n" +
                    "已退回**内置目录**判断 —— 内置目录只随 Core 版本更新，所以下面的结论可能漏报更新。\n" +
                    "请检查网络，或在工具栏右侧改成一个可达的地址（内网镜像）后重试。"));
            });
        }

        /// <param name="caveat">结论可信度说明；null 表示目录来源可靠。</param>
        private void ShowUpdateResult(string caveat)
        {
            var updatable = _installed.Where(p => IsOutdated(p.name, out _)).ToList();

            if (updatable.Count == 0)
            {
                _status = caveat == null ? "所有模块已是最新版本。" : caveat.Replace("\n", " ");
                EditorUtility.DisplayDialog("检查更新",
                    caveat == null
                        ? "所有已安装模块已是最新版本。"
                        : "没有发现可更新的模块。\n\n⚠ " + caveat,
                    "OK");
                Repaint();
                return;
            }

            var lines = updatable.Select(p =>
            {
                _installedTags.TryGetValue(p.name, out string cur);
                IsOutdated(p.name, out string latest);
                return $"- {p.name}: {(string.IsNullOrEmpty(cur) ? "?" : cur)} → {latest}";
            }).ToList();
            _status = $"发现 {updatable.Count} 个可更新模块。";
            bool updateAll = EditorUtility.DisplayDialog("发现更新",
                string.Join("\n", lines) + (caveat == null ? string.Empty : "\n\n⚠ " + caveat) +
                "\n\n是否立即全部更新？", "全部更新", "稍后");
            if (updateAll)
            {
                // 一次 UPM 请求提交整批。原来是在循环里逐个 InstallFromEntry，
                // 每次都启动一条异步 Client.Add 链 —— N 个模块同时发起会互相打架；
                // 后来改成逐个串行又会被域重载断链。现在统一走 AddAndRemove 一次性提交。
                var ids = new List<string>();
                foreach (PackageInfo p in updatable)
                {
                    if (FindRegistryEntry(p.name) != null) ids.Add(p.name);
                }

                if (ids.Count > 0)
                {
                    _busy = true;
                    _status = $"正在更新 {ids.Count} 个模块...";
                    // includePresentTargets: true —— 更新的语义就是"把已装的重新 Add 到 latest"
                    ModuleInstaller.InstallMany(_registry, ids, includePresentTargets: true,
                        onCompleted: (ok, message) =>
                        {
                            _busy = false;
                            _status = message;
                            ReloadInstalled();
                            Repaint();
                        },
                        onProgress: BatchProgress("更新"));
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

        /// <summary>
        /// 是不是 Core 自己。Core **可以更新**（更新后 Core 也登记在 registry 里了），
        /// 但**绝不能被卸载** —— 框架工具中心（本窗口）与模块安装器都住在它里面，
        /// 卸载掉就是"把正在用的螺丝刀一起扔了"，工程还会立刻编译不过。
        /// </summary>
        internal static bool IsCorePackage(string packageId)
            => !string.IsNullOrEmpty(packageId)
               && string.Equals(packageId, ModuleDependencyResolver.CorePackageId, StringComparison.OrdinalIgnoreCase);

        // ========== GUI ==========

        private const string NavGroupManage = "管理";
        private const string NavGroupTools = "工具";

        /// <summary>
        /// 框架当前版本（品牌栏显示用）。**不再硬编码**。
        ///
        /// 这里原来写死成 <c>"0.1.43"</c>，之后 core 一路发到 0.1.55，
        /// 而窗口上一直显示 0.1.43 —— 漂移了 12 个版本，纯属"发布时记得同步"没兜住。
        /// 现在从权威来源取（并缓存，OnGUI 每帧都会读）：
        /// 1) UPM 解析到的**包版本**（package.json，即用户实际装的版本）；
        /// 2) 回退：模块标记 <see cref="CoffeeBeanModuleAttribute"/> 里的版本
        ///    （Core 自己用于 MinCoreVersion 比较的那个）。
        ///
        /// 两者若不一致（手工改版本时漏改一处），品牌栏会直接给出警告徽章 ——
        /// 把漂移暴露出来，而不是继续显示一个谁都信不过的数字。
        /// </summary>
        internal static string FrameworkVersion
            => _frameworkVersion ?? (_frameworkVersion = ResolveFrameworkVersion());

        private static string _frameworkVersion;

        /// <summary>模块标记里声明的 Core 版本（用于和包版本比对，暴露漂移）。</summary>
        internal static string FrameworkMarkerVersion
            => _frameworkMarkerVersion ?? (_frameworkMarkerVersion = ResolveMarkerVersion());

        private static string _frameworkMarkerVersion;

        private static string ResolveFrameworkVersion()
        {
            // 1) UPM 包版本（权威：就是用户实际安装的那个版本）
            try
            {
                PackageInfo info = PackageInfo.FindForAssembly(typeof(CoffeeBeanVersion).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.version)) return info.version;
            }
            catch (Exception)
            {
                // 编辑器尚未完成包解析等情况 → 走回退
            }

            // 2) 模块标记
            string marker = FrameworkMarkerVersion;
            return string.IsNullOrEmpty(marker) ? "?" : marker;
        }

        private static string ResolveMarkerVersion()
        {
            try
            {
                var attr = typeof(CoffeeBeanVersion).Assembly
                    .GetCustomAttribute<CoffeeBeanModuleAttribute>();
                if (attr != null && !string.IsNullOrEmpty(attr.Version)) return attr.Version;
            }
            catch (Exception)
            {
            }
            return string.Empty;
        }

        /// <summary>包版本与模块标记版本是否漂移（手工改版本时容易漏改一处）。</summary>
        internal static bool HasVersionDrift
        {
            get
            {
                string marker = FrameworkMarkerVersion;
                return !string.IsNullOrEmpty(marker) && marker != FrameworkVersion;
            }
        }

        /// <summary>测试/刷新用：清掉版本缓存。</summary>
        internal static void ResetVersionCache()
        {
            _frameworkVersion = null;
            _frameworkMarkerVersion = null;
        }

        private void OnGUI()
        {
            DrawBrandBar();     // 顶部品牌 + 概览徽章
            DrawBuildModeBar(); // 构建模式（Beta / Release）切换
            DrawToolbar();      // 操作栏
            DrawBody();         // 主体：左侧导航 + 右侧内容
            DrawStatusBar();    // 底部状态
        }

        /// <summary>构建模式切换条：显示当前模式 + 一键切 Beta/Release（维护 PlayerSettings 符号）。</summary>
        private void DrawBuildModeBar()
        {
            var target = CBuildModeEditor.ActiveTarget();
            var mode = CBuildModeEditor.CurrentMode(target);
            bool isBeta = mode == CBuildModeEditor.Mode.Beta;

            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("构建模式", EditorStyles.boldLabel);
            GUILayout.Space(4);

            // 当前模式徽章
            var badge = new GUIStyle("HelpBox") { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 2, 2, 2) };
            badge.normal.textColor = isBeta ? new Color(0.15f, 0.55f, 0.25f) : new Color(0.85f, 0.4f, 0.1f);
            GUILayout.Label(isBeta ? "● Beta" : "● Release", badge);
            GUILayout.Space(6);
            GUILayout.Label(CBuildModeEditor.Describe(mode) + "（Editor 下恒有日志）", EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();
            _applyAllGroups = GUILayout.Toggle(_applyAllGroups, "应用到所有平台组", EditorStyles.miniButtonRight, GUILayout.Width(130));

            if (isBeta)
            {
                if (GUILayout.Button("切到 Release", GUILayout.Width(110))) ApplyMode(CBuildModeEditor.Mode.Release);
            }
            else
            {
                if (GUILayout.Button("切到 Beta", GUILayout.Width(110))) ApplyMode(CBuildModeEditor.Mode.Beta);
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool _applyAllGroups;

        private void ApplyMode(CBuildModeEditor.Mode mode)
        {
            int n;
            if (_applyAllGroups) n = CBuildModeEditor.ApplyModeAll(mode);
            else
            {
                CBuildModeEditor.ApplyModeCurrent(mode);
                n = 1;
            }
            _status = $"{CBuildModeEditor.Describe(mode)}（已应用 {n} 组，脚本将重新编译）";
            EditorApplication.delayCall += AssetDatabase.Refresh; // 符号变更已自动触发重编译，此处兜底刷新
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
            // 包版本与模块标记版本漂移时直接暴露出来（手工改版本容易漏改一处）
            if (HasVersionDrift)
            {
                GUILayout.Space(6);
                DrawBadgeWarn($"版本漂移：模块标记 v{FrameworkMarkerVersion}");
            }
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
            GUI.enabled = !_busy && !_batchRunning;
            if (GUILayout.Button("检查更新", EditorStyles.toolbarButton)) CheckForUpdates();
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
            {
                CoffeeBeanToolRegistry.RefreshCache();
                _tools = CoffeeBeanToolRegistry.Scan();
                Refresh();
                _status = "已刷新。";
            }
            if (GUILayout.Button("加载远程 registry", EditorStyles.toolbarButton)) LoadRemoteRegistry();
            GUILayout.Space(12);
            if (GUILayout.Button("一键安装所有依赖", EditorStyles.toolbarButton)) RunBatchInstall();
            if (GUILayout.Button("一键卸载所有依赖", EditorStyles.toolbarButton)) RunBatchUninstall();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            // 目录来源必须可见：内置目录只随 Core 版本更新，用户需要知道"我现在看到的清单新不新"
            GUILayout.Label(_remoteLoading ? "目录：拉取中…" : "目录：" + _registrySource,
                EditorStyles.miniLabel);
            if (_remoteRegistryFailed)
            {
                if (GUILayout.Button("重试拉取", EditorStyles.toolbarButton)) RetryRemoteRegistry();
            }
            _remoteUrl = EditorGUILayout.TextField(_remoteUrl, GUILayout.MinWidth(220));
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>手动重试远程目录（跳过 60 秒节流）。</summary>
        private void RetryRemoteRegistry()
        {
            _lastRemoteAttemptUtc = DateTime.MinValue;
            RefreshRegistryFromRemote();
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

        /// <summary>右侧：内容区（模块管理 / 内嵌面板 / 工具卡片）。</summary>
        private void DrawContentPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            if (_selectedTool == BuiltinModuleManager)
            {
                DrawModuleManager();
            }
            else
            {
                CoffeeBeanToolRegistry.ToolEntry tool = _tools.FirstOrDefault(t => t.Title == _selectedTool);
                if (tool != null && tool.IsInline) DrawInlineTool(tool);
                else DrawToolCard();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 内嵌工具面板：直接画在内容区里（不另开窗口），内容由模块自己的
        /// <c>DrawTool</c> 提供。Hub 只是宿主，所以面板出错也只在这里兜住，不牵连整个窗口。
        /// </summary>
        private void DrawInlineTool(CoffeeBeanToolRegistry.ToolEntry tool)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("🧩 " + tool.Title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });
            if (!string.IsNullOrEmpty(tool.Description))
            {
                EditorGUILayout.LabelField(tool.Description, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.Space(6);

            _inlineScroll = EditorGUILayout.BeginScrollView(_inlineScroll, GUILayout.ExpandHeight(true));
            try
            {
                tool.DrawInline(Repaint);
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox(
                    $"面板绘制失败：{e.Message}\n（{tool.Title} · {tool.Module}）\n详见 Console。", MessageType.Error);
                Debug.LogError($"[CoffeeBean] 内嵌工具面板「{tool.Title}」绘制异常：{e}");
            }
            EditorGUILayout.EndScrollView();
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
                if (IsCorePackage(pkg.name))
                {
                    // Core 不给卸载按钮：窗口与安装器都在它里面（这里原来是有按钮的，
                    // 只要工程里没有别的模块依赖 core，FindDependents 返回空 → 真能卸掉）
                    GUILayout.Label("核心模块", EditorStyles.miniLabel, GUILayout.Width(60));
                }
                else if (GUILayout.Button("卸载", GUILayout.Width(60)))
                {
                    ConfirmUninstallByName(pkg.name);
                }
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
            ModuleInstallPlan plan = ResolveInstallPlan(entry);
            string depText = plan.HasErrors ? $"\n\n⚠ {plan.Error}" : DescribeDependencies(plan);
            if (!EditorUtility.DisplayDialog("更新模块",
                    $"更新 {entry.id}\n  当前: {(string.IsNullOrEmpty(current) ? "?" : current)}\n  最新: {entry.latest}{depText}\n\n确定更新？",
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
            ModuleInstallPlan plan = ResolveInstallPlan(entry);
            if (plan.HasErrors)
            {
                _status = plan.Error;
                EditorUtility.DisplayDialog("无法安装", plan.Error, "OK");
                Repaint();
                return;
            }

            // 需要顺带补装依赖时先让用户知情（CoffeeBean 模块是 git 包，UPM 自己解析不了它们的版本号）
            if (!confirmed && plan.RequiredDependencies.Count > 0)
            {
                if (!EditorUtility.DisplayDialog("安装模块",
                        $"安装 {entry.id}@{entry.latest}{DescribeDependencies(plan)}\n\n继续？",
                        "安装", "取消")) return;
            }

            _busy = true;
            _status = isUpdate ? $"正在更新 {entry.id} → {entry.latest}..." : $"正在安装 {entry.id}...";
            ModuleInstaller.InstallPlan(plan, entry.id, (ok, message) =>
            {
                _busy = false;
                _status = message;
                ReloadInstalled();
                Repaint();
            });
        }

        /// <summary>算出安装计划（含需要自动补装的依赖）。</summary>
        private ModuleInstallPlan ResolveInstallPlan(CoffeeBeanRegistryEntry entry)
            => ModuleDependencyResolver.Resolve(_registry, entry.id, ModuleInstaller.GetRegisteredPackageIds());

        /// <summary>把计划里要补装的依赖整理成可读文本（无依赖时为空串）。</summary>
        private static string DescribeDependencies(ModuleInstallPlan plan)
        {
            List<PlannedPackage> deps = plan.RequiredDependencies;
            if (deps.Count == 0)
            {
                return plan.Warnings.Count == 0 ? string.Empty : "\n\n注意：\n- " + string.Join("\n- ", plan.Warnings);
            }

            var lines = new List<string>();
            foreach (PlannedPackage p in deps) lines.Add("  · " + p);
            string text = $"\n\n需先自动补装 {deps.Count} 个依赖：\n{string.Join("\n", lines)}";
            if (plan.Warnings.Count > 0) text += "\n\n注意：\n- " + string.Join("\n- ", plan.Warnings);
            return text;
        }

        // ========== 一键批量（菜单 / 工具栏共用，不依赖窗口是否打开） ==========

        private const string BatchTitle = "CoffeeBean 模块";

        /// <summary>批量执行中（避免连点/重复触发两批并发跑）。</summary>
        private static bool _batchRunning;

        [MenuItem("Tools/CoffeeBean/一键安装所有依赖", false, 200)]
        public static void InstallAllModulesFromMenu() => RunBatchInstall();

        [MenuItem("Tools/CoffeeBean/一键卸载所有依赖", false, 201)]
        public static void UninstallAllModulesFromMenu() => RunBatchUninstall();

        /// <summary>
        /// 取模块目录：默认走**远程**（<see cref="RegistrySource.ResolveUrl"/>，官方 main），
        /// 失败或未配置则退回内置。菜单入口（一键安装/卸载）与窗口必须用同一份目录，
        /// 否则会出现"窗口说有更新、菜单却按旧目录装"这种自相矛盾。
        /// </summary>
        private static void WithRegistry(Action<CoffeeBeanRegistryData> action)
        {
            string url = RegistrySource.ResolveUrl();
            if (string.IsNullOrEmpty(url))
            {
                action(RegistrySource.LoadBuiltIn());
                return;
            }

            RegistrySource.LoadRemote(url, data =>
                action(data != null && data.modules.Count > 0 ? data : RegistrySource.LoadBuiltIn()));
        }

        private static bool BeginBatch(string status)
        {
            if (_batchRunning)
            {
                EditorUtility.DisplayDialog(BatchTitle, "已有一批模块操作正在执行，请等它结束。", "OK");
                return false;
            }
            _batchRunning = true;
            SetWindowStatus(status);
            return true;
        }

        private static void EndBatch(string message, bool refreshInstalled)
        {
            _batchRunning = false;
            EditorUtility.ClearProgressBar();
            SetWindowStatus(message);
            if (refreshInstalled) RefreshOpenWindows();
        }

        /// <summary>
        /// 域重载后清掉可能残留的忙碌标记与进度条。
        ///
        /// 批量操作会跨域重载（装/卸包必然触发重编译），而挂在旧域上的完成回调不一定还会被调用；
        /// 一旦没人清，"进行中"的状态就永远留在界面上 —— 表现就是 Unity 卡死。
        /// 这里做一次兜底（现在批量走单次 UPM 请求、且不再用模态进度条，但保险留着）。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ClearStaleBatchState()
        {
            _batchRunning = false;
            EditorUtility.ClearProgressBar();
        }

        private static void SetWindowStatus(string status)
        {
            foreach (ModuleManagerWindow window in Resources.FindObjectsOfTypeAll<ModuleManagerWindow>())
            {
                window._status = status;
                window._busy = _batchRunning;
                window.Repaint();
            }
        }

        private static void RefreshOpenWindows()
        {
            foreach (ModuleManagerWindow window in Resources.FindObjectsOfTypeAll<ModuleManagerWindow>())
            {
                window.ReloadInstalled();
            }
        }

        /// <summary>
        /// 进度汇报：只更新窗口状态文本，**不用模态进度条**。
        ///
        /// 批量现在是一次 UPM 请求，本来就没有细粒度进度；而模态进度条一旦因为域重载
        /// 拿不到完成回调就会永远留在屏幕上（看起来就是 Unity 卡死）。所以这里只写状态栏。
        /// </summary>
        private static Action<int, int, string> BatchProgress(string verb)
            => (done, total, id) => SetWindowStatus(total <= 0 ? $"{verb}中..." : $"{verb} {total} 个模块（一次 UPM 请求）...");

        /// <summary>
        /// 一键安装：把 registry 里**还没装**的模块装上（含各自缺失依赖，依赖优先）。
        ///
        /// 只装缺的：已安装的不进计划。否则会把整个目录原样重装一遍 —— 既慢又毫无意义，
        /// 而且是之前"Unity 卡死"的直接原因（N 个包 = N 次依赖图求解 + N 次域重载）。
        /// 想升级已装的模块，用「检查更新 → 全部更新」。
        /// </summary>
        public static void RunBatchInstall()
        {
            if (!BeginBatch("正在解析模块目录...")) return;

            WithRegistry(registry =>
            {
                var targets = new List<string>();
                foreach (CoffeeBeanRegistryEntry entry in registry.modules)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.id)) targets.Add(entry.id);
                }

                if (targets.Count == 0)
                {
                    EndBatch("registry 里没有模块。", false);
                    EditorUtility.DisplayDialog(BatchTitle, "registry 里没有可安装的模块。", "OK");
                    return;
                }

                // includePresentTargets: false —— 已安装的目标不进计划
                ModuleInstallPlan plan = ModuleDependencyResolver.ResolveMany(
                    registry, targets, ModuleInstaller.GetRegisteredPackageIds(), includePresentTargets: false);

                if (plan.HasErrors)
                {
                    EndBatch(plan.Error, false);
                    EditorUtility.DisplayDialog(BatchTitle, plan.Error, "OK");
                    return;
                }

                if (plan.Packages.Count == 0)
                {
                    EndBatch("全部模块都已安装，无需操作。", false);
                    EditorUtility.DisplayDialog(BatchTitle,
                        $"registry 里的 {targets.Count} 个模块都已经装好了，没有需要补装的依赖。", "OK");
                    return;
                }

                var lines = new List<string>();
                foreach (PlannedPackage package in plan.Packages)
                {
                    lines.Add("- " + package.ToString() + (package.IsTarget ? string.Empty : "   [依赖]"));
                }

                if (!EditorUtility.DisplayDialog(BatchTitle,
                        $"registry 共 {targets.Count} 个模块，其中 {plan.Packages.Count} 个需要安装" +
                        "（已安装的会跳过；依赖优先补装）：\n\n" +
                        string.Join("\n", lines) +
                        "\n\n继续？", "全部安装", "取消"))
                {
                    EndBatch("已取消。", false);
                    return;
                }

                ModuleInstaller.InstallPlan(plan, null, (ok, message) =>
                {
                    EndBatch(message, true);
                    EditorUtility.DisplayDialog(ok ? "安装完成" : "安装失败", message, "OK");
                });
            });
        }

        /// <summary>
        /// 一键卸载：移除工程里全部 CoffeeBean 模块（Core 除外 —— Module Manager 就住在 Core 里）。
        /// 顺序由 <see cref="ModuleDependencyResolver.ResolveUninstallOrder"/> 决定（依赖方先卸）。
        /// </summary>
        public static void RunBatchUninstall()
        {
            if (!BeginBatch("正在读取工程模块...")) return;

            WithRegistry(registry =>
            {
                List<string> declared = ModuleInstaller.GetManifestCoffeeBeanModules();
                var targets = new List<string>();
                foreach (string id in declared)
                {
                    // Core 不能卸：本窗口与安装器都在它里面
                    if (string.Equals(id, ModuleDependencyResolver.CorePackageId, StringComparison.OrdinalIgnoreCase)) continue;
                    targets.Add(id);
                }

                if (targets.Count == 0)
                {
                    EndBatch("工程里没有可直接卸载的 CoffeeBean 模块。", false);
                    EditorUtility.DisplayDialog(BatchTitle, "工程里没有可直接卸载的 CoffeeBean 模块。", "OK");
                    return;
                }

                // 卸载顺序：依赖方先卸，被依赖者后卸（UPM 不允许卸掉仍被依赖的包）
                List<string> ordered = ModuleDependencyResolver.ResolveUninstallOrder(registry, targets);

                if (!EditorUtility.DisplayDialog(BatchTitle,
                        $"将从工程移除以下 {ordered.Count} 个模块（Core 除外），按依赖反序执行：\n\n" +
                        string.Join("\n", ordered.ConvertAll(id => "- " + id)) +
                        DescribeThirdPartyLeftovers(registry, ordered) +
                        "\n\n引用这些模块的代码会立刻编译不过，确定继续？", "全部卸载", "取消"))
                {
                    EndBatch("已取消。", false);
                    return;
                }

                ModuleInstaller.UninstallMany(registry, ordered,
                    (ok, message) =>
                    {
                        EndBatch(message, true);
                        EditorUtility.DisplayDialog(ok ? "卸载完成" : "卸载失败", message, "OK");
                    },
                    BatchProgress("卸载"));
            });
        }

        /// <summary>列出被移除模块所需的第三方依赖（框架不会替你卸它们，避免误删还被别处用着的东西）。</summary>
        private static string DescribeThirdPartyLeftovers(CoffeeBeanRegistryData registry, List<string> removedIds)
        {
            var leftovers = new List<string>();
            var declared = new HashSet<string>(
                ModuleInstaller.GetRegisteredPackageIds(), StringComparer.OrdinalIgnoreCase);

            foreach (CoffeeBeanRegistryEntry entry in registry.modules)
            {
                if (entry == null || !removedIds.Contains(entry.id, StringComparer.OrdinalIgnoreCase)) continue;
                if (entry.externalDependencies == null) continue;
                foreach (CoffeeBeanExternalDependency external in entry.externalDependencies)
                {
                    if (external == null || string.IsNullOrEmpty(external.id)) continue;
                    if (!declared.Contains(external.id)) continue;
                    if (!leftovers.Contains(external.id)) leftovers.Add(external.id);
                }
            }

            if (leftovers.Count == 0) return string.Empty;
            return "\n\n这些第三方依赖不会被动到（可能还被别处用着，请自行判断是否手工移除）：\n" +
                   string.Join("\n", leftovers.ConvertAll(id => "- " + id));
        }

        private void ConfirmUninstallByName(string packageId)
        {
            // 兜底：按钮已经不给 Core 了，但批量入口 / 以后新增的入口也得挡住
            if (IsCorePackage(packageId))
            {
                EditorUtility.DisplayDialog("无法卸载",
                    "Core 不能卸载：框架工具中心（本窗口）与模块安装器都住在它里面。\n\n" +
                    "要换版本请用「更新」，或直接改 Packages/manifest.json 里的引用。", "OK");
                return;
            }

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
