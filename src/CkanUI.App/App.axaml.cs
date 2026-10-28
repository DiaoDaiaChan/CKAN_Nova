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
using CkanUI.ViewModels;
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
    private bool _shutdownDone;

    /// <summary>当前应用实例（供单实例转发把外部参数送进窗口）。</summary>
    public static App? Instance { get; private set; }

    /// <inheritdoc />
    public override void Initialize()
    {
        Instance = this;
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ServiceRegistration.Build(() => desktop.MainWindow);

            // 语言包必须在窗口显示前灌进资源字典，否则首帧会看到裸 key（i18n-guide §6.2）。
            var localizer = _services.GetRequiredService<ILocalizer>();
            _localization = new LocalizationHost(localizer, _services.GetRequiredService<IDispatcher>(), this);
            _localization.Install();

            // 主题与语言一样必须在首帧前生效，否则会看到默认变体闪一下（20 篇 P-08）。
            // 系统高对比度是最高优先级（20 篇 §15 / P-02）：探针直读平台设置，开了就压过用户选的主题与强调色。
            _theme = new ThemeHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this,
                () => ThemeHost.IsSystemHighContrast(this));
            _theme.Install();

            // 密度与主题同理：首帧前落地，否则会看到默认行高跳一下（20 篇 P-08、THM-06）。
            _density = new DensityHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _density.Install();

            // 字号缩放紧随密度（20 篇 P-09）：同样首帧前落地，避免第一帧用基准字号再跳一档。
            _fonts = new Theming.FontScaleHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _fonts.Install();

            // 强调色与圆角宿主：首帧前落地，初始化 12 预设强调色与圆角缩放 token（20 篇 P-03、P-05）
            _accent = new Theming.AccentHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this,
                _services.GetService<IInstanceService>());
            _accent.Install();

            // 高对比度开关一变，主题变体与强调色都要重算（强调色依赖主题变体，故排在其后订阅）。
            _theme.ForcedStateChanged += _ => _accent?.Refresh();

            // 动效强度（20 篇 P-10）：页面/控件过渡时长各自落在 token 上，同样首帧前落地。
            _motion = new Theming.MotionHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _motion.Install();

            _radius = new Theming.RadiusHost(
                _services.GetRequiredService<IUiSettingsStore>(),
                _services.GetRequiredService<IDispatcher>(),
                this);
            _radius.Install();

            var shell = _services.GetRequiredService<ShellViewModel>();
            var window = new MainWindow { DataContext = shell };
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
                _services.GetService<IInstanceService>());
            _background.Install();

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

            // 托盘（12 篇 F-51）：窗口建好后再装图标；「最小化到托盘 / 关闭到托盘」两个开关的生效点就在这里。
            var trayMenu = _services.GetRequiredService<TrayMenuViewModel>();
            _tray = _services.GetRequiredService<TrayHost>();
            _tray.Install();

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
            _ = StartAsync(shell);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(ShellViewModel shell)
    {
        var services = _services!;
        var dispatcher = services.GetRequiredService<IDispatcher>();
        var errors = services.GetRequiredService<IErrorReporter>();
        try
        {
            var result = await services.GetRequiredService<IAppBootstrapper>()
                                       .StartupAsync(CancellationToken.None)
                                       .ConfigureAwait(false);
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
    /// 处理第二个进程转发过来的命令行（ARC-05）：把主窗口拉到前台，
    /// 并解析 ckan:// 命令。深链的实际跳转属 M1，这里只记录。
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

            foreach (var arg in args)
            {
                if (CkanUri.TryParse(arg, out var command))
                {
                    Log.Info($"解析到深链命令：{command.Kind} = {command.Value}");
                }
            }
        });
    }
}
