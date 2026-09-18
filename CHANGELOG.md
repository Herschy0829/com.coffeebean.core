# Changelog

## [0.1.62] - 2026-09-18

### Changed
- **Hub 导航支持双击直接打开窗口工具**。原来点导航项只是"选中"，右侧出现一张卡片，
  还得再点卡片上的「打开」才弹窗口 —— 两步，而且第一步在用户看来像"点了没反应"
  （用户实测反馈："Asset 模块的菜单不够直观，点击按钮也没反应"）。
  现在：**单击选中看说明，双击直接打开**；工具组标题下会写明「单击选中 · 双击直接打开」，
  卡片上的提示也同步说明。内嵌面板类工具本来就在右边直接画出来，不需要双击。

  双击检测没有读 `Event.clickCount`（`GUILayout.Button` 在 MouseUp 触发，那一刻的
  clickCount 不可靠），而是自己记"同一工具 0.4 秒内被点了两次"。

- **内置 registry 指向 asset v0.5.0**（该版本修了"资源依赖分析窗口卡死""设置面板按钮静默空转"
  两个"点了没反应"的问题，并把 Addressables 设置改成 Hub 内嵌面板）。指针：core → v0.1.62、asset → v0.5.0。

### Notes
- 本次无运行时代码改动，都是 Hub 的编辑器交互体验。

## [0.1.61] - 2026-09-17

### Changed
- **tools 与 asset 的第三方硬依赖登记进 registry**（纯数据，无运行时代码改动）：

  | 模块 | 新增 `externalDependencies` | 为什么 |
  |---|---|---|
  | `com.coffeebean.tools` | `com.cysharp.unitask`、`com.neuecc.unirx` | tools 把它们变成了**强制依赖**（package.json 里的 dependencies） |
  | `com.coffeebean.asset` | `com.cysharp.unitask` | 资源管理改用 UniTask |

  这两个第三方包**不在任何 registry 里**，UPM 无法按版本号解析 —— 不登记的话，
  "在新工程里一键安装"会直接失败。登记后 `ModuleDependencyResolver` 会把它们排在同一批的
  最前面（第三方 → CoffeeBean 依赖 → 目标模块），一次 `Client.AddAndRemove` 提交，
  UPM 解一次依赖图就能同时满足。

  registry 指针：tools → v0.12.0、asset → v0.4.0、core → v0.1.61。

### Tests
- 新增 4 条：
  - `BuiltInRegistry_ToolsDeclaresItsForcedThirdPartyDependencies`（tools 必须登记 uniRx + unitask，
    且给带 `?path=` 的完整 UPM 地址）；
  - `BuiltInRegistry_AssetDeclaresUniTask`；
  - `Resolve_Tools_PlansThirdPartyBeforeTools`（**行为锁**：空工程里装 tools，计划里第三方必须排在
    tools 之前、tools 最后 —— 顺序错了 UPM 就解不开硬依赖）；
  - `BuiltInRegistry_ThirdPartyUrlsMatchTheToolsCatalog`：按全名反射拿 tools 的
    `CThirdPartyCatalog`，逐条比对 registry 里的 UPM 地址 —— 两处各自维护，
    漂移就会出现"同一个包两个来源"。
- Core 测试 88 → 92，全量 EditMode **690 → 699**（asset 模块另 +4）。

### Fixed（工具链）
- `scripts/run-editmode-tests.ps1` 读结果 XML 改用**显式 UTF-8 解码**。
  Unity 的 NUnit 结果文件**没有 BOM**，而 Windows PowerShell 5.1 的 `Get-Content` 默认按 ANSI 解码 ——
  一旦 XML 里出现中文（`<message>` / `<output>` 的 CDATA），就会被解成乱码甚至破坏 XML 结构，
  表现为"测试全过了，脚本却报 start tag 'message' does not match end tag 'output'"。
  这条以前是潜伏的（结果文件带 BOM 或没有中文 CDATA 就看不出来），这次加了带中文的跳过原因才暴露。

## [0.1.60] - 2026-09-17

### Added
- **Hub 工具支持"内嵌面板"**：以前模块的工具只能是"另开一个窗口"，于是"就几个开关/一行状态"
  这种小工具也得占一个窗口（点导航 → 弹出新窗口 → 看完关掉）。现在多了一类：

  | 工具形态 | 模块侧写法 | Hub 行为 |
  |---|---|---|
  | 独立窗口工具（原有） | 非抽象 `EditorWindow` 派生类 + `[CoffeeBeanTool]` | 点导航开一个窗口 |
  | **内嵌面板（新增）** | `static class` + `[CoffeeBeanTool]` + `public static void DrawTool(Action requestRepaint)` | **直接画在 Hub 内容区** |

  判定纯靠**结构**，没有给 attribute 加字段：`[CoffeeBeanTool]` 在各模块里是各自维护的副本，
  加字段就得让每个模块跟着改一遍，而按"static 类 + DrawTool 签名"找则旧的副本一行都不用动
  （老模块的窗口工具照常工作，两者判据互斥）。

  `DrawTool` 的 `Action requestRepaint` 是宿主窗口传进去的 —— 面板是静态类、拿不到窗口，
  异步操作（如 UPM 装包）结束后靠它刷新自己。也接受无参的 `DrawTool()`。
  面板抛异常只会在这个区块里显示错误 + 打 Console，不会把整个 Hub 打挂。

- `CoffeeBeanToolRegistry.ToolEntry` 新增 `InlineDraw` / `IsInline` / `DrawInline(Action)`；
  `CoffeeBeanToolRegistry.FindInlineDraw(Type)` 公开（带测试）。

