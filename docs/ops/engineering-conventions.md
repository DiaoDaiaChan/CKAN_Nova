# 工程约定（所有 agent 必读）

> 本文件是执行层的**唯一共同约定**。规格本身的真相源是规格仓：`C:\Users\43701\Documents\github\CKAN\doc\uir\`（只读，23 篇 + `i18n/` 证据）。
> 代码仓是 `C:\Users\43701\Documents\github\ckan-ui`。两者分离（00-index D2/D16）。

## §1 目录与写作用域

权威目录树见 21 篇 §4。当前已就位：`src/` 11 个项目、`tests/` 7 个测试项目 + TestSupport + TestRunner、`adapters/`、`themes/`、`i18n/`、`docs/`、`tools/`、`.github/workflows/`。

**只改自己写作用域内的文件**（任务里写明）。跨域要改，先在任务里注明并通知 Lead，不要顺手改别人的文件。

## §2 构建

```bash
cd C:/Users/43701/Documents/github/ckan-ui
dotnet build ckan-ui.sln -v q --nologo          # 全量
dotnet build src/CkanUI.Sfs/CkanUI.Sfs.csproj   # 只建自己那块（并行时优先用这个）
```

- 自有项目 **0 警告**（`TreatWarningsAsErrors=true`，`Nullable=enable`，`AnalysisLevel=latest`）。
- 上游 `external/CKAN` 的 11 条警告（NU1904 / SYSLIB0050）已在 `Directory.Build.props` 里 `NoWarn`，**不要动上游代码**（00-index D5：不改 Core）。
- 并行开发时多个 agent 会同时写 `obj/`/`bin/`：**只构建自己那条链路**，遇到 `MSB3021/MSB3027/文件被占用` 等瞬时锁错误，等 5 秒重试一次；持续失败再报 Lead。

## §3 测试

沙箱里 `dotnet test` 不可用（VSTest testhost 打开父进程句柄被拒，Win32Exception 5）。用进程内 runner：

```bash
dotnet run --project tests/CkanUI.TestRunner                       # 全部
dotnet run --project tests/CkanUI.TestRunner -- Services   # 过滤词匹配的是**测试项目名**，不是测试用例名
```

- Runner 从 `tests/<proj>/bin/<config>/net10.0/<proj>.dll` 发现程序集，因此新增测试项目**不用登记**，构建后自动可见。
- **它不会替你重建测试项目**：改了测试源码必须先 `dotnet build ckan-ui.sln`，否则跑的还是旧 dll（实测踩过：改了断言却一直看到旧结果）。

测试项目本身是标准 NUnit 类库，CI 上照常用 `dotnet test`。

## §4 代码风格

- 语言级别 `latest`，文件级命名空间 `namespace CkanUI.Xxx;`，`ImplicitUsings=enable`。
- 一个文件一个公共类型；VM 用 `[ObservableProperty]`/`[RelayCommand]`（CommunityToolkit.Mvvm 8.4.0，`partial class`）。
- 注释与用户可见文案用**中文**；标识符英文 PascalCase（21 篇 §10 命名规范）。
- 禁止 `#pragma warning disable` 掩盖自己的警告；确实需要就在本文件顶部 `NoWarn` 并写理由注释。
- **禁止**把本机绝对路径、日志、dump、真实存档写进仓库（21 篇 §12 H-03/H-10）。

## §5 架构铁律（`tests/CkanUI.Architecture.Tests` 会红）

1. `Avalonia.*` 只允许出现在 `CkanUI.App`、`CkanUI.Controls`、`CkanUI.Platform`、`CkanUI.Services/Threading/`；
2. `CKAN`（Core）命名空间只允许出现在 `CkanUI.Services` 与 `CkanUI.Localization`；
3. `CkanUI.ViewModels` 只能引用 `Services.Abstractions`，不得引用 `CkanUI.Services`；
4. `Core` / `Services.Abstractions` / `Sfs` / `ModText` 四个项目**零 ProjectReference 零 PackageReference**（测试框架除外）；
5. 依赖方向唯一：`App → ViewModels → Services.Abstractions ← Services → CKAN`。

