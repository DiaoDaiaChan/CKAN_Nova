using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CkanUI.App.Ai;
using CkanUI.App.Dialogs;
using CkanUI.App.Lifecycle;
using CkanUI.App.Localization;
using CkanUI.App.Tray;
using CkanUI.Localization;
using CkanUI.Platform.Dialogs;
using CkanUI.Platform.Threading;
using CkanUI.Services;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Abstractions.Security;
using CkanUI.Services.Ai;
using CkanUI.Services.Onboarding;
using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Bootstrap;
using CkanUI.Services.Commands;
using CkanUI.Services.Diagnostics;
using CkanUI.Services.Lifecycle;
using CkanUI.Services.Logs;
using CkanUI.Services.Security;
using CkanUI.Services.Users;
using CkanUI.ViewModels;
using CkanUI.ViewModels.CommandPalette;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Operations;
using log4net;
using CkanUI.Services.Abstractions.Instances;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.Abstractions.Mods;
using CkanUI.Services.Abstractions.Update;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Logs;
using CkanUI.ViewModels.Pages.Settings;
using CkanUI.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CkanUI.App;

/// <summary>
/// DI 组装根（02 篇 §2 / ARC-01）。这是全应用唯一允许 new 具体服务实现的地方。
/// 规则：本容器不与 Core 的 Autofac 容器合并，Core 类型由 Services 层的 AddCkanCoreServices 转接。
/// </summary>
public static class ServiceRegistration
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServiceRegistration));

    /// <summary>
    /// 构建 UI 容器。
    /// </summary>
    /// <param name="ownerProvider">返回当前主窗口，供模态对话框指定 owner（05 篇 §2 铁律：必须指定 owner）。</param>
    public static ServiceProvider Build(Func<Window?> ownerProvider, Action<IServiceCollection>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(ownerProvider);

        var services = new ServiceCollection();

        // 表现层基础设施：IDispatcher 与 IErrorReporter 是所有服务的共同依赖。
        services.AddSingleton<IDispatcher>(new AvaloniaDispatcher());

        // 剪贴板通道（UI-09 复制诊断信息）：VM 只拿到委托，不碰 Avalonia。
        services.AddSingleton<Func<string, Task>>(_ => CreateClipboardWriter(ownerProvider));

        // 对话框：契约在 Abstractions、实现留在 Platform、视图由 App 的呈现者接上（03 篇 §8）。
        services.AddSingleton<IDialogService>(sp => new DialogService(
            sp.GetRequiredService<IDispatcher>(),
            ownerProvider,
            new DialogPresenter(ownerProvider, sp.GetRequiredService<Func<string, Task>>())));

        // 服务层（Services 项目自有实现）。
        services.AddCkanServices();

        // 卸载收尾的残留目录对话框（F-43）：只在卸载后出现一次，故单独成呈现者，不塞进 DialogPresenter。
        services.AddSingleton<IResidualCleanupDialogService>(sp => new ResidualCleanupDialogPresenter(
            ownerProvider,
            sp.GetRequiredService<IResidualCleanupService>(),
            sp.GetService<IErrorReporter>()));

        // 模组对话框族（F-34..F-36）：推荐选择 / 提供者选择 / 冲突详情。VM 只认 IModDialogService。
        services.AddSingleton<IModDialogService>(sp => new ModDialogService(
            ownerProvider,
            sp.GetRequiredService<Func<string, Task>>()));

        // 日志域（M8 / 14 篇）：缓冲是唯一真源，页面、抽屉、导出包都从它读。
        // 规则表允许外置覆盖，缺失或 JSON 坏掉时分类器静默回落内置 20 条规则（并保留 LastLoadError）。
        services.AddSingleton<ILogClassifier>(_ => new LogClassifier(
            Path.Combine(AppContext.BaseDirectory, "Themes", "log-rules.json")));
        services.AddSingleton<ILogStore>(sp => new LogStore(sp.GetService<ILogClassifier>()));
        services.AddSingleton<ILogQueryService>(sp => new LogQueryService(sp.GetRequiredService<ILogStore>()));
        services.AddSingleton<ILogRedactor>(_ => new LogRedactor());
        // 诊断包里的非日志事实由 App 采（版本/主题/实例数/仓库数），服务层只编排与脱敏。
        services.AddSingleton<IDiagnosticFactsProvider, Diagnostics.AppDiagnosticFactsProvider>();
        services.AddSingleton<ILogBundleService>(sp => new LogBundleService(
            sp.GetRequiredService<ILogStore>(),
            sp.GetRequiredService<ILogRedactor>(),
            sp.GetService<IDiagnosticFactsProvider>()));
        services.AddSingleton<ILegacyLogImporter>(sp => new LegacyLogImporter(sp.GetRequiredService<ILogStore>()));
        // 当前实例的 KSP.log 跟随（第五路采集）：实例切换由 App 启动时订阅 IInstanceService 接上。
        services.AddSingleton<IKspLogTailService>(sp => new KspLogTailService(sp.GetRequiredService<ILogStore>()));
        // AI 排障（14 篇 §6）：OpenAI 兼容客户端 + 只在本机可解的密钥保护（DPAPI，密文落盘）。
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<ILlmDiagnosticService>(_ => new OpenAiCompatibleDiagnosticService());
        // AI 与 CKAN 之间的宿主桥：只读查询走快照、导航走 INavigationService。构造期不做 IO，
        // 所以它排在 INavigationService 注册之前也没关系——单例解析是惰性的。
        services.AddSingleton<IAiCkanBridge, AiCkanBridge>();

        // IUser 的 UI 形态：Core 的同步回调会在这里被拦到 UI 线程（INT-03）。
        // 外面再套一层 LoggingUser：Core 的进度与警告进结构化缓冲（Kind=User、进度按整十百分点限流）。
        // 装饰器只写缓冲、不重复写 log4net —— AvaloniaUser.RaiseMessage 已经写过一遍，双写会让 UI 看板重复计数。
        services.AddCkanUser(sp => new LoggingUser(
            new AvaloniaUser(
                sp.GetRequiredService<IDispatcher>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IErrorReporter>()),
            sp.GetRequiredService<ILogStore>()));

        // Core 类型：由 Services 层从 Core 的 Autofac 容器转接过来。
        services.AddCkanCoreServices();

        // 页面 VM 一律登记为单例：导航切换不重建，避免丢状态（04 篇 §1）。
        // 各页面的构造依赖由容器注入，页面工厂只做"按 key 取实例"，不再 new 具体 VM。
        services.AddSingleton<OperationQueueViewModel>();
        // 变更集页同时充当「变更请求落点」：模组页把勾选灌进去再导航过去（IChangeRequestSink）。
        services.AddSingleton(sp => new ChangesPageViewModel(
            sp.GetService<IChangeSetService>(),
            sp.GetService<IInstallerService>(),
            sp.GetRequiredService<IErrorReporter>(),
            sp.GetService<IOperationQueue>(),
            sp.GetService<IDispatcher>(),
            sp.GetService<IResidualCleanupDialogService>(),
            sp.GetRequiredService<IInstanceService>(),
            sp.GetService<IModDialogService>(),
            sp.GetService<IUiSettingsStore>(),
            sp.GetService<IRepositoryService>()));
        services.AddSingleton<IChangeRequestSink>(sp => sp.GetRequiredService<ChangesPageViewModel>());
        // 详情面板（F-26 四页签）：模组页右侧直接渲染这同一个 VM，注册成单例后由模组页工厂注入，
        // 免得每页各建一份、切换实例后状态对不上。
        services.AddSingleton<ModDetailsViewModel>();
        services.AddSingleton(sp =>
        {
            var page = new ModsPageViewModel(
                sp.GetRequiredService<IRepositoryService>(),
                sp.GetRequiredService<IRegistryService>(),
                sp.GetRequiredService<IInstanceService>(),
                sp.GetRequiredService<IErrorReporter>(),
                sp.GetService<IChangeSetService>(),
                sp.GetService<IUiSettingsStore>(),
                sp.GetService<IChangeRequestSink>(),
                sp.GetService<INavigationService>(),
                sp.GetService<IModSearchParser>(),
                sp.GetService<ILabelService>(),
                sp.GetService<IModSearchFormatter>());

            // 富字段快照由模组页在选中行变化时喂进来（ModDetailsSnapshotBuilder），这里只负责接线。
            page.Details = sp.GetRequiredService<ModDetailsViewModel>();
            // F-30「全部版本」页签的数据源：跨仓库索引取同一标识符的全部版本（只读内存对象图）。
            page.VersionCatalog = sp.GetService<IModVersionCatalog>();
            return page;
        });
        // 内容预览页要拿模组页的当前选中项（F-29）：只经 IContentTargetProvider 窄接口，不直接耦合。
        services.AddSingleton<IContentTargetProvider>(sp =>
            new ModsSelectionContentTarget(sp.GetRequiredService<ModsPageViewModel>()));
        services.AddSingleton<ContentPageViewModel>();
        // 主菜单页：启动器与命令存储是宿主能力，由视图的 ??= 注入；容器只给服务契约。
        services.AddSingleton<HomePageViewModel>();
        services.AddSingleton<ReposPageViewModel>();
        services.AddSingleton<ModPacksPageViewModel>();
        services.AddSingleton<ModPackEditorPageViewModel>();
        // 实例页要拿资源释放服务与 UI 设置（18 篇 E6）：视图层的 ??= 注入拿不到容器，只能在这里挂。
        services.AddSingleton(sp =>
        {
            var page = ActivatorUtilities.CreateInstance<InstancesPageViewModel>(sp);
            page.ResourceRelease = sp.GetRequiredService<IResourceReleaseService>();
            page.UiSettings = sp.GetRequiredService<IUiSettingsStore>();
            // F-48 自动计时：实例页启动/退出时记账，服务端与 IPlayTimeService 是同一单例（见 ServiceCollectionExtensions）。
            page.PlayTime = sp.GetService<IPlayTimeTracker>();
            // F-08 实例自检/强制解锁/坏注册表隔离：服务在 Services 层注册，页面只认窄接口。
            page.Repair = sp.GetService<IInstanceRepairService>();
            return page;
        });
        // 存档 / 飞船 / 旗帜 / 加载图 / KSP 游戏设置五页已搬成首方插件（docs/design/plugins.md §11.11）：
        // 页面实例由 PluginPageFactory 按插件声明创建，宿主不再认识这些类型——
        // 这也是"停用插件后入口真的消失"的前提，否则 DI 里还留着一份永远不会被用到的单例。
        services.AddSingleton<UnmanagedPageViewModel>();
        services.AddSingleton<CachePageViewModel>();
        services.AddSingleton<DownloadStatsPageViewModel>();
        services.AddSingleton<PlayTimePageViewModel>();
        services.AddSingleton<DedupPageViewModel>();
        services.AddSingleton<InstallHistoryPageViewModel>();
        services.AddSingleton<PluginsPageViewModel>();
        // 日志页与底部抽屉（M8 / LOG-01..05）：两者共享同一个 ILogStore 单例，抽屉挂在日志页之上
        // （LogDrawerViewModel 内部另建一页供抽屉里的小时间线用），不重建日志页的状态。
        services.AddSingleton<LogPageViewModel>();
        services.AddSingleton(sp => new LogDrawerViewModel(
            sp.GetRequiredService<ILogStore>(),
            sp.GetRequiredService<ILogQueryService>(),
            sp.GetRequiredService<IKspLogTailService>()));

        // 分层资源释放的参与方（18 篇 §3）：L1 丢页面行投影，L0 放字形与背景位图。
        // 三个页面按窄接口登记，服务层只认识 IPageProjectionTrim，不认识任何具体页面。
        services.AddSingleton<IPageProjectionTrim>(sp => sp.GetRequiredService<ModsPageViewModel>());

        // 插件工具项（12 篇 F-17）：目前只有模组管理页的工具条接收插件按钮。
        services.AddSingleton<IPluginToolHost>(sp => sp.GetRequiredService<ModsPageViewModel>());
        services.AddSingleton<IPageProjectionTrim>(sp => sp.GetRequiredService<ContentPageViewModel>());
        // 插件页（「模组翻译」）也实现了 IPageProjectionTrim，但它不在 DI 里：
        // 实例由 PluginPageCache 持有，参与者在 TrimAppCache 里单独问一遍（见 PageProjectionCacheParticipant）。
        services.AddSingleton<Plugins.PluginPageCache>();
        services.AddSingleton<UiViewResourceParticipant>();
        services.AddSingleton<IViewResourceParticipant>(sp => sp.GetRequiredService<UiViewResourceParticipant>());
        services.AddSingleton<IAppCacheParticipant, PageProjectionCacheParticipant>();
        // 本地化诊断表要在 App 侧才能登记（ILocalizer 由上面这条工厂创建）。
        services.AddSingleton<IAppCacheParticipant, LocalizerCacheParticipant>();

        // 设置页的诊断区需要 CKAN 库版本（12 篇 F-55 同源）：VM 只收委托，CKAN 类型不越界。
        services.AddSingleton(sp =>
        {
            var page = ActivatorUtilities.CreateInstance<SettingsPageViewModel>(sp);
            page.Diagnostics.CoreVersionProvider = () => CoreVersionInfo.ReleaseVersion;

            // F-53：语言候选只列真正随包发布的语种（i18n/packs 里有的那些）。
            // 硬编码 zh-TW/ja-JP/de-DE 会让用户选完重启后毫无变化——那是假 UI，宁可少列。
            // 显示名取「本地名（代码）」，不拼覆盖率百分比，避免把 100% 误读成翻译质量。
            var localizer = sp.GetRequiredService<ILocalizer>();
            page.LocaleChoicesProvider = () => localizer.AvailableLocales
                .Select(locale => (Code: locale.Name, Display: $"{locale.NativeName}（{locale.Name}）"))
                .ToList();
            page.RefreshLocaleChoices();

            // 首次启动指引的手动重入：用户跳过代理那里之后再也没别的入口，这条必须接上。
            page.FirstRunWizard = sp.GetService<IFirstRunWizardLauncher>();

            // F-52 更新弹窗：VM 只发委托，真实弹窗与「打开发布页」的浏览器通道都在 App 侧接。
            // 不接的话更新弹窗永远不会出现——用户只能看到状态栏里一句「发现新版本」，点不到安装。
            page.OpenExternalUrlAsync = CreateUrlLauncher(ownerProvider);
            page.ShowUpdateDialogAsync = dialogVm => ShowDialogAsync(
                new Views.Dialogs.UpdateAvailableDialogView { DataContext = dialogVm },
                ownerProvider);
            return page;
        });

        // 本地化：磁盘语言包优先、内嵌兜底；App 侧把告警接到 log4net（docs/ops/i18n-guide.md §6.1）。
        services.AddSingleton<ILocalizer>(sp =>
        {
            var packs = Path.Combine(AppContext.BaseDirectory, "i18n", "packs");
            var log = new Log4NetLocalizationLog();

            // 界面语言的唯一真源是 IUiSettingsStore.Locale（工程约定 §12），未设置时跟随系统 UI 语言。
            // 这里必须显式 Load()：Localizer 是窗口显示前第一个被解析的单例，早于各 ThemeHost 的 Load 调用，
            // 此时 Current 还是默认值，读它等于永远拿不到用户上次选的语言。
            var locale = sp.GetService<IUiSettingsStore>()?.Load().Locale;

            return LocalizerFactory.CreateFromDirectory(packs, new LocalizationOptions
            {
                Log = log,
                FallbackLocale = "en-US",
                DefaultLocale = string.IsNullOrWhiteSpace(locale) ? null : locale,
                // 只切 CurrentUICulture，数字/日期仍跟随系统区域（16 篇 §2.3 / Q3）。
                ApplyThreadCulture = true,
                // D11 的 L3：Core. 前缀键转发到上游 CKAN 的 209 条资源；资源缺失时该层静默降级（16 篇 §3）。
                Sources = [new CoreResourceStringSource(log)],
            });
        });

        // 视图模型不引用本地化层（架构铁律 2），只拿一个「取译文」委托。
        services.AddSingleton<Func<string, string>>(sp => sp.GetRequiredService<ILocalizer>().Get);

        // 命令面板（UI-27）：模糊匹配与命令目录属于服务层，视图模型只认接口；
        // 26 条内置命令（16 导航 + 10 动作）在这里一次性注册，Id 由 DefaultCommands 保证唯一。
        // 搬进插件的页面（存档 / 飞船 / 旗帜 / 加载图 / KSP 游戏设置 / 模组翻译）不在表里：插件可停用，
        // 静态表留条目就会留下点了没反应的 nav:<键>。动态注册见 docs/design/plugins.md §11.11。
        // 注意：ShortcutsCatalogViewModel 不登记单例 —— 它是 DialogViewModelBase，IsClosed 是状态，复用会带着上次关闭态。
        services.AddSingleton<IFuzzyMatcher>(_ => new FuzzyMatcher());
        services.AddSingleton<ICommandCatalog>(sp =>
        {
            var catalog = new CommandCatalog(sp.GetRequiredService<IFuzzyMatcher>());
            _ = catalog.Register(DefaultCommands.CreateAll(sp.GetRequiredService<Func<string, string>>()));
            return catalog;
        });
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<CkanUI.ViewModels.Onboarding.OnboardingTourViewModel>(sp =>
        {
            var uiSettings = sp.GetRequiredService<IUiSettingsStore>();
            var translate = sp.GetRequiredService<Func<string, string>>();
            return new CkanUI.ViewModels.Onboarding.OnboardingTourViewModel(
                translate,
                onCompleted: () =>
                {
                    var current = uiSettings.Load();
                    uiSettings.Save(current with { HasSeenOnboardingTour = true });
                });
        });

        // 导航与外壳。
        // 导航服务顺便把本地化解析器挂到每个被解析出来的页面上（页面文案 i18n 的统一入口）：
    // 页面 VM 只认 Func<string,string>，认不出 ILocalizer，因此这里传本地化器的 Get。
    services.AddSingleton<INavigationService>(sp =>
    {
        var localizer = sp.GetService<ILocalizer>();
        Func<string, string>? translate = localizer is null ? null : localizer.Get;
        Func<string, object?[], string>? format = localizer is null ? null : (key, args) => localizer.Format(key, args);
        return new NavigationService(CreatePageFactory(sp), translate, format);
    });
        services.AddSingleton<ShellViewModel>();

        // 深链路由（ARC-05）：解析 ckan:// 的是 Platform 层的 CkanUri，视图模型层不引用 Platform，
        // 所以这里把「按导航键取页面单例」的委托喂进去，路由器只管落点。
        services.AddSingleton(sp => new DeepLinkRouter(
            sp.GetRequiredService<INavigationService>(),
            sp.GetService<IInstanceService>(),
            key => PageTypes.TryGetValue(key, out var pageType) ? sp.GetService(pageType) : null));

        // 首次启动指引（02 篇 §1 Ⅱ-⑪）：安装目录扫描复用现有的 Steam 库定位器，
        // 免得 libraryfolders.vdf 的三种写法要维护两遍。
        // 代理必须是单例：它在构造时抓一次「系统默认值」用于回退，多实例会各自快照、互相覆盖。
        services.AddSingleton<IKspInstallScanner>(_ => new KspInstallScanner(new Services.Platform.SteamLibraryLocator()));
        services.AddSingleton<IProxyConfigurator>(_ => new ProxyConfigurator());
        // 首启指引：自动弹出与「设置 → 首次启动指引」重入共用同一个启动器，装配只有一份。
        services.AddSingleton<IFirstRunWizardLauncher>(sp => new Onboarding.FirstRunWizardLauncher(
            ownerProvider,
            sp.GetRequiredService<IInstanceService>(),
            sp.GetRequiredService<IKspInstallScanner>(),
            sp.GetRequiredService<IProxyConfigurator>(),
            sp.GetRequiredService<ICompatibleVersionsService>(),
            sp.GetRequiredService<IRepositoryService>(),
            sp.GetRequiredService<IChangeSetService>(),
            sp.GetRequiredService<IInstallerService>(),
            sp.GetRequiredService<IUiSettingsStore>(),
            sp.GetRequiredService<Func<string, string>>()));

        // 托盘（12 篇 F-51 / UI-26）：VM 只描述菜单，图标与原生菜单由 App 层的 TrayHost 装。
        services.AddSingleton<TrayHostActions>(sp => new TrayHostActions(
            ownerProvider,
            () => SettingsPaths.DefaultConfigDirectory,
            () => null,
            () => sp.GetService<IInstanceService>()?.Current?.GameDir,
            sp.GetService<IRepositoryService>(),
            () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0),
            sp.GetRequiredService<IDispatcher>(),
            sp.GetService<ISettingsService>()));
        services.AddSingleton<TrayMenuViewModel>(sp => new TrayMenuViewModel(sp.GetRequiredService<TrayHostActions>()));
        services.AddSingleton<TrayHost>(sp => new TrayHost(
            Application.Current!,
            sp.GetRequiredService<TrayMenuViewModel>(),
            sp.GetRequiredService<IUiSettingsStore>(),
            sp.GetRequiredService<IDispatcher>()));

        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true,
        });
    }

    /// <summary>剪贴板写入委托：没有可用 owner 或剪贴板时抛 <see cref="NotSupportedException"/>，由 VM 转成提示文案。</summary>
    private static Func<string, Task> CreateClipboardWriter(Func<Window?> ownerProvider)
    {
        return async text =>
        {
            var window = ownerProvider();
            var clipboard = window is null ? null : TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard is null)
            {
                throw new NotSupportedException("没有可用的剪贴板通道");
            }

            await clipboard.SetTextAsync(text);
        };
    }

    // 页面 key ↔ 视图模型类型。新增页面时只改这张表与上方的单例登记。
    public static readonly Dictionary<string, Type> PageTypes = new(StringComparer.Ordinal)
    {
        ["mods"] = typeof(ModsPageViewModel),
        ["changes"] = typeof(ChangesPageViewModel),
        ["content"] = typeof(ContentPageViewModel),
        ["repos"] = typeof(ReposPageViewModel),
        ["modpacks"] = typeof(ModPacksPageViewModel),
        ["modpack-editor"] = typeof(ModPackEditorPageViewModel),
        ["instances"] = typeof(InstancesPageViewModel),
        ["unmanaged"] = typeof(UnmanagedPageViewModel),
        ["cache"] = typeof(CachePageViewModel),
        ["downloadstats"] = typeof(DownloadStatsPageViewModel),
        ["playtime"] = typeof(PlayTimePageViewModel),
        ["dedup"] = typeof(DedupPageViewModel),
        ["installhistory"] = typeof(InstallHistoryPageViewModel),
        ["plugins"] = typeof(PluginsPageViewModel),
        ["logs"] = typeof(LogPageViewModel),
        ["settings"] = typeof(SettingsPageViewModel),
        ["home"] = typeof(HomePageViewModel),
    };

    private static Func<string, object?> CreatePageFactory(IServiceProvider services)
    {
        // 插件页实例按导航键缓存：内置页是 DI 单例（同一个实例反复返回），插件页否则每导航一次
        // 就构造一个新实例。外壳把页面视图常驻舞台上（换页只切可见性），新实例会让旧视图和它的
        // 轮询定时器永远留在可视树里（插件页视图的 StartPolling/StopPolling 挂在挂载/卸载事件上）。
        var pluginPages = services.GetRequiredService<Plugins.PluginPageCache>();
        if (services.GetService<IPluginService>() is { } plugins)
        {
            // 插件停用/卸载后它的页签不再出现在目录里：先把实例摘掉并 Dispose（停定时器、退订阅），
            // 视图那一侧由 MainWindow 按导航键撤（见 RebuildNavigation → PruneRemovedPageViews），
            // 否则外层还攥着视图 ⇒ 插件程序集永远卸载不掉。
            // 保证跨线程安全：若在后台线程触发（如 Bootstrapper 或异步激活/停用），封送到 UI 线程释放，
            // 避免页面 Dispose 触发的 UI 操作或控件解绑跨线程冲突。
            plugins.Changed += (_, _) =>
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    ReleaseOrphanedPluginPages(pluginPages, plugins);
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ReleaseOrphanedPluginPages(pluginPages, plugins));
                }
            };
        }

        // 内置页签直接查表；查不到再问插件（12 篇 F-17）：插件声明的页签由 App 层反射构造，
        // 契约层与外壳都不需要知道插件程序集在哪。
        return key => PageTypes.TryGetValue(key, out var type)
            ? services.GetService(type)
            : pluginPages.GetOrCreate(key, () => Plugins.PluginPageFactory.CreateFor(services, key, PageTypes.Keys));
    }

    /// <summary>按当前激活表释放已经不在目录里的插件页实例（插件代码可能抛，这里兜底）。</summary>
    private static void ReleaseOrphanedPluginPages(Plugins.PluginPageCache cache, IPluginService plugins)
    {
        try
        {
            var live = PluginPageCatalog
                .Collect(plugins.Active, PageTypes.Keys)
                .Select(entry => entry.Key);
            cache.RetainOnly(live);
        }
        catch (Exception ex)
        {
            Log.Warn("释放已下线的插件页时发生异常（已忽略）。", ex);
        }
    }

    /// <summary>
    /// 把 URL 交给系统默认浏览器：优先用 owner 窗口的 Avalonia Launcher。
    /// 拿不到窗口（启动早期／无窗口宿主）时返回 false，由 VM 提示用户手动访问，而不是抛异常。
    /// </summary>
    private static Func<string, Task<bool>> CreateUrlLauncher(Func<Window?> ownerProvider) => async url =>
    {
        if (ownerProvider() is not { } owner || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return await owner.Launcher.LaunchUriAsync(uri).ConfigureAwait(true);
    };

    /// <summary>
    /// 弹出一个对话框窗口：有可见 owner 时严格模态，否则降级非模态并等它关闭
    /// （与 <see cref="Dialogs.DialogPresenter"/> 的口径一致，避免无窗口宿主里 ShowDialog 抛异常）。
    /// </summary>
    private static async Task ShowDialogAsync(Window window, Func<Window?> ownerProvider)
    {
        ArgumentNullException.ThrowIfNull(window);

        var owner = ownerProvider();
        if (owner is not null && owner.IsVisible)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
            return;
        }

        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        await closed.Task.ConfigureAwait(true);
    }
}