### Changed
- **内置 registry 指向 tools v0.11.0**（第三方依赖面板进 Hub）。

### Tests
- `CoffeeBeanToolRegistryTests` 4 → 9 条：两类工具的判据必须**互斥且恰好一类**
  （`IsInline ^ WindowType != null`）、内嵌面板必须是 static 类且签名正确、
  内嵌面板的 `Open()` 是安全空操作、`FindInlineDraw` 对签名不符/null 返回 null，
  以及**「第三方依赖」面板必须被 Hub 发现为内嵌**（改错方法名/漏加 attribute 会红）。
- Core 测试 83 → 88，全量 EditMode **690/690 全绿**。

## [0.1.59] - 2026-09-17

### Changed
- **内置 registry 指向 tools v0.10.1**（修正第三方依赖菜单的勾选状态语义）。
  纯指针同步，无运行时代码改动。

  值得一提：这次**不需要**为了拿到更新而再发一次 Core —— 0.1.58 起
  `RegistrySource.DefaultRemoteUrl` 已经是默认目录来源，目录随发布走，
  所以 tools 的后续版本一推上去，装了 0.1.58+ 的工程「检查更新」就能看见。
  本次改这一行只是为了让**内置**目录（离线 / 未联网时那一份）也不落后太多。

## [0.1.58] - 2026-09-17

### Fixed
- **「检查更新」谎报"所有模块已是最新版本"** —— 用户实测踩到：tools 已经发到 v0.10.0，
  窗口却说全部最新。

  根因是**内置目录只随 Core 版本更新**：`registry.json` 编译进 Core 包，
  Core 0.1.56 的内置目录里 `com.coffeebean.tools` 还写着 `v0.9.0`；
  而 `检查更新` 只在"用户手工填过远程地址"（`EditorPrefs: CoffeeBean.RegistryUrl`，默认空）时才拉远程，
  否则就拿这份陈旧目录去比对 —— 于是必然"已是最新"。

  于是形成了死锁：**想看到工具模块的更新得先更新 Core，想更新 Core 又得先看到更新。**

  三处修改：
  1. `RegistrySource.DefaultRemoteUrl`：官方 `main` 分支上那份目录成为**默认**来源，
     `ResolveUrl()` = EditorPrefs 有值用它、否则用默认。**目录随发布走，不再随 Core 版本走**；
     内网/镜像仍可覆盖（含"锁定某个分支"这种用法）。
  2. 开窗/刷新时**异步拉一次**远程目录覆盖内置目录（60 秒节流，失败不影响离线可用），
     工具栏上常驻显示 `目录：内置（随 Core 版本）/ 远程（URL）` —— 用户能一眼看出这份清单新不新。
  3. **远程拉取失败时不再冒充结论**：对话框明确写出"已退回内置目录，结论可能漏报更新"，
     并给出网络/镜像提示；工具栏同时出现「重试拉取」。

- **Core 自己永远无法更新**（只能手改 `manifest.json`）。`registry.json` 里从来没有 Core 的条目，
  所以「检查更新 / 全部更新」永远看不到它 —— 上面那个死锁的另一半。
  现在 Core 作为普通条目登记（`repo` + `latest`），可以像别的模块一样被更新。

- **Core 可以从窗口里被卸载**（顺带发现的、同族的隐患）。已安装列表里每一行都有「卸载」按钮，
  而 `ConfirmUninstallByName` 只用 `FindDependents`（谁依赖我）做保护 ——
  工程里若只有 Core + tools（tools 不依赖 Core），依赖者列表为空，**真能卸掉**，
  后果是框架工具中心与模块安装器一起消失、工程立刻编译不过。
  现在：Core 行不给卸载按钮（显示"核心模块"），并且 `ConfirmUninstallByName` 里再加一道兜底拦截。

### Added
- `RegistrySource.DefaultRemoteUrl` / `RegistrySource.ResolveUrl()`（新的目录来源约定）。
- `ModuleManagerWindow.IsCorePackage(string)`（internal，带测试）。

### Tests
- 新增 `BuiltInRegistry_RegistersCoreItselfForUpdate`：Core 必须登记在目录里、
  `latest` 必须是 **`v` + 当前框架版本**（与 package.json、模块标记三方一致）——
  以后发 Core 忘了改这一行会直接红。
- 新增 `BuiltInRegistry_CoreEntryHasNoDependencies`：Core 条目不能声明依赖，
  否则会被卷进别人的安装计划。
- 新增 `IsCorePackage_MatchesOnlyCore`：大小写不敏感、null/空/前缀相似（`com.coffeebean.corex`）都必须是 false。
- Core 测试 78 → 83，全量 EditMode **677 → 682 全绿**。

## [0.1.57] - 2026-09-17

### Changed
- **内置 registry 指向 tools v0.10.0**（新增第三方依赖一键集成：UniRx / UniTask）。
  只有这一处变化 —— 目录里的版本号是"框架告诉用户去哪儿拿最新模块"的唯一来源，
  不跟着发版走的话，装了这个 core 的工程在 `Window > CoffeeBean` 里看不到 tools 的更新。

  拿到新目录有两条路，都不需要先升级 core：
  1. 窗口里点 **加载远程 registry**（`EditorPrefs: CoffeeBean.RegistryUrl`，可指向 raw.githubusercontent）；
  2. 直接一键更新 core 自己。