## §6 提交

- 并行期间**不要各自 git commit**：Lead 统一提交与审查（避免索引竞争）。
- 完成一项就在回复里说明：改了哪些文件、构建结果、测试结果、剩余 TODO。

## §7 规格阅读顺序

动手前先读**与本任务直接相关的那一篇**（不要只读摘要）。篇目：00-index、01-architecture、02-core-integration、03-ui-spec、04-mvvm-and-models、05-threading、06-wbs-milestones、07-testing、08-risk-register、09-migration-packaging、10-spike-plan、11-tech-stack-decision、12-feature-parity、13-savegame-sfs、14-log-and-ai-debug、15-mod-config-adapters、16-i18n、17-mod-metadata-i18n、18-launch-resource-release、19-windows-install-flow、20-appearance-personalization、21-repo-layout、22-test-cases。

## §8 里程碑口径

M0 = ENV-01..05 + ARC-01..04 + INT-01..03，门禁六条：①`dotnet build` 0 警告 ②启动到主窗口 ③日志落盘 ④`IUser` 弹框不死锁 ⑤CI 三平台绿 ⑥`tools/verify-layout` 全绿。

当前进度：ENV-01..02、ENV-04（CI 三平台 + verify-layout 全绿）、ENV-05（架构测试 9 条）、ARC-01（DI 组装根 + `IDispatcher`）、ARC-02/03（ViewLocator + Shell 导航）、ARC-04（三条崩溃兜底）、ARC-05（单实例 + `ckan://`）、INT-01..03（Core 接线 + 启动序列 + 服务注册）完成；全量测试 83 通过 0 失败。

## §9 本沙箱的两个环境限制（不是代码缺陷，别去"修"）

1. **`dotnet test` 不可用** → 见 §3，用 TestRunner。
2. **由本沙箱拉起的 GUI 子进程无法写 `%LocalAppData%\CKAN`**：实测 `CkanUI.App.exe` 里任何路径（`CKAN.log`、`probe2.log`、`ckan-ui/probe3.log`）在该目录下都抛
   `UnauthorizedAccessException: Access to the path 'C:\Users\<user>\AppData\Local\CKAN\CKAN.log' is denied`，
   而同一 shell 里 bash 写同名目录成功；log4net 内部日志报
   `ERROR [RollingFileAppender] ErrorCode: GenericFailure. Unable to acquire lock on file ... is denied`。
   因此"启动后日志落盘"这条门禁**不能**靠手工起 GUI 验证，改由测试保证：
   `tests/CkanUI.Services.Tests/ShippedLogConfigTests.cs` 用随包 `src/CkanUI.App/log4net.xml` + 临时目录跑通配置与落盘，
   `LoggingSetupTests` 覆盖 fallback。真实 Windows 桌面环境下该路径可写（同一路径下 `config.json`/`repos/` 都由 Core 正常写入）。

## §10 日志配置的两条硬约束

1. 随包 `src/CkanUI.App/log4net.xml` 的 `file` 值必须是 `__CKAN_LOG_DIR__/CKAN.log`；
   `LoggingSetup` 在配置前把占位符替换成 `CKAN.IO.CKANPathUtils.AppDataPath`。
2. **不要**用 log4net 的 `${ENVVAR}`、更不要用 NLog 的 `%envFolderPath{...}`（log4net 根本没有这个语法）：
   XML 解析失败时 `XmlConfigurator` 只写内部日志、不抛异常，结果是仓库保持"未配置"、日志静默丢失。
   `LoggingSetupTests` + `ShippedLogConfigTests` 会拦住这类回归。

## §11 M1 契约决策（对话框与详情面板）

