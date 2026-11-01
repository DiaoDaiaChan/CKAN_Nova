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
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Commands;
using CkanUI.Services.Logs;
using CkanUI.ViewModels;
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
    private TrayHost? _tray;
    private ILogStore? _logStore;
    private LogPipeline? _logPipeline;
    private LogExceptionRouter? _logRouter;
    private bool _shutdownDone;

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

            var window = new MainWindow { DataContext = shell };
            Mark("main-window-new");

            // 命令面板（UI-27）：键位与「命令 Id → 动作」的映射留在外壳，这里只交 VM、目录与动作落点。
            window.AttachCommandPalette(
                _services.GetRequiredService<CkanUI.ViewModels.CommandPalette.CommandPaletteViewModel>(),
                _services.GetRequiredService<ICommandCatalog>(),
                _services.GetRequiredService<Func<string, string>>(),
                HandlePaletteAction);

            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
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
            };

            Mark("assembly-done");
            _ = StartAsync(shell, desktop.Args ?? Array.Empty<string>());
        }

        base.OnFrameworkInitializationCompleted();
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

    private async Task StartAsync(ShellViewModel shell, IReadOnlyList<string>? launchArgs = null)
    {
        // 这段同步序言会挡住窗口显示 —— 它跑在 Avalonia 调用 Show() 之前，于是这里多花的时间
        // 直接算进「从启动到看见窗口」。
        TraceStartup($"[启动计时] start-async-sync-begin={_startupWatch.ElapsedMilliseconds}ms");
        var services = _services!;
        var dispatcher = services.GetRequiredService<IDispatcher>();
        var errors = services.GetRequiredService<IErrorReporter>();
        try
        {
            TraceStartup($"[启动计时] bootstrap-called={_startupWatch.ElapsedMilliseconds}ms");
            var result = await services.GetRequiredService<IAppBootstrapper>()
                                       .StartupAsync(CancellationToken.None)
                                       .ConfigureAwait(false);
            TraceStartup($"[启动计时] bootstrap-done={_startupWatch.ElapsedMilliseconds}ms");
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
        }
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
            _services.Dispose();
        }
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