### Notes
- 本次没有任何运行时代码改动，纯粹是随 tools v0.10.0 发布同步目录。

## [0.1.56] - 2026-09-17

### Fixed
- **Module Manager 品牌栏显示的框架版本与实际版本不一致（漂移 12 个版本）**。
  窗口里写死了 `private const string FrameworkVersion = "0.1.43";`，
  而 core 从那之后一路发到 0.1.55 —— 界面上却一直显示 `框架工具中心 v0.1.43`，
  只能靠"发版时记得改这里"兜着，显然没兜住。

  现在**不再硬编码**，改为从权威来源取（并缓存，`OnGUI` 每帧都会读）：
  1. `PackageInfo.FindForAssembly(...)` 拿到的**包版本**（package.json，即用户实际装的版本）；
  2. 回退：模块标记 `[CoffeeBeanModule]` 里的版本（Core 自己用于 `MinCoreVersion` 比较的那个）——
     覆盖 core 被 embed 到 `Assets/` 这类 UPM 查不到的情况。

  顺带加了**漂移可视化**：包版本与模块标记版本不一致时，品牌栏直接亮出
  「版本漂移：模块标记 vX.Y.Z」警告徽章 —— 把问题暴露出来，而不是继续显示一个谁都信不过的数字。

### Tests
- 新增 `FrameworkVersion_MatchesPackageJsonAndModuleMarker`：用**三个独立来源**交叉验证
  （package.json 文件内容、`[CoffeeBeanModule]` 标记、窗口实际显示值），
  任何一处漏改都会红。另加 `FrameworkVersion_IsNotEmpty` 防退化。
  这条测试就是为了让上面那类漂移不可能再发生。
- core **78/78**、全量 EditMode **622/622 通过**。

### Audit（全框架版本一致性盘点）
顺手把所有模块的版本声明都查了一遍，结论：**只有上面这一处不一致**。
- 16 个模块的 `package.json` 版本与 `[CoffeeBeanModule]` 标记版本**两两一致**；
- 除该常量外，框架里没有第二处硬编码版本号。
- registry 里 `latest` 与各仓库远端 tag 也一致（之前已用 `git ls-remote` 核对过）。

## [0.1.55] - 2026-09-17

### Fixed
- **「一键安装所有依赖」会把整个目录重装一遍**：`Resolve` 里"目标模块永远进计划"是为了让
  「首次安装」与「更新到指定 tag」共用一条路径，但批量场景下这个语义是错的 ——
  16 个模块全部被当成目标，已安装的也会被重新 Add。
  新增 `includeTargetIfPresent` / `includePresentTargets` 开关：
  **一键安装所有依赖传 `false`（只装缺的，已装的跳过）**；
  批量更新仍传 `true`（更新语义就是"把已装的重新 Add 到 latest"）。
  确认框现在也会明确写出"共 N 个模块，其中 M 个需要安装"。

- **批量操作会把 Unity 卡死**（两个原因叠加）：
  1. **逐个 `Client.Add` 太慢**：每次调用都会让 UPM **重新求解一遍依赖图**
     （Unity 文档原话：`AddAndRemove` "only has to solve the dependency list once,
     instead of constructing a new dependency graph after each call"），
     N 个包 = N 次求解 + N 次脚本导入/域重载；
  2. **更致命的是会断链**：完成通知挂在 `EditorApplication.update` 上，而**域重载会丢掉
     旧域里注册的回调** —— 链一断完成回调永不触发，界面上"进行中"的状态就再也清不掉，
     表现就是 Unity 卡死（模态进度条尤其明显）。

  现在**整批用一次 `Client.AddAndRemove(packagesToAdd, packagesToRemove)` 提交**：
  一个请求、一次解析，两个问题一起消失。`Install` / `InstallPlan` /
  `InstallMany` / `Uninstall` / `UninstallMany` 全部改走这条路径。

- **不再使用模态进度条**：批量改用状态栏文本汇报。并在窗口加了
  `[InitializeOnLoadMethod]` 兜底，域重载后主动清掉可能残留的忙碌标记与进度条 ——
  万一哪天又出现"没人清"的路径，也不会留下卡死观感。

### Added
- `ModuleDependencyResolver.Resolve(..., bool includeTargetIfPresent = true)`
- `ModuleDependencyResolver.ResolveMany(..., bool includePresentTargets = true)`
- `ModuleInstaller.InstallMany(..., bool includePresentTargets = false, ...)`

### Tests
- 新增 6 个用例：已装目标跳过 / 未装目标仍进计划 / **目标已装但依赖缺失时只补依赖** /
  多目标只装缺的 / 全部已装得到空计划（界面据此提示"无需操作"）/
  `includePresentTargets: true` 仍然等价于更新语义。
- core **76/76**、全量 EditMode **620/620 通过**。

## [0.1.54] - 2026-09-17

### Added
- **一键安装所有依赖 / 一键卸载所有依赖**（菜单 `Tools > CoffeeBean`，同时是 Module Manager 工具栏按钮）：
  - **一键安装所有依赖**：把 registry 里登记的全部模块装上，各自依赖自动优先补装（已装的顺带更新到 latest）。
  - **一键卸载所有依赖**：移除工程里全部 `com.coffeebean.*` 模块（**Core 除外** —— Module Manager 就住在它里面），
    按 **"依赖方先卸"** 排序执行。
  - 两者都在执行前列出**具体清单**并二次确认；不依赖窗口是否打开（菜单直接可用）；
    带进度条；完成后弹结果对话框。