1. **对话框的分层**：契约 `IDialogService` 在 `CkanUI.Services.Abstractions`，唯一实现 `DialogService` 在 `CkanUI.Platform`（Avalonia 可用、看不到 App 的视图），
   真实视图由 `CkanUI.Platform.Dialogs.IDialogPresenter` 接缝注入，实现在 `src/CkanUI.App/Dialogs/DialogPresenter.cs`。
   每个呈现方法返回可空值：`null` = "这类对话框我不接"，`DialogService` 退回内置代码建窗实现。**禁止**让 Services 层 new 任何视图或在 Services 里实现 IDialogService。
2. **纯提示不接管**：`ShowMessageAsync` 一律走内置轻量窗口；只有 `ShowErrorAsync` 走 `ErrorDialogView`（带技术细节折叠与复制诊断）。把普通提示渲染成"出错了"加堆栈是语义错位。
3. **剪贴板通道**：VM 只拿 `Func<string, Task>`，由 `ServiceRegistration.CreateClipboardWriter` 注册（`TopLevel.GetTopLevel(owner)?.Clipboard`）；没有 owner 或剪贴板时抛 `NotSupportedException`，VM 转成"当前环境没有剪贴板通道"的提示而不是崩溃。
4. **详情面板富字段**：`ModDetailsSnapshot`/`ModRelationItem` 暂留 `CkanUI.ViewModels.Dialogs`，由页面 VM 通过 `DetailsProvider` 属性注入；**不**提升进 `Services.Abstractions.Dtos`，直到 Core 侧落一个 `CkanModule → ModDetailsSnapshot` 适配器（避免契约里塞 UI 专属形状）。
5. **单选取消值**：`SelectDialogViewModel` 取消返回 `-1`，与 `IDialogService.SelectAsync` 注释一致；若 SP-04 发现某个 Core 调用点期望 0 或 null，只改 `SelectDialogViewModel.OnSecondary`，不动契约。

## §12 M1 契约决策（设置、主题与操作队列）

1. **界面语言的唯一真源是 `IUiSettingsStore.Locale`**（存 `%LocalAppData%/CKAN/ckan-ui.json`），不回写 Core 的 `SettingKeys.Language`。
   理由：UI 语言是纯表现层偏好，Core 的 `config.json` 还要被 CLI 与旧 GUI 读写，两处真源必然漂移；`LocalizationHost` 与设置页都只认 `Locale`。
2. **`UiSettings` 新增 `Density`（compact / standard / comfortable，默认 "standard"）**：密度属于外观偏好，必须持久化，所以从"仅本次会话"升级为落盘字段；
   `Equals`/`GetHashCode` 已同步（record 合成相等对字典属性不可靠，本类型手写了值相等）。
3. **主题生效点**：`src/CkanUI.App/ThemeHost.cs` 订阅 `IUiSettingsStore.Changed`，在首帧前 `Load()` 并设置 `Application.RequestedThemeVariant`；
   设置页只负责存值，**不得**直接改 `Application`（VM 层禁止引用 Avalonia）。
   Avalonia 12 的 `ThemeVariant` 只有 Light/Dark/Default 三个内置值，高对比度用自定义变体 `ThemeHost.HighContrastVariant = new("HighContrast", ThemeVariant.Dark)`，
   key 必须与 `src/CkanUI.App/Resources/Tokens.axaml` 的 ThemeDictionaries key 一致。
4. **`IUiSettingsStore.Changed` 在锁外触发**：订阅方会同步回读 `Current`，持锁回调容易把锁序搞乱；订阅方异常只记日志，不影响保存结果（05 篇 §7）。
5. **`OperationProgress` 暂不扩字节/重试字段**：浮层的字节与速率从 `Message` 文本解析（解析不到显示 `—`，绝不拿 0 冒充）。
   扩契约要同时改 Services 的订阅侧与全部生产方，收益不足以进 M1；等 M2 安装器打通真实下载后再一次性加 `BytesTransferred/BytesTotal`。
