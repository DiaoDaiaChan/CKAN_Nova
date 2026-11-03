using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CkanUI.App.Localization;
using CkanUI.App.Theming;
using CkanUI.App.Tray;
using CkanUI.Localization;
using CkanUI.Platform.SingleInstance;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Commands;
using CkanUI.Services.Logs;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Shell;
using log4net;
using Microsoft.Extensions.DependencyInjection;

namespace CkanUI.App;

/// <summary>应用组装根（02 篇 §1 / §2、ARC-01）。</summary>
public sealed partial class App : Application
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(App));

    private ServiceProvider? _services;

    /// <summary>
    /// 当前应用的服务容器（应用组装根之外唯一的读取口子）。
    /// </summary>
    /// <remarks>
    /// 只在「外壳要按导航键临时造一个插件页」这类场景用：底栏 AI 面板不是导航页，容器里的页面表认不到它，
    /// 只能由视图层现解析一次。未启动完成或已关闭时为 <c>null</c>，调用方必须自己兜底。
    /// </remarks>
    public static IServiceProvider? Services { get; private set; }
    private LocalizationHost? _localization;
    private ThemeHost? _theme;
    private DensityHost? _density;
    private Theming.FontScaleHost? _fonts;
    private Theming.AccentHost? _accent;
    private Theming.MotionHost? _motion;
    private Theming.RadiusHost? _radius;
    private Theming.BackdropHost? _backdrop;
    private Theming.BackgroundHost? _background;
    private Plugins.PluginToolItemHost? _pluginTools;
    private CancellationTokenSource? _pluginBootstrapCts;
    private TrayHost? _tray;
    private ILogStore? _logStore;
    private IKspLogTailService? _kspLogTail;
    private IInstanceService? _kspLogInstanceSource;
    private LogPipeline? _logPipeline;
    private LogExceptionRouter? _logRouter;
    private bool _shutdownDone;
    private MainWindow? _mainWindow;
    private bool _firstRunWizardShown;

    /// <summary>启动计时：从 XAML 加载前到首个窗口可交互，一次启动只打一行总账与各阶段耗时。</summary>
    /// <remarks>
    /// 真机启动实测 2.4-2.7 秒、其中约 1.5 秒 CPU、其余在等 I/O，光看一个总数没法判断该动哪一段，
    /// 所以把组装顺序上的几个必然经过的点插上标记。开销只有几次 Stopwatch 读数。
    /// </remarks>
    private readonly System.Diagnostics.Stopwatch _startupWatch = new();

    /// <summary>启动阶段标记的落点：进 log4net，另外在 CKANUI_STARTUP_TRACE 指向文件时追加一行。</summary>
    /// <remarks>
    /// 只写 log4net 不好事后核对 —— 落盘目录跟着实例走、被并行测试进程混写，抓一次要翻半天。
    /// 环境变量是给测量脚本用的显式开关，不设就只多一次字符串拼接。
    /// </remarks>
    /// <summary>启动预建延后多久：太早会和首帧渲染抢队列，1.2s 时窗口已经稳定可交互。</summary>
    private const int PrefetchDelayMilliseconds = 1200;

    private static void TraceStartup(string line)
    {
        Log.Info(line);
        var path = Environment.GetEnvironmentVariable("CKANUI_STARTUP_TRACE");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 追写失败只是丢一行诊断，不影响启动。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    /// <summary>当前应用实例（供单实例转发把外部参数送进窗口）。</summary>
    public static App? Instance { get; private set; }

    /// <inheritdoc />
    public override void Initialize()
    {
        Instance = this;
        _startupWatch.Restart();
        AvaloniaXamlLoader.Load(this);
        TraceStartup($"[启动计时] app-xaml={_startupWatch.ElapsedMilliseconds}ms");
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var lastMarkMs = _startupWatch.ElapsedMilliseconds;
            void Mark(string phase)
            {
                var now = _startupWatch.ElapsedMilliseconds;
                TraceStartup($"[启动计时] {phase}={now}ms (+{now - lastMarkMs}ms)");
                lastMarkMs = now;
            }

            _services = ServiceRegistration.Build(() => desktop.MainWindow);
            Services = _services;
            Mark("di-build");

            // 实例服务解析 = 初始化 Core 的实例管理（读 config.json + 探测实例目录），实测 140ms 纯磁盘 I/O。
            // 它到强调色宿主才被用到，中间还排着日志管道、语言包、主题、密度、字号几段 UI 线程工作，
            // 所以在后台线程先把它造好，主线程继续往下走，用到时直接拿成品（单例，容器并发解析安全）。
            // 注意这不等于「挪到首帧之后」—— 那样这 140ms 会原样搬到 ShellViewModel 构造里，一秒都不省。
            Task<IInstanceService?>? instanceWarmup = Task.Run(() => _services.GetService<IInstanceService>());

            // 日志域（M8 / 14 篇 §3.2）：Program 里已跑过 LoggingSetup.Initialize()，这里把缓冲挂到 log4net 仓库。
            // 顺序不能反 —— Install() 会把仓库标记为 Configured，抢在 LoggingSetup 之前就会跳过 log4net.xml。
            // 文件 appender 建不出来只是降级（缓冲照常工作），绝不能因为写不了盘就不启动。
            InstallLogPipeline();
            Mark("log-pipeline");

            // 语言包必须在窗口显示前灌进资源字典，否则首帧会看到裸 key（i18n-guide §6.2）。
            var localizer = _services.GetRequiredService<ILocalizer>();
            _localization = new LocalizationHost(localizer, _services.GetRequiredService<IDispatcher>(), this);
            _localization.Install();
            Mark("localization");

            // 主题与语言一样必须在首帧前生效，否则会看到默认变体闪一下（20 篇 P-08）。
            // 系统高对比度是最高优先级（20 篇 §15 / P-02）：探针直读平台设置，开了就压过用户选的主题与强调色。
            _theme = new ThemeHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this,
                () => ThemeHost.IsSystemHighContrast(this));
            _theme.Install();
            Mark("theme");

            // 密度与主题同理：首帧前落地，否则会看到默认行高跳一下（20 篇 P-08、THM-06）。
            _density = new DensityHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _density.Install();
            Mark("density");

            // 字号缩放紧随密度（20 篇 P-09）：同样首帧前落地，避免第一帧用基准字号再跳一档。
            _fonts = new Theming.FontScaleHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _fonts.Install();
            Mark("fonts");

            // 强调色与圆角宿主：首帧前落地，初始化 12 预设强调色与圆角缩放 token（20 篇 P-03、P-05）
            var instanceService = instanceWarmup.GetAwaiter().GetResult();
            Mark("instance-service-resolved");
            _accent = new Theming.AccentHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this,
                instanceService);
            _accent.Install();
            Mark("accent");

            // 高对比度开关一变，主题变体与强调色都要重算（强调色依赖主题变体，故排在其后订阅）。
            _theme.ForcedStateChanged += _ => _accent?.Refresh();

            // 动效强度（20 篇 P-10）：页面/控件过渡时长各自落在 token 上，同样首帧前落地。
            _motion = new Theming.MotionHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _motion.Install();
            Mark("motion");

            _radius = new Theming.RadiusHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _radius.Install();
            Mark("radius");

            var shell = _services.GetRequiredService<ShellViewModel>();
            Mark("shell-vm");

            // 日志抽屉（UI-LOG-01）：外壳负责挂载位置与跳转，抽屉自己只管缓冲与徽标。
            // 事件而不是直接依赖导航服务 —— 抽屉是被外壳实例化的，不该反过来认识外壳的导航实现。
            shell.LogDrawer = _services.GetRequiredService<CkanUI.ViewModels.Pages.Logs.LogDrawerViewModel>();
            shell.LogDrawer.OpenPageRequested += (_, _) => shell.NavigateCommand.Execute("logs");

            // 游戏日志（14 篇 §3.2 第五路）：跟随当前实例的 KSP.log。页面没打开也要跟着——
            // 玩家往往是先看到游戏报错，再回来翻日志，那时错误已经写进文件了。
            AttachKspLogTail(instanceService);

            var window = new MainWindow { DataContext = shell };
            _mainWindow = window;
            Mark("main-window-new");

            // 命令面板（UI-27）：键位与「命令 Id → 动作」的映射留在外壳，这里只交 VM、目录与动作落点。
            window.AttachCommandPalette(
                _services.GetRequiredService<CkanUI.ViewModels.CommandPalette.CommandPaletteViewModel>(),
                _services.GetRequiredService<ICommandCatalog>(),
                _services.GetRequiredService<Func<string, string>>(),
                HandlePaletteAction);

            // 首次启动高亮指引（Onboarding Tour）：遮罩高亮引导用户使用软件。
            window.AttachOnboardingTour(
                _services.GetRequiredService<CkanUI.ViewModels.Onboarding.OnboardingTourViewModel>());

            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 真机换页计时（CKANUI_NAV_TRACE=<文件>）：不用时只是一个环境变量判断。
            Diagnostics.NavigationProbe.Attach(window);
            desktop.Exit += (_, _) => Shutdown();

            _backdrop = new Theming.BackdropHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                desktop);
            _backdrop.Install();

            // 背景层（20 篇 P-06 / P-07）：需要窗口先建好才能按名字取 BackgroundLayer 等控件，
            // 所以排在这里；实例切换会改变「按实例覆盖」的解析结果，故一并传实例服务。
            _background = new Theming.BackgroundHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this,
                window,
                instanceService);
            _background.Install();
            Mark("backdrop+background");

            // 资源释放的 L0 参与方要拿到背景宿主才能放位图（18 篇 §3）：容器解析时不带窗口参数，只能回填。
            if (_services.GetService<Lifecycle.UiViewResourceParticipant>() is { } viewParticipant)
            {
                viewParticipant.Background = _background;
            }

            // 插件工具项（12 篇 F-17）：插件可以往宿主既有页面的工具条插按钮。
            // 页面实例由 DI 单例提供，外壳拿到的就是同一批实例，所以按接口收齐即可。
            _pluginTools = new Plugins.PluginToolItemHost(
                _services.GetRequiredService<IPluginService>(),
                _services.GetServices<CkanUI.ViewModels.Navigation.IPluginToolHost>(),
                _services.GetRequiredService<IDispatcher>());
            _pluginTools.Install();
            Mark("plugin-tools");

            // 插件启动期引导（12 篇 F-17）：扫内置 plugins\ 与用户目录，恢复上次显式激活态。
            // 刻意放后台：插件目录 IO 与第三方 dll 加载一旦卡在首帧前，代价是整窗白屏
            // （982e93d 的教训：启动期同步重活能把帧率从 60 打到 3）。插件页签由外壳订阅
            // PluginService.Changed 后差分补进导航栏，这里不 await、不阻塞窗口显示。
            _pluginBootstrapCts = new CancellationTokenSource();
            StartPluginBootstrap(_pluginBootstrapCts.Token);
            Mark("plugin-bootstrap-scheduled");

            // 托盘（12 篇 F-51）：窗口建好后再装图标；「最小化到托盘 / 关闭到托盘」两个开关的生效点就在这里。
            var trayMenu = _services.GetRequiredService<TrayMenuViewModel>();
            _tray = _services.GetRequiredService<TrayHost>();
            _tray.Install();
            Mark("tray");

            window.Closing += (_, e) =>
            {
                if (!trayMenu.HideOnClose) return;
                e.Cancel = true;
                window.Hide();
            };

            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty
                    && window.WindowState == WindowState.Minimized
                    && trayMenu.HideOnMinimize)
                {
                    window.Hide();
                }
            };

            // 启动序列放后台跑：UI 线程必须保持空闲，否则 02 篇 §1 里的网络/磁盘步骤会冻住窗口。
            // 首个进程用 ckan:// 直接启动时同样要执行深链，所以把启动参数一并交给启动序列，在它收尾后落点。
            // 首帧布局：Opened 只表示窗口已映射，真正把可视树量完排完发生在之后的布局轮，
            // 所以用 Loaded 优先级的 Post 兜住这一段，把「组装」和「首帧布局」分开计数。
            window.Opened += (_, _) =>
            {
                TraceStartup($"[启动计时] window-opened={_startupWatch.ElapsedMilliseconds}ms");
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => TraceStartup($"[启动计时] first-layout={_startupWatch.ElapsedMilliseconds}ms"),
                    Avalonia.Threading.DispatcherPriority.Loaded);
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => TraceStartup($"[启动计时] first-paint={_startupWatch.ElapsedMilliseconds}ms"),
                    Avalonia.Threading.DispatcherPriority.Background);

                // 首帧之后把主页面视图预建掉：模组页是整个应用最重的视图（DataGrid 列模板 +
                // 单元格模板，实测首次构建 195ms，见 docs/ops/perf-baseline.md §21.2）。
                // 用户从主页点「模组」时才建，这 195ms 就落在第一次点击上；放到首帧后的
                // 后台优先级里做，点击时 ViewLocator 直接命中缓存。视觉、交互顺序都不变。
                Avalonia.Threading.DispatcherTimer.RunOnce(
                    PrefetchFirstNavigationView,
                    TimeSpan.FromMilliseconds(PrefetchDelayMilliseconds),
                    Avalonia.Threading.DispatcherPriority.ApplicationIdle);
            };

            Mark("assembly-done");
            _ = StartAsync(shell, desktop.Args ?? Array.Empty<string>());
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 首帧之后预建主页面视图（见 docs/ops/perf-baseline.md §25）：只构建控件树并交给
    /// <see cref="ViewLocator"/> 的静态缓存，不挂到窗口、不触发入场动效，页面呈现方式不变。
    /// 任何失败都只记日志——这是纯优化路径，绝不能影响导航。
    /// </summary>
    private void PrefetchFirstNavigationView()
    {
        try
        {
            if (_services?.GetService(typeof(ModsPageViewModel)) is not ModsPageViewModel mods) return;
            if (ViewLocator.HasCachedView(mods)) return;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            _ = new ViewLocator().Build(mods);
            TraceStartup($"[启动计时] prefetch-mods-view={_startupWatch.ElapsedMilliseconds}ms (+{watch.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Log.Warn($"主页面视图预建失败（已忽略）：{ex.Message}");
        }
    }

    /// <summary>挂日志缓冲与未捕获异常路由（14 篇 §3.2）：失败只降级，不阻断启动。</summary>
    private void InstallLogPipeline()
    {
        var services = _services!;
        _logStore = services.GetRequiredService<ILogStore>();
        _logPipeline = new LogPipeline(_logStore);
        var install = _logPipeline.Install();
        if (!install.IsSuccess)
        {
            Log.Warn($"日志缓冲未挂载：{install.Message}");
        }
        else if (_logPipeline.Warnings.Count > 0)
        {
            Log.Warn($"日志落盘降级：{string.Join("；", _logPipeline.Warnings)}");
        }

        // AppDomain / TaskScheduler 由路由器自己挂；UI 线程的那一路只能在 App 层挂（服务层不引用 Avalonia）。
        _logRouter = new LogExceptionRouter(_logStore);
        _logRouter.Attach();
        // 全限定：App 层同时引用了 CkanUI.Services.Abstractions.IDispatcher（服务层自有抽象），
        // 直接 using Avalonia.Threading 会让两个 IDispatcher 撞名（CS0104）。
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    /// <summary>UI 线程未捕获异常也进看板（14 篇 §3.2 ③）：只记录，是否优雅退出仍由 Avalonia 决定。</summary>
    private void OnDispatcherUnhandledException(
        object? sender,
        Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logRouter?.Report(e.Exception, "ui-dispatcher");
    }

    /// <summary>
    /// 启动期预热元数据语言包（不阻塞首帧）。
    /// <para>
    /// 为什么要预热：<c>ModDetailsViewModel</c> 的摘要/描述只读 <c>IModTextService.Provider</c>，而 Provider 是按需构建的——
    /// 以前只有 i18n 页面会调 <c>ReloadPacksAsync</c>，用户不点开那一页，模组详情永远显示英文原文。
    /// </para>
    /// <para>
    /// 失败语义：缺包、目录无权访问都只降级为英文（<c>ReloadPacksAsync</c> 内部已 catch 并记日志），
    /// 这里再兜一层是为了不让任何异常冒到启动序列里。
    /// </para>
    /// </summary>
    private static async Task WarmUpModTextAsync(IServiceProvider services)
    {
        try
        {
            var modText = services.GetRequiredService<CkanUI.ModText.IModTextService>();
            await modText.ReloadPacksAsync().ConfigureAwait(false);
            Log.Info($"[启动] 元数据语言包预热完成：{modText.CurrentLanguage} / {modText.TotalEntries} 条条目（详情面板译文可用）。");
        }
        catch (Exception ex)
        {
            Log.Warn($"[启动] 元数据语言包预热失败，详情面板按英文原文显示：{ex.Message}");
        }
    }

    private async Task StartAsync(ShellViewModel shell, IReadOnlyList<string>? launchArgs = null)
    {
        // 这段同步序言会挡住窗口显示 —— 它跑在 Avalonia 调用 Show() 之前，于是这里多花的时间
        // 直接算进「从启动到看见窗口」。
        TraceStartup($"[启动计时] start-async-sync-begin={_startupWatch.ElapsedMilliseconds}ms");
        var services = _services!;
        var dispatcher = services.GetRequiredService<IDispatcher>();
        var errors = services.GetRequiredService<IErrorReporter>();
        var startupReady = false;
        try
        {
            ApplySavedProxy(services);
            TraceStartup($"[启动计时] bootstrap-called={_startupWatch.ElapsedMilliseconds}ms");
            var result = await services.GetRequiredService<IAppBootstrapper>()
                                       .StartupAsync(CancellationToken.None)
                                       .ConfigureAwait(false);
            TraceStartup($"[启动计时] bootstrap-done={_startupWatch.ElapsedMilliseconds}ms");
            startupReady = result.IsSuccess;

            // 语言包预热：详情面板的译文只读 IModTextService.Provider，而 Provider 只在 ReloadPacksAsync
            // 之后才有内容——此前全仓唯一触发点是 i18n 页面，等于用户不点开那一页就永远看到英文。
            // 后台预热不挡窗口显示；失败只降级为英文原文，不影响启动（17 篇 §7「不阻塞首帧」）。
            _ = WarmUpModTextAsync(services);
            var localizer = services.GetRequiredService<ILocalizer>();
            var status = result.IsSuccess
                ? localizer.Get("Ui_Common_Status_Ready")
                : localizer.Format("Ui_Common_Status_StartupIncomplete", result.Message);
            if (!result.IsSuccess) Log.Warn($"启动未完成：{result.Message} {result.Detail}");
            await dispatcher.InvokeAsync(() => shell.StatusText = status).ConfigureAwait(false);

        }
        catch (Exception ex)
        {
            Log.Error("启动序列抛出未捕获异常。", ex);
            errors.Report(ex, "启动序列", "CKAN UI 启动失败。");
            var failedText = services.GetRequiredService<ILocalizer>().Get("Ui_Common_Status_StartupFailed");
            await dispatcher.InvokeAsync(() => shell.StatusText = failedText).ConfigureAwait(false);
        }
        finally
        {
            // 深链放在启动序列之后：实例列表/仓库索引都就位了才谈得上定位（ARC-05）。
            if (launchArgs is { Count: > 0 })
            {
                try
                {
                    await DispatchDeepLinksAsync(launchArgs).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warn("启动期深链处理失败。", ex);
                }
            }

            // 首屏兜底：启动序列失败时（实例读不出来、注册表损坏）导航服务一次都没跳转过，
            // 内容区会一直空白，用户只能靠点侧栏自救。只要还没落过页面，就落到侧栏第一个入口。
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (shell.CurrentPage is null && shell.RailItems.Count > 0)
                    {
                        shell.NavigateCommand.Execute(shell.RailItems[0].Key);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn("首屏兜底导航失败。", ex);
            }

            // 引导放在首屏导航之后：先让页面入场动效起跑，再由引导按落位时间接手。
            if (startupReady)
            {
                try
                {
                    await dispatcher.InvokeAsync(() => { _ = MaybeShowStartupGuidesAsync(); }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warn("启动指引序列未能启动。", ex);
                }
            }
        }
    }

    /// <summary>
    /// 把磁盘上的下载代理装到进程上（首启指引第 3 步保存的也是同一份配置）。
    /// </summary>
    /// <remarks>
    /// 必须赶在任何一次仓库刷新之前：Core 的下载器走 WebClient，只认 .NET 的全局代理，
    /// 没有「按请求指定代理」的入口。晚一步，首启拉索引就还是直连。
    /// </remarks>
    private void ApplySavedProxy(IServiceProvider services)
    {
        try
        {
            var configurator = services.GetService<IProxyConfigurator>();
            var uiSettings = services.GetService<IUiSettingsStore>()?.Current;
            if (configurator is null || uiSettings is null)
            {
                return;
            }

            var mode = uiSettings.ProxyMode switch
            {
                "direct" => ProxyMode.Direct,
                "manual" => ProxyMode.Manual,
                _ => ProxyMode.System,
            };
            configurator.Apply(new ProxySettings(mode, uiSettings.ProxyServer, uiSettings.ProxyBypass));
        }
        catch (Exception ex)
        {
            // 代理配置坏了只该降级成「直连也能用」，不能拖垮启动。
            Log.Warn("应用已保存的下载代理失败。", ex);
        }
    }

    /// <summary>启动动效落位后，再决定这次启动要不要向导、要不要界面导览——两段全屏浮层不连播。</summary>
    /// <remarks>
    /// <para>
    /// 冲突来自三件事挤在同一个窗口：整页入场（<c>CkanMotionDurationPage</c> + 卡片错峰）还没落位，首次启动指引就把它整块盖住；
    /// 向导一关，界面导览又立刻接上，用户看到的是「两层遮罩连着闪」。这里按「等入场落位 → 向导 → 不再接导览」排序。
    /// </para>
    /// <para>
    /// 向导被关掉（未完成）时同样不接着播导览：用户刚结束一套引导，再糊一层只会更像打扰。
    /// 重看入口保留在 F1、命令面板与「设置 → 首次启动指引」，完成页也会写明 F1。
    /// </para>
    /// </remarks>
    private async Task MaybeShowStartupGuidesAsync()
    {
        if (_services is null || _mainWindow is not { } window)
        {
            return;
        }

        try
        {
            if (!await WaitUntilVisibleAsync(window).ConfigureAwait(true))
            {
                Log.Info("启动指引：主窗口一直不可见，本次跳过。");
                return;
            }

            // 等入场动效落位再盖遮罩：Windows 关闭动效时这里为 0，不会平白多等。
            await Task.Delay(EntranceSettleDelay()).ConfigureAwait(true);
            if (!window.IsVisible)
            {
                return;
            }

            var settings = _services.GetRequiredService<IUiSettingsStore>().Current;
            var instances = _services.GetRequiredService<IInstanceService>();
            var wizardDue = !_firstRunWizardShown
                            && !settings.FirstRunCompleted
                            && instances.Instances.Count == 0;

            if (wizardDue)
            {
                if (window is MainWindow { IsOnboardingActive: true })
                {
                    Log.Info("启动指引：界面导览正在播放，本次跳过首次启动指引。");
                    return;
                }

                _firstRunWizardShown = true;
                Log.Info("启动指引：打开首次启动指引；本次启动不再自动打开界面导览。");
                await _services.GetRequiredService<IFirstRunWizardLauncher>()
                               .LaunchAsync()
                               .ConfigureAwait(true);
                return;
            }

            var tourDue = !settings.HasSeenOnboardingTour && instances.Instances.Count > 0;
            if (!tourDue || window is not MainWindow main)
            {
                return;
            }

            // 启动期间用户可能已经切走：导览是全屏遮罩，只在窗口真在前台、且没有别的导览在播时开。
            await Task.Delay(TourDelay).ConfigureAwait(true);
            if (!main.IsVisible || !main.IsActive || main.IsOnboardingActive)
            {
                Log.Info("启动指引：窗口不在前台或导览已在播放，本次不自动打开界面导览。");
                return;
            }

            main.ShowOnboardingGuide();
        }
        catch (Exception ex)
        {
            Log.Warn("首次启动指引或高亮导览未能显示。", ex);
        }
    }

    /// <summary>把 KSP.log 跟随器接到当前实例上（实例切换即换文件，缺失时保持轮询等待生成）。</summary>
    private void AttachKspLogTail(IInstanceService? instances)
    {
        if (_services?.GetService<IKspLogTailService>() is not { } tail)
        {
            return;
        }

        _kspLogTail = tail;
        tail.Attach(instances?.Current?.GameDir, instances?.Current?.Name);
        if (instances is not null)
        {
            _kspLogInstanceSource = instances;
            instances.CurrentChanged += OnKspLogInstanceChanged;
        }
    }

    private void OnKspLogInstanceChanged(InstanceInfo? info) => _kspLogTail?.Attach(info?.GameDir, info?.Name);

    /// <summary>界面导览比向导晚一步出现，避开对话框自己的淡入与页面入场尾巴。</summary>
    private static readonly TimeSpan TourDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>等主窗口真正可见；托盘启动/启动即隐藏时不让引导挂在一个看不见的 owner 上。</summary>
    private static async Task<bool> WaitUntilVisibleAsync(Window window)
    {
        if (window.IsVisible)
        {
            return true;
        }

        var visible = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.IsVisibleProperty && window.IsVisible)
            {
                visible.TrySetResult(true);
            }
        }

        window.PropertyChanged += OnChanged;
        try
        {
            var finished = await Task.WhenAny(visible.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(true);
            return finished == visible.Task || window.IsVisible;
        }
        finally
        {
            window.PropertyChanged -= OnChanged;
        }
    }

    /// <summary>入场落位时长：页面动效 token + 卡片错峰尾巴；动效关闭（token=0）时不等。</summary>
    private static TimeSpan EntranceSettleDelay()
    {
        if (Animations.MotionBudget.IsCollapsed)
        {
            return TimeSpan.Zero;
        }

        var page = TimeSpan.FromMilliseconds(320);
        if (Application.Current?.Resources.TryGetResource(MotionHost.PageKey, null, out var raw) == true
            && raw is TimeSpan duration
            && duration > TimeSpan.Zero)
        {
            page = duration;
        }

        return page + TimeSpan.FromMilliseconds(260);
    }

    /// <summary>
    /// 命令面板里 action: 命令的落点（UI-27）。外壳只管「面板自身那四条」，
    /// 其余六条要落到具体能力上：托盘动作、诊断包、更新检查、主题与密度。
    /// 返回 true 表示已处理；未接线的动作在目录注册时就被置为禁用，用户不会看到「按了没反应」。
    /// </summary>
    private bool HandlePaletteAction(string id)
    {
        if (_services is null)
        {
            return false;
        }

        if (id == DefaultCommands.ActionId("open-log-folder"))
        {
            _ = RunTrayActionAsync(() => _services.GetRequiredService<TrayHostActions>().OpenLogFolder());
            return true;
        }

        if (id == DefaultCommands.ActionId("open-instance-folder"))
        {
            _ = RunTrayActionAsync(() => _services.GetRequiredService<TrayHostActions>().OpenInstanceFolder());
            return true;
        }

        if (id == DefaultCommands.ActionId("export-diagnostics"))
        {
            // 诊断包（脱敏 + SHA256 + 出口反向扫描）由日志域实现，直接复用日志页的默认导出命令，
            // 免得命令面板里再走一套旁路逻辑、把脱敏规则绕过。
            _services.GetRequiredService<CkanUI.ViewModels.Pages.Logs.LogPageViewModel>()
                     .ExportDefaultCommand.Execute(null);
            return true;
        }

        if (id == DefaultCommands.ActionId("check-updates"))
        {
            _services.GetRequiredService<CkanUI.ViewModels.Pages.SettingsPageViewModel>()
                     .CheckForUpdatesCommand.Execute(null);
            return true;
        }

        if (id == DefaultCommands.ActionId("toggle-theme"))
        {
            CycleUiSetting(ThemeCycle, settings => settings.Theme, (settings, value) => settings with { Theme = value }, "主题");
            return true;
        }

        if (id == DefaultCommands.ActionId("toggle-density"))
        {
            CycleUiSetting(DensityCycle, settings => settings.Density, (settings, value) => settings with { Density = value }, "密度");
            return true;
        }

        if (id == DefaultCommands.ActionId("open-onboarding-guide"))
        {
            if (_mainWindow is MainWindow mw)
            {
                mw.ShowOnboardingGuide();
                return true;
            }
        }

        return false;
    }

    /// <summary>主题循环：深色 → 浅色 → 跟随系统（高对比度是可达性模式，不进循环，免得误触）。</summary>
    private static readonly string[] ThemeCycle = ["dark", "light", "system"];

    /// <summary>密度循环：标准 → 紧凑 → 宽松。</summary>
    private static readonly string[] DensityCycle = ["standard", "compact", "comfortable"];

    /// <summary>
    /// 把某个 UI 设置推到下一档并落盘。宿主们订阅了 <see cref="IUiSettingsStore.Changed"/>，
    /// 所以这里不用自己去碰主题/密度宿主，也不需要在 UI 线程上额外封送（Save 在调用线程同步完成）。
    /// </summary>
    private void CycleUiSetting(
        string[] cycle,
        Func<UiSettings, string?> read,
        Func<UiSettings, string, UiSettings> write,
        string what)
    {
        if (_services?.GetService<IUiSettingsStore>() is not { } store)
        {
            return;
        }

        var settings = store.Load();
        var current = read(settings)?.Trim();
        var index = Array.FindIndex(cycle, value => string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
        var next = cycle[(index + 1) % cycle.Length];
        var saved = store.Save(write(settings, next));
        if (saved.IsSuccess)
        {
            Log.Info($"{what}已切换为 {next}。");
        }
        else
        {
            Log.Warn($"{what}切换未能保存：{saved.Message} {saved.Detail}");
        }
    }

    /// <summary>托盘动作返回 OperationResult，命令面板不需要回执；失败只进日志，不弹框打断输入。</summary>
    private static async Task RunTrayActionAsync(Func<Task<OperationResult>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Log.Warn($"命令面板动作失败：{result.Message} {result.Detail}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("命令面板动作抛出异常。", ex);
        }
    }

    private void Shutdown()
    {
        if (_shutdownDone || _services is null) return;
        _shutdownDone = true;

        // 先取消插件引导再去跑关闭序列：引导要调第三方插件的 Initialize，
        // 容器拆了它还在回调服务就是 ObjectDisposedException 满地跑。取消是配合式的，这里不等它。
        try
        {
            _pluginBootstrapCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已释放，取消无意义。
        }

        try
        {
            // 关闭序列要跑完（DisposeAll 是必经步骤），但窗口已经在关，最多等 3 秒。
            _services.GetRequiredService<IAppBootstrapper>()
                     .ShutdownAsync(CancellationToken.None)
                     .Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Log.Warn("关闭序列抛出异常。", ex);
        }
        finally
        {
            if (_logRouter is not null)
            {
                Avalonia.Threading.Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
                _logRouter.Dispose();
            }

            _logPipeline?.Dispose();
            _pluginTools?.Dispose();
            _tray?.Dispose();
            _background?.Dispose();
            _backdrop?.Dispose();
            _radius?.Dispose();
            _accent?.Dispose();
            _motion?.Dispose();
            _fonts?.Dispose();
            _density?.Dispose();
            _theme?.Dispose();
            _localization?.Dispose();
            _pluginBootstrapCts?.Dispose();
            _services.Dispose();
            Services = null;
        }
    }

    /// <summary>
    /// 后台跑一次插件引导（12 篇 F-17）：建目录 → 扫描内置与用户插件 → 恢复用户的持久化激活态。
    /// </summary>
    /// <param name="ct">取消令牌；关闭窗口时取消。</param>
    /// <remarks>
    /// 不 await、不观察结果：<see cref="CkanUI.Services.Plugins.PluginBootstrapper"/> 内部对每一步
    /// 单独 try/catch 并记日志，承诺「插件再烂也不让主窗口打不开」。这里只保证两件事——
    /// 不占用首帧，以及关闭时能取消。
    /// </remarks>
    private void StartPluginBootstrap(CancellationToken ct)
    {
        if (_services?.GetService<CkanUI.Services.Plugins.PluginBootstrapper>() is not { } bootstrapper)
        {
            Log.Warn("插件引导未注册，跳过启动期扫描。");
            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await bootstrapper.RunAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 兜底：RunAsync 自称不抛，但第三方插件代码可以在任何地方炸。
                    Log.Warn("插件引导抛出未预期异常（已忽略）。", ex);
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 处理第二个进程转发过来的命令行（ARC-05）：把主窗口拉到前台，并执行 ckan:// 深链。
    /// </summary>
    public static void HandleExternalArguments(IReadOnlyList<string> args)
    {
        Log.Info($"收到外部参数：{string.Join(' ', args)}");
        if (Instance?._services is null) return;

        var dispatcher = Instance._services.GetRequiredService<IDispatcher>();
        dispatcher.Post(() =>
        {
            if (Instance.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
            var window = desktop.MainWindow;
            if (window is null) return;

            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Show();
            window.Activate();

            _ = Instance.DispatchDeepLinksAsync(args);
        });
    }

    /// <summary>
    /// 逐个执行命令行里的 ckan:// 深链（ARC-05）：非深链参数直接忽略，单条失败只记日志、不影响其余。
    /// </summary>
    private async Task DispatchDeepLinksAsync(IReadOnlyList<string> args)
    {
        if (_services is null || args.Count == 0)
        {
            return;
        }

        var router = _services.GetService<DeepLinkRouter>();
        if (router is null)
        {
            Log.Warn("深链路由器未注册，外部参数已忽略。");
            return;
        }

        foreach (var arg in args)
        {
            if (!CkanUri.TryParse(arg, out var command))
            {
                continue;
            }

            try
            {
                var outcome = await router.DispatchAsync(command.Kind, command.Value).ConfigureAwait(true);
                if (outcome.Handled)
                {
                    Log.Info($"深链已执行：{outcome.Summary}");
                }
                else
                {
                    Log.Warn($"深链未落到目标：{command.Kind} = {command.Value}；{outcome.Summary}");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"深链处理抛出异常：{command.Kind} = {command.Value}", ex);
            }
        }
    }
}