- `ModuleDependencyResolver.ResolveMany(registry, targetIds, present)`：为**多个**目标解析合并后的安装计划。
  做法是按顺序拼接各目标的计划、再按包名去重（保留首次出现）——
  这样拼出来的顺序仍然是"依赖在前"：若 t 依赖 d，则任何包含 t 的计划必然在同一计划里更靠前地包含 d，
  因此 d 的首次出现一定不晚于 t。目标即使先被当作别人的依赖进过计划，`IsTarget` 也会补正为 true。

- `ModuleDependencyResolver.ResolveUninstallOrder(registry, ids)`：按"依赖方先卸"给出卸载顺序。
  **为什么必须**：UPM 不允许移除一个仍被其它包依赖的包（`Client.Remove` 直接失败），
  顺序错了整批就卡在第一个有依赖方的包上。registry 里查不到的 id 排最前（对它们一无所知，
  免得它们反过来依赖已知包把顺序卡住）；成环时也不会死循环。

- `ModuleInstaller.InstallMany(...)` / `UninstallMany(...)`：**串行**批量执行，带 `onProgress` 回调。
- `ModuleInstaller.GetManifestCoffeeBeanModules()`：只读 manifest 顶层 dependencies 里的
  `com.coffeebean.*`。卸载必须用这个而不是"已解析的全部包"——
  `Client.Remove` 只能移除 manifest 里**显式声明**的包，对间接依赖会失败。

### Fixed
- **「检查更新 → 全部更新」在多于一个模块时会互相打架**：原实现是在 `foreach` 里逐个调用
  `InstallFromEntry`，而每一次都会启动一条**异步**的 `Client.Add` 链 —— N 个模块同时发起，
  UPM 请求互相覆盖/丢结果，「全部更新」基本必然失败。现在改走 `InstallMany` 串行执行。

### 说明（设计取舍）
- **卸载不会动第三方依赖**（如 `com.cysharp.memorypack`）：它们可能还被别处用着，
  框架不替你判断。但确认对话框会把"被移除模块所需、当前工程里还在"的第三方依赖列出来，
  由你决定是否手工移除。
- Core 自身不参与卸载：它不在 registry 里，且 Module Manager / 安装器都编译在它内部。

### Tests
- 新增 16 个用例：多目标计划合并 / 共享依赖去重 / 目标标记补正 / 未知目标整体失败 /
  空与 null 目标、**"计划顺序永不违反依赖"的强不变式**、
  卸载顺序（依赖方在前）/ 只返回被请求的 id / 未知 id 排最前 / 每个 id 只出现一次 /
  成环不死循环 / **对内置 registry 全量校验卸载顺序自洽**。
- core **70/70**、全量 EditMode **614/614 通过**。

## [0.1.53] - 2026-09-17

### Changed
- 模块目录 `com.coffeebean.tools` latest → **v0.9.0**（**纯 registry 同步，无代码变更**）：
  tools 新增**通用 Loading 框** —— `CLoading`（门面）+ `UILoading`（视图）+ 预制体/材质/着色器，
  预制体随包发布在 `Runtime/Resources/CoffeeBean/LoadingCanvas.prefab`，零配置即可用。

  ```csharp
  CLoading.Show();
  CLoading.Hide();
  using (CLoading.Scope("加载中...")) { await LoadSomething(); }  // 异常路径也自动收起
  ```

  特点：**引用计数**（嵌套/并发 Show 累加，归零才隐藏）、**线程安全**（非主线程经
  `MainThreadDispatcher` 投递）、预制体来源可替换（显式 Prefab → Provider → Resources）。

  同时修掉初版的一批缺陷：自动隐藏的游离定时器会误关后来的 Show、没有 `DontDestroyOnLoad`
  导致切场景丢遮罩、`SetAsFirstSibling()` 把遮罩放到最底层、`Resources.Load` 路径与实际不符、
  `async void` 吞异常、`uiLoading` 死字段。

  ⚠️ tools 现在声明依赖 **`com.unity.ugui`**（loading 用到 UGUI）。

## [0.1.52] - 2026-09-17

### Changed
- 模块目录同步（**纯 registry 同步，无代码变更**）：
  - `com.coffeebean.tools` latest → **v0.8.0**
  - `com.coffeebean.build` latest → **v0.3.0**

  两个版本合起来解决一件事：**应用内评价所需的 Android Gradle 依赖不再要手工改 gradle**。
  应用内评价需要 `com.google.android.play:review`，而 Unity 包没法直接改消费工程的
  `build.gradle` —— 以前只能靠文档提醒开发者手改，漏了就表现成「接口在真机上静默失效」。

  现在拆成「谁需要」与「谁来写」：
  - tools 登记需求（`CAndroidGradleRequirements`），判定依据有三条：
    运行期调用痕迹（持久化，跨域重载有效）、工程源码扫描（给 CI / 新克隆机器兜底）、显式登记；
  - **装了 build** → 由 build 的 `CAndroidRequiredDeps` 写（首选，有导出日志）；
  - **没装 build** → 由 tools 的 `CAndroidGradleDependencyFallback` 写。
    它探测 `CoffeeBean.CAndroidRequiredDeps` 类型决定是否兜底，因此旧版 build 在场时也能兜底。

  ⚠️ build v0.3.0 同时修正了一个**门控缺口**：原回调在没有
  `Assets/CoffeeBean/ExportConfig.asset` 时直接 warning + return。若把必需依赖塞进那条
  配置门控的管线，没配过导出定制的工程就会静默漏依赖。现在必需依赖走独立入口，
  每次 Android 导出/构建都执行。

  依赖版本取自 Google Maven 元数据：`com.google.android.play:review:2.0.2`。