6. **安装器后端保持 public**：`IInstallerBackend`/`IInstallerBackendFactory` 在 `CkanUI.Services.Installers` 内公开，便于 `tests/CkanUI.Services.Tests` 用假后端离线跑；
   要改 internal 需给 Services 加 `InternalsVisibleTo`，M1 不做（收益只是少两个公开类型）。
7. **页面之间的交接只走一个窄接口**：模组页把勾选翻译成 `ChangeRequest` 后灌进 `IChangeRequestSink`（`src/CkanUI.ViewModels/Pages/IChangeRequestSink.cs`），
   再经 `INavigationService.NavigateAsync("changes")` 跳转；`ChangesPageViewModel` 是唯一实现。
   理由：两页互相持有具体类型会让"哪个页面持有谁的引用"变成隐式耦合，也让 DI 循环；窄接口让两个页面各自可独立测。
8. **导航项顺序固定为 mods → changes → repos → modpacks → instances → playtime → saves → unmanaged → cache → dedup → installhistory → plugins → settings**（十三项，`ShellViewModel.NavItems`），key 即 `PageTypes` 的键；
   新增页面必须同时补 `LocalizationKeys` 与两份语言包（`i18n/packs/{zh-CN,en-US}.json`），否则 `ShellViewModelTests` 与 `verify-layout` 会红。
9. **仓库管理的写事务封在 `IRepositoryStore` 后面**：`RegistryRepositoryStore` 是唯一碰 `Registry.RepositoriesSet` + `RegistryManager.Save()` 与网络的实现，
   校验/排序/重编号全在纯函数 `RepositoryCatalog`（可离线测）。`IRepositoryService` 的 8 个增量成员用 DIM（默认实现）追加，
   既有实现与测试替身零改动——这是扩冻结契约的唯一允许方式（否则会连锁改十几个 fake）。

10. **页面 VM 必须放在 `src/CkanUI.ViewModels/Pages/` 根目录**（辅助类才进子目录）：`ViewLocator.ResolveViewType` 是按名字把 `CkanUI.ViewModels.Pages.XPageViewModel` 换成 `CkanUI.App.Views.XPageView`，
    放子目录会连 namespace 一起变，反射找错视图、静默落到占位视图。新增页面必须同时在 `tests/CkanUI.App.Tests/ViewLocatorTests.cs` 加一行断言。
11. **modpack 导出不自己拼 JSON**：一律走 Core 的 `RegistryManager.GenerateModpack(recommends, with_versions).ToJson()`（与 `CkanExporter` 同语义），
    `depends`/`recommends`/`metapackage`/`spec_version` 由 Core 决定，这样导出物才可能被 `ckan import` 接受。
    可选「打包缓存 zip」是**本仓自定**的朴素归档（根放 `.ckan`，`Downloads/<id>-<ver>.zip` 放下载包）——上游没有这种归档格式，改动格式要同时改 `docs/design/pages.md` §2.7。
12. **导入执行只做编排**：勾选行 → `ChangeRequest` → `IChangeSetService.BuildAsync`（`HasConflicts` 即中止且**不**调 `ApplyAsync`）→ `IInstallerService.ApplyAsync`。
    模块内不得出现自有下载/解包逻辑，否则依赖求解与安装口径会分叉。
13. **下载分两层**：字节搬运在 `IDownloadTransport`（生产实现 `NetAsyncDownloaderTransport`，测试用假传输层，**测试绝不触网**），
    落盘语义（`.tmp-{guid}` → 大小校验 → SHA256 校验 → `File.Move(overwrite:true)` 重试 → finally 删临时文件）在 `DownloadService`。
    踩坑固定结论：`NetAsyncDownloader.DownloadAndWait` 是同步阻塞（必须 `Task.Run` 包，取消靠构造器 token）、`TargetProgress` 第二参是**剩余字节**、
    `DownloadTargetFile.size` 是断点续传起点（新下载必须传 0）。失败分类只产出 `retryable` 标志，重试策略交给上层（F-38 / UI-35）。