## [0.1.51] - 2026-09-17

### Changed
- 模块目录 `com.coffeebean.tools` latest → **v0.7.0**（**纯 registry 同步，无代码变更**）：
  tools 新增两个**原生平台**接口。
  - `CAppReview` —— 应用内评价：iOS 走 `UnityEngine.iOS.Device.RequestStoreReview()`；
    Android 走 Google Play In-App Review（JNI）。结果枚举显式区分 `Unavailable`（缺依赖）
    与 `Failed`，并带冷却（默认 90 天）与 `OpenStorePage()` 兜底。
  - `CDeviceLocale` —— 设备地区 / 语言：`LanguageCode` / `CountryCode` / `LocaleIdentifier` /
    `IsRightToLeft` 等；Android JNI 直读 `java.util.Locale`，iOS 及其它平台用
    Unity 以 `NSLocale` 初始化的 `CultureInfo`，兜底再映射 `Application.systemLanguage`。

  ⚠️ Android 应用内评价需要消费工程自己在 Gradle 依赖里加 `com.google.android.play:review`
  （框架不代为分发 Google 的二进制）；缺依赖时接口返回 `Unavailable` 并给出可操作告警。

## [0.1.50] - 2026-09-17

### Changed
- 模块目录 `com.coffeebean.save` latest → **v0.5.0**（**纯 registry 同步，无代码变更**）：
  save 补上了 **`BigInteger` 的 MemoryPack 格式化器**。
  `BigInteger` 是存档核心类型（`PlayerData.mBasicItems_total` / `PeopleData.mBasicItems_total`
  都是 `Dictionary<int, BigInteger>`），此前 save 没提供它，等于把这块留给每个消费工程自己手写。

  **为什么不能直接用 MemoryPack.Core 的内建版**：MemoryPack.Core 确实带了
  `MemoryPack.Formatters.BigIntegerFormatter`，但 NuGet 那份 DLL 把上游一条有缺陷的分支
  （`temp.Slice(written)`，应为 `Slice(0, written)`）编了进去 ——
  该分支只在未定义 `UNITY_2021_2_OR_NEWER` 时参与编译，Unity 工程编译自带源码时走安全分支，
  而 netstandard2.1 的 DLL 没有 Unity 宏。实测同一个 13 字节数值：
  本模块版 17 B 且读回正确，DLL 内建版 246 B 且**读回 = 0**（自读不自洽）。

  本模块实现与工程历史手写版**逐字节一致**（4 字节小端长度 + `ToByteArray()`），
  升级**不改变已写出的存档格式**。

## [0.1.49] - 2026-09-17

### Changed
- 模块目录同步到各模块最新版本（**纯 registry 同步，无代码变更**）：
  - `com.coffeebean.save` latest → **v0.4.0**：修掉**多槽位写入被静默丢弃的竞态** ——
    后台写盘原先只有一个待写信箱，第二次入队直接覆盖第一次，
    「先写 A 槽、紧接着写 B 槽」时 A 的写会丢，且丢不丢取决于后台线程调度时机。
    本模块自带的 `SaveDataAuto` 写的正是另一个槽位（`{Slot}_auto`），
    所以 `SaveData(x)` 紧跟 `SaveDataAuto(x)` 就是真实触发路径。
    已改为**按槽位**分别暂存（不同槽位互不覆盖，同槽位仍「最新优先」），无 API/存档格式变更。
  - `com.coffeebean.excel` latest → **v0.3.0**：补上 `[assembly: CoffeeBeanModule]` 模块标记。
    此前 excel 既无 `Runtime/` 也无标记，Core 完全发现不到它 ——
    `Window > CoffeeBean` 里看不到，`MinCoreVersion` 校验也覆盖不到。
    标记放在 **Editor-only** 的 Bridge 程序集里（`includePlatforms: ["Editor"]`）：
    与本包「Editor-only 配置表工具链」一致，玩家包体不会白带一个空 DLL。

## [0.1.48] - 2026-09-17

### Added
- **安装模块时自动补装缺失依赖**。此前 `Window > CoffeeBean` 的「安装」只做一件事：`Client.Add(模块自己的 git url)`。
  于是装 `save` 必然失败——它的 `package.json` 声明了 `com.coffeebean.tools: 0.5.0` 与
  `com.cysharp.memorypack: 1.21.4`，而模块是 git 包、不在任何 registry 里，
  UPM 无法把版本号解析成地址，解析直接报错。
  现在新增 `ModuleDependencyResolver`（纯逻辑、可单测）与
  `ModuleInstaller.InstallWithDependencies / InstallPlan`：
  - 按 `dependencies` 递归展开 CoffeeBean 模块依赖（传递闭包、菱形去重、环检测）；
  - 按 `externalDependencies` 安装 registry 之外的第三方包（用条目里登记的原样 UPM url）；
  - 已在工程中的依赖跳过；**目标模块无论是否已装都进计划**，所以「首次安装」与「更新到指定 tag」共用一条路径；
  - 串行 `Client.Add`（每次解析基于上一个已就位的结果），完成后回一次 `AssetDatabase.Refresh()`；
  - 安装前的确认框会列出「本次将自动补装哪些依赖」，registry 里查不到的依赖只告警不中断。
  - `com.coffeebean.core` 恒视为已存在（它不登记在 registry 里，否则用户能把它当普通模块装卸）。

### Changed
- **registry schema 升到 version 2**：每个模块条目新增 `dependencies`（CoffeeBean 模块 id）
  与 `externalDependencies`（`{id, url}`）。JsonUtility 会忽略缺失字段，因此旧版 registry JSON 仍可用。
  已按各模块 `package.json` 的真实依赖补齐全部 16 条：`ad → tools+telemetry`、`asset/build/debug/input/net/telemetry → tools`、
  `events → core`、`purchase → excel`、`save → tools`（第三方 `com.cysharp.memorypack`）、`ui → tools+asset`。
  注意：`com.unity.addressables` / `com.unity.purchasing` 这类官方 registry 能自行解析的依赖**不登记**，
  它们由 UPM 处理，登记反而会绕过版本约束。

### Tests
- 新增 `ModuleDependencyResolverTests`（22 个用例）：传递顺序、菱形去重、已存在跳过、目标恒在计划内、
  第三方依赖排在最前且用显式 url、缺 url/缺 repo/未登记依赖只告警、环与自环报错、大小写不敏感匹配、
  `BuildUrl` 拼接；另含 **registry 自洽性**校验（无重复 id、依赖均可解析、任意模块都能解析出无环计划、
  save 必须声明 memorypack）。

## [0.1.47] - 2026-09-17

### Changed
- 模块目录 `com.coffeebean.save` latest → **v0.3.0**：save 开始**内嵌 MemoryPack 二进制**
  （`Runtime/Plugins/MemoryPack/`：Core 1.21.4 + Roslyn 源生成器 + netstandard2.1 依赖）。
  消费工程从此不需要 NuGetForUnity、不需要手动放 DLL、不需要联网还原 ——
  此前只做 git 引用会产生 87 条 `CS0234/CS0246`。

  ⚠️ **升级注意**：若你此前用 NuGetForUnity 还原或手动放过 `MemoryPack.Core.dll` /
  `MemoryPack.Generator.dll`，升级到 save v0.3.0 时**必须先把它们移除**，
  否则 Unity 会因同名预编译程序集报错（`Multiple precompiled assemblies with the same name`）。

## [0.1.46] - 2026-09-17

### Changed
- 模块目录 `com.coffeebean.save` latest → **v0.2.0**：save 开始自带 **UniRx 的 MemoryPack 格式化器**
  （可选程序集 `CoffeeBean.Save.UniRx`，装了 `com.neuecc.unirx` 才编译 + 自动注册）。
  此前每个接入工程都要自己手写这份约 350 行的实现。

## [0.1.45] - 2026-09-17

### Changed
- 模块目录 `com.coffeebean.save` latest → **v0.1.2**。该版本修的是**文档缺陷**（无代码变更）：
  README 里的 MemoryPack git 路径写错（`?path=src/MemoryPack` 并非 Unity 包），
  且未说明「git 引用之外还必须自备 NuGet 的 `MemoryPack.Core.dll` + Roslyn 源生成器」——
  这是把模块接入真实工程（IdleMedievalLife）时暴露的：只做 git 引用会产生 87 条 `CS0234/CS0246`。

## [0.1.44] - 2026-09-14

### Changed
- **模块目录（registry）同步到各模块最新版本**：`ad → v0.2.0`、`asset → v0.3.0`、`build → v0.2.0`、
  `di → v0.1.1`、`input → v0.1.2`、`purchase → v0.4.0`、`save → v0.1.1`、`telemetry → v0.1.1`、`ui → v0.2.4`
  （其余未变：`debug v0.2.0` / `events v0.3.0` / `excel v0.2.3` / `fsm v0.2.0` / `net v0.2.0` /
  `pooling v0.2.0` / `tools v0.6.0`）。
  这样打开 `Window > CoffeeBean` 才能看到并可一键升级到这些修复版本。
- 顺带把 registry JSON 的排版规整为常规 2 空格缩进（原先是从 PowerShell 导出的、冒号后带对齐空格的格式，
  人工 diff 时噪声很大）。

### Notes
- 本轮各模块修复的要点（详见各自 CHANGELOG 与 GitHub Release）：
  `build` iOS PBX 落盘编译错误 + manifest 幂等性；`purchase` ConsumeType 直映对齐设计文档；
  `telemetry` 后端从未被初始化 + 缓存事件不自动补发；`di` 循环依赖栈溢出；`save` 退出丢档 + `Flush` 不可靠；
  `asset` Bridge 版本号落后两个版本；`ui` 显示动画从未生效 + 依赖声明缺失；`input` 触屏抬起永不触发；
  `ad` 删除无用的 `CAdResult`。

## [0.1.43] - 2026-09-03

### Changed
- 模块目录 `com.coffeebean.input` latest → v0.1.1（补全 .meta 修复版本化，修复按 tag 拉包时 immutable folder asset ignored 报错）

## [0.1.42] - 2026-09-03

### Added
- **Hub 构建模式切换（Beta/Release）**：Window/CoffeeBean 品牌区下方新增构建模式条——
  显示当前模式、一键切换 Beta/Release（维护 PlayerSettings 符号 `COFFEEBEAN_DEV_TOOLS`/`COFFEEBEAN_LOG`，
  保留既有符号），支持"应用到所有平台组"；核心逻辑 `CBuildModeEditor`（可测试）
- 说明：Editor 下日志恒可用（UNITY_EDITOR 分支），Release 模式用于"模拟正式包 + 打上架包"