14. **启动命令持久化在 `<GameDir>/CKAN/GUIConfig.json` 的 `CommandLines` 键**（与老 GUI 共用同一文件）：只改该键、其余字段原样保留，写 `.tmp` 再 `File.Move(overwrite:true)`；
    默认命令按 `KerbalSpaceProgram.DefaultCommandLines` 语义生成，命令解析口径与 Core `PlayGame` 一致（首个 token 为 binary）。

15. **搜索语法不在页面层**：页面只认 `IModSearchParser` 返回的 `ModSearchSpec`，语法权威仍是上游 `ModSearch`（`ModSearch.cs` 公开签名未改）。
    `ModsQuery` 逐条镜像 `ModSearch.Matches` 的语义，`UnsupportedTerms` 只用于"上游会静默丢弃"的情况（`is:`/`not:` 未知值、未知前缀、前缀缺值），
    必须在状态栏用中文说明——**不许静默丢词**。需要实例上下文的事实（`is:cached`/`is:newly-compatible`/`label:`/关系前缀）由服务层写进 `ModRowData.Facts`，
    页面不得自己解析模块元数据；宿主没注入 parser 时降级为"不过滤 + 逐词提示"，不许假装过滤。
16. **未管理文件的安全边界在服务层**：`UnmanagedScanner` 不引用 CKAN（纯文件系统，可 headless 测），受管路径集合由 `IManagedPathProvider` 从
    `registry.InstalledFileInfo()` + `InstalledDlls` 合成；`DeleteAsync` 默认 `dryRun:true`，**任一路径受管或包含受管内容即整批拒绝**（不可重试，提示去模组页卸载），
    dry-run 也不放行——因为"预演成功、执行失败"比"预演就拒绝"更伤信任。界面必须两次 `ConfirmAsync` 才落盘。
17. **存档格式化的安全边界**：默认只写 `<stem>.formatted.sfs`；就地重写先落 `.pre-edit-<yyyyMMdd-HHmmss>.sfs` 快照再 `File.Move` 原子写并还原 `LastWriteTime`；
    源文件有 Error 诊断或自校验失败一律 `Refused` 且不落盘（**不猜修复**）；`*.sfs.bak` 永不覆盖；损坏存档返回 `Ok` + 诊断而不是抛异常。
    `.sfs` 的键挂在根节点 `GAME` 下（不是顶层），读头部必须"顶层键值对 + 根节点直接子键"一起看，否则 `Title`/`scene` 恒为 null。
18. **资源释放必须可逆且分层独立**（18 篇 E6）：能真正放掉的只有「仓库快照 + `RegistryManager.DisposeAll()`」，所以 `ReleaseAsync` 之后必须能用
    `OpenAsync(当前实例)` + `LoadCachedAsync()` 还原；每层独立成败——失败只 `Log.Warn` 并继续后续层，报告恒含五层（L4 未接入记 `Skipped` 不算失败）。
    档位落在 `UiSettings.ReleaseTier`（默认 `Balanced`），`Off` 时连计时器都不起；取消时剩余层标 Skipped、`Cancelled=true`，**仍返回 Ok**（取消不是失败）。
    退出信号由 App 侧 `ShellInstanceLauncher` 保留进程句柄后广播——`UseShellExecute=true` 直接 `Dispose()` 就永远等不到 `Exited`，E6 的 Restore 也就永远不触发。
19. **CKAN 版本事实只能从 Services 层薄壳取**：`src/CkanUI.Services/Diagnostics/CoreVersionInfo.cs` 暴露 `Meta.ReleaseVersion`/`Meta.ProductName`；
    App 层拼装（`AboutInfoFactory`），ViewModels 只收字符串或 `Func<string?>` 委托。不许让 VM 引用 `CKAN` 命名空间或在视图里写死版本号。