### Changed
- 模块目录 `com.coffeebean.tools` latest → v0.6.0（CLog 构建模式剥离 + CGameBuild）
- 模块目录 `com.coffeebean.debug` latest → v0.2.0（作弊注册 Conditional + 控制台 #if）

## [0.1.41] - 2026-09-03

### Changed
- 模块目录 `com.coffeebean.build` latest → v0.1.1（Google Play 150MB AAB 分包：Play Asset Delivery asset packs）

## [0.1.40] - 2026-09-03

### Added
- 模块目录新增 `com.coffeebean.build`（v0.1.0，原生导出定制：Android Studio / Xcode 工程导出后处理注入）

### Changed
- 模块目录 `com.coffeebean.ad` latest → v0.1.1（补全 .meta 修复）

## [0.1.39] - 2026-08-28


### Added
- 模块目录新增 `com.coffeebean.telemetry`（v0.1.0，打点框架：事件缓存 + 热插拔后端）与 `com.coffeebean.ad`（v0.1.0，广告框架：激励/插屏 + 可插拔后端 + 打点联动）

# Changelog

## [0.1.38] - 2026-08-28


### Changed
- 模块目录同步：asset → v0.2.2（资源依赖分析工具）

# Changelog

## [0.1.37] - 2026-08-28


### Changed
- 模块目录同步：ui → v0.2.3（面板统计）、asset → v0.2.1（组件自动释放钩子）

# Changelog

## [0.1.36] - 2026-08-28


### Added
- **COFFEEBEAN_CORE 宏自动安装**：Core 安装后自动加入全局脚本定义，使各模块 Bridge（Core 集成）真正参与编译（此前宏未定义，Bridge 从未生效）

### Changed
- 模块目录同步：debug → v0.1.1（接入 Core 生命周期）、ui → v0.2.2（面板遮罩）

# Changelog

## [0.1.35] - 2026-08-28


### Changed
- 模块目录同步：asset → v0.2.0（Pin/Unpin 常驻资源）、ui → v0.2.1（面板转场动画）

# Changelog

## [0.1.34] - 2026-08-28


### Added
- 模块目录新增 `com.coffeebean.input`（v0.1.0，输入抽象模块）

# Changelog

## [0.1.33] - 2026-08-28


### Added
- 模块目录新增 `com.coffeebean.di`（v0.1.0，依赖注入容器）

# Changelog

## [0.1.32] - 2026-08-28


### Added
- 模块目录新增 `com.coffeebean.debug`（v0.1.0，运行期调试模块：游戏内控制台 + 作弊命令）

# Changelog

## [0.1.31] - 2026-08-28


### Changed
- 模块目录同步：asset → v0.1.2（移除 Window/CoffeeBean 子菜单，恢复 Hub 单入口）

# Changelog

## [0.1.30] - 2026-08-28


### Changed
- 模块目录同步：asset → v0.1.1

# Changelog

## [0.1.29] - 2026-08-28

### Changed
- **CoffeeBean 工具中心窗口重新设计**（Window > CoffeeBean）：顶部品牌区（标题 + 版本 + 概览徽章：已装/可装/可更新）、
  左侧分组导航（管理 / 工具，选中高亮 + 描述 tooltip）、右侧内容区（模块管理双栏 / 工具卡片详情 + 大打开按钮）、
  底部状态栏——替代原先平铺按钮布局，信息更清晰、操作更顺手
- 模块目录同步：ui → v0.2.0（Addressables 面板加载 CAssetPanelLoader）

## [0.1.28] - 2026-08-28


### Changed
- 模块目录同步：excel → v0.2.3（生成代码默认命名空间统一为 CoffeeBean）

# Changelog

## [0.1.27] - 2026-08-28


### Changed
- 模块目录同步：excel → v0.2.2（生成代码独立 asmdef，加快增量编译）

# Changelog

## [0.1.26] - 2026-08-28

### Added
- 模块目录新增 `com.coffeebean.asset`（v0.1.0，资源管理模块：Addressables 封装门面 + 组件绑定 + 更新下载，可插拔后端）

# Changelog

## [0.1.25] - 2026-08-28

### Changed
- 模块目录同步：excel → v0.2.1、purchase → v0.2.1、ui → v0.1.1

# Changelog

## [0.1.24] - 2026-08-28

### Changed
- **CoffeeBean 菜单收敛为单入口**：`Window > CoffeeBean` 打开工具中心窗口（原 Module Manager 改造）——
  左侧工具导航（内置"模块管理" + 各模块注册的 Editor 工具一键打开），右侧模块管理内容区（已安装/可安装/检查更新/远程 registry）
- **新增 `CoffeeBeanToolAttribute` + `CoffeeBeanToolRegistry`**：各模块 Editor 工具窗口打标记即被 Hub 自动发现
  （模块内复制同名 attribute，无需编译期依赖 core；excel/purchase 已接入，移除各自独立菜单项）

## [0.1.23] - 2026-08-28

### Added
- 模块目录新增 `com.coffeebean.ui`（v0.1.0，UI 模块：UGUI 面板管理 + 代码生成，设计参考 QFramework UIKit + CodeGenKit）

## [0.1.22] - 2026-08-27

### Added
- 模块目录新增 `com.coffeebean.save`（v0.1.0，存档模块：MemoryPack 序列化 + AES 加密 + 原子写 / 损坏回退 / 自动存档节流 / 版本迁移）

## [0.1.21] - 2025-xx-xx

### Changed
- 模块目录同步（统一命名空间）：events → v0.3.0、tools → v0.5.0、net/pooling/fsm/excel/purchase → v0.2.0
- **统一命名空间**：全部类型迁移到 `CoffeeBean` 根命名空间（业务只需 `using CoffeeBean;` 即可使用所有模块主类型），模块内部辅助 / 测试 / 示例保留 `CoffeeBean.X` 子命名空间（父命名空间自动可见）
- **破坏性变更**：旧 `using CoffeeBean.X;` 需移除（类型已上移到根命名空间）

## [0.1.20] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.5（多语言表加密无乱码确认）

## [0.1.19] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.4（配置 JSON 混淆加密，默认开启）

## [0.1.18] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.3（JSON 生成进 Resources，运行时加载修复）

## [0.1.17] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.2（文件夹批量 + 增量生成 + 二级预览窗口）

## [0.1.16] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.excel` latest → v0.1.1（多 Sheet / 分章节 / 列说明注释）

## [0.1.15] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.excel`（v0.1.0，Excel 配置表工具链，Editor-only）

### Changed
- 模块目录同步：`com.coffeebean.purchase` latest → v0.1.6（Excel 解析迁移到 excel 模块）

## [0.1.14] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.fsm`（v0.1.0，状态机：泛型 CStateMachine + 全局状态，独立无依赖）

## [0.1.13] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.pooling`（v0.1.0，对象池：CPool 纯 C# 泛型池 + CGameObjectPool Prefab 池，独立无依赖）

## [0.1.12] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.net`（v0.1.0，网络模块：HTTP / TCP / WebSocket，依赖 tools）

## [0.1.11] - 2025-xx-xx

### Added
- **MinCoreVersion 运行时校验**：模块声明的 Core 最低版本不满足时，该模块 fail-fast（不加载）并输出明确错误日志
  （新工具类 `CoffeeBeanVersion`：语义化版本解析 / 比较 / 最低版本判断，Module Manager 与运行期校验共用同一套版本逻辑）
- 单元测试：`CoffeeBeanVersionTests`（解析 / 比较 / IsSatisfied，含缺段补零、非法输入退化字典序）

### Changed
- Module Manager 的版本解析 / 比较收敛到 `CoffeeBeanVersion`（去重，行为不变）
- 模块目录同步：`com.coffeebean.tools` latest → v0.4.1、`com.coffeebean.events` latest → v0.2.1、`com.coffeebean.purchase` latest → v0.1.5

## [0.1.10] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.tools` latest → v0.4.0

## [0.1.9] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.tools` latest → v0.3.0

## [0.1.8] - 2025-xx-xx

### Changed
- 模块目录同步：`com.coffeebean.tools` latest → v0.2.0

## [0.1.7] - 2025-xx-xx

### Added
- 模块目录新增 `com.coffeebean.tools`（v0.1.0，工具模块，公开仓库）

## [0.1.6] - 2025-xx-xx

### Added
- **示例：Bootstrap Demo**（`Samples~/BootstrapDemo`）：手动引导/关闭、已发现模块清单、运行期配置演示
- 模块目录更新：`com.coffeebean.events` latest → v0.2.0、`com.coffeebean.purchase` latest → v0.1.4（Module Manager 会提示更新）

## [0.1.5] - 2025-xx-xx

### Fixed
- `CoffeeBeanBootstrap` 跨场景常驻：`DontDestroyOnLoad` + 单例保护
  （修复 Loading 场景跳转 Main 时框架被 OnDestroy 关闭、Context 丢失的问题；
  场景中重复挂载自动销毁多余实例）

## [0.1.4] - 2025-xx-xx

### Changed
- Module Manager：已安装的模块不再出现在 Available（未安装）列表，更新/卸载统一在 Installed 面板
- Module Manager：已安装列表改用 `Client.List` 异步拉取最新注册快照（修复安装/更新后版本号不刷新），
  版本显示以 manifest 引用 tag 为准

## [0.1.3] - 2025-xx-xx

### Added
- Module Manager 新增**检查更新**：比对已安装模块的 git 引用 tag 与 registry 的 latest
  （语义化版本比较，v0.1.9 < v0.1.10），可在窗口内一键更新 / 全部更新
- Installed 面板显示当前 ref 与"有更新"提示，逐行 Update 按钮
- 单元测试：版本 tag 解析与比较逻辑

## [0.1.2] - 2025-xx-xx

### Fixed
- 修复 Module Manager 窗口创建时报错：EditorWindow 字段初始化器里调用 `EditorPrefs.GetString`
  违反 ScriptableObject 序列化规则（UnityException）→ 移到 `OnEnable()` 加载

## [0.1.1] - 2025-xx-xx

### Fixed
- Module Manager 窗口默认尺寸过小（现在默认 900x560，最小 760x420）
- Module Manager 只管理 `com.coffeebean.*` 模块，不再列出其他来源的 git 包
- 模块目录随版本更新：`v0.1.1` 起内置 registry 包含 events + purchase

## [0.1.0] - 2025-xx-xx

### Added
- 初始骨架：模块标识特性 `CoffeeBeanModuleAttribute`
- 模块注册表 `CoffeeBeanRegistry`（程序集扫描 / 依赖查询）
- 引导器 `CoffeeBeanBootstrapper`（依赖拓扑排序、环检测、生命周期）
- 服务注册表 `ServiceRegistry`（模块间解耦）
- 运行期配置 `CoffeeBeanConfig`（模块启用/禁用开关）
- 编辑器 `Module Manager` 窗口与安装/卸载 API
- 官方模块目录 `RegistrySource`（内置 + 远程覆盖）