20. **插件、去重、安装历史三条新页面的口径**：
    - 插件（F-17）扫描一律 `activate:false` 进 Dormant，**必须用户显式点「激活」才执行 `Initialize`**；加载用可回收 `AssemblyLoadContext`，
      且 `Load(AssemblyName)` 对契约程序集与 `System/Microsoft/netstandard/mscorlib/WindowsBase/CkanUI/Avalonia` 前缀返回 null（否则插件会加载出第二份
      `CkanUI.Services.Abstractions`，`IsAssignableFrom` 判假并抛 `InvalidCastException`）；用 `File.OpenRead + LoadFromStream` 而非
      `LoadFromAssemblyPath`（后者长期占用 dll 句柄，「删 dll 后重启」会卡住）；失败分支一律先调幂等的 `Release()`。
    - 跨实例去重（F-41）的六条安全边界全在服务层，逐条给中文跳过原因、从不因单文件失败抛异常：仅在 `<实例>/GameData` 内、跳过重解析点（扫描与建链前各判一次）、
      默认跳过受管路径（`IncludeManagedFiles` 才能覆盖，否则会破坏卸载语义）、同卷、≥2 副本、默认 `DryRun=true`。
      写盘用「同目录临时名 → `CreateLink` → 校验两侧文件标识 → `File.Move(overwrite:true)`」，失败按**组**逆序回滚（组是原子单位）。
    - 安装历史（F-44）落 `<GameDir>/CKAN/install_history.jsonl`，一次操作 = entry + snapshot 两行**共用同一时间戳**；快照只存 identifier+version，
      「是否已装」在读取时按当前注册表现算（历史不因注册表变化而失真）；自动记账靠 `HistoryRecordingInstaller` 装饰器，重装时用 `InstallHistoryScope`
      （AsyncLocal）抑制双记；截断按行数（默认 2000），写入走 `.tmp` + `File.Move(overwrite:true)`。

## 修订记录

| 日期 | 版本 | 变更 |
| --- | --- | --- |
| 2026-10-27 | v1.0 | 新建：M0 开工时的工程约定，供并行 agent 共同遵守 |
| 2026-10-27 | v1.1 | 补 §3 runner 不重建测试项目、§8 M0 进度、§9 沙箱环境限制、§10 日志配置硬约束 |
| 2026-10-27 | v1.2 | 补 §11 M1 契约决策（IDialogPresenter 接缝、剪贴板通道、详情面板字段归属、单选取消值） |
| 2026-10-27 | v1.4 | 补 §12.7–9（页面交接窄接口、导航项顺序与语言包义务、仓库写事务封在 IRepositoryStore 后、DIM 扩契约的方式） |
| 2026-10-27 | v1.3 | 补 §12 M1 契约决策（语言真源、`UiSettings.Density`、ThemeHost 与高对比度变体、Changed 锁外触发、OperationProgress 不扩字段） |
| 2026-10-27 | v1.6 | 导航项改为八项（新增 saves / unmanaged）；补 §12.15–17（搜索语法只走 IModSearchParser 且不许静默丢词、未管理文件删除的整批拒绝与双确认、存档格式化快照与 Refused 语义 + `.sfs` 根节点 GAME 的读法） |
| 2026-10-27 | v1.5 | 导航项改为六项（新增 modpacks）；补 §12.10–14（页面 VM 放 Pages 根、modpack 导出走 Core ToJson、导入只做编排、下载两层与 NetAsyncDownloader 踩坑、启动命令持久化） |
| 2026-10-27 | v1.8 | 导航项改为十三项（新增 playtime / cache / dedup / installhistory / plugins）；补 §12.20（插件 ALC 加载、去重六条安全边界与组内回滚、安装历史 JSONL 与防双记） |
| 2026-10-27 | v1.7 | 补 §12.18（资源释放可逆 + 分层独立 + 退出信号必须保留进程句柄）与 §12.19（CKAN 版本事实只走 Services 薄壳）；F-25/F-55 收口（导航历史、关于框） |
