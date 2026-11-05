using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CkanUI.Localization;
using CkanUI.Platform.SingleInstance;
using CkanUI.Services.Logging;
using log4net;

namespace CkanUI.App;

/// <summary>进程入口（ARC-01 / ARC-04 / ARC-05）。</summary>
public static class Program
{
    /// <summary>
    /// 单实例互斥量与命名管道使用同一标识。默认 <c>CkanUI</c>，可用环境变量 <c>CKANUI_APP_ID</c> 覆盖——
    /// 只给诊断/测量用（同一台机器同时跑两个实例），不改变默认单实例行为。
    /// </summary>
    public static readonly string AppId = ReadAppId();

    private static string ReadAppId()
    {
        var configured = Environment.GetEnvironmentVariable("CKANUI_APP_ID");
        return string.IsNullOrWhiteSpace(configured) ? "CkanUI" : configured.Trim();
    }

    private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

    [STAThread]
    public static void Main(string[] args)
    {
        // ①：日志最先就位，否则后面的失败都看不到原因（02 篇 §1）。
        EnsureConfigFileWritable();
        LoggingSetup.Initialize();
        InstallCrashHandlers();
        Log.Info("CKAN UI 启动。");

        // 若默认配置目录不可写（如沙箱环境），重定向到 AppContext.BaseDirectory/config.json
        EnsureConfigFileWritable();

        // ARC-05：第二个实例只做转发，然后把参数交给第一个实例。
        using var guard = SingleInstanceGuard.TryAcquire(AppId);
        if (guard is null)
        {
            if (!SingleInstanceGuard.TryForwardToOwner(AppId, args, TimeSpan.FromSeconds(3)))
            {
                Log.Warn("检测到已有实例，但参数转发失败（对方可能正在启动）。");
            }

            return;
        }

        guard.ArgumentsReceived += App.HandleExternalArguments;
        guard.StartListening();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal("Avalonia 主循环异常退出。", ex);
            throw;
        }
        finally
        {
            Log.Info("进程退出。");
            LogManager.Shutdown();
        }
    }

    /// <summary>ARC-04：三条兜底路径统一记录 + 提示，避免静默崩溃。</summary>
    private static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            Log.Fatal($"未处理异常（IsTerminating={e.IsTerminating}）。", exception);
            ShowCrash(exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // 观察掉，防止 .NET 直接把进程带走；任务级失败应当已由各服务上报。
            Log.Error("未被观察的任务异常。", e.Exception);
            e.SetObserved();
        };
    }

    private static void ShowCrash(Exception? exception)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                var title = Application.Current?.Resources.TryGetResource(LocalizationKeys.CommonErrorUnhandledTitle, null, out var t) == true && t is string ts ? ts : "CKAN UI 遇到错误";
                var bodyTemplate = Application.Current?.Resources.TryGetResource(LocalizationKeys.CommonErrorUnhandledBody, null, out var b) == true && b is string bs ? bs : "程序遇到未处理的错误，详细信息已写入日志。\n\n{0}";
                var unknown = Application.Current?.Resources.TryGetResource(LocalizationKeys.CommonErrorUnknown, null, out var u) == true && u is string us ? us : "未知错误";
                var errorDetail = string.Format(System.Globalization.CultureInfo.CurrentCulture, bodyTemplate, exception?.Message ?? unknown);

                var window = new Window
                {
                    Title = title,
                    Width = 640,
                    Height = 320,
                    Content = new ScrollViewer
                    {
                        Padding = new Thickness(16),
                        Content = new TextBlock
                        {
                            Text = errorDetail,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        },
                    },
                };
                window.Show();
            });
        }
        catch (Exception ex)
        {
            Log.Warn("崩溃提示窗口无法显示。", ex);
        }
    }

    /// <summary>
    /// Win32 平台选项：弹层（下拉列表 / 右键菜单 / Flyout / ToolTip）一律嵌进窗口自身的
    /// Overlay 层，不各开一个顶层子窗口。Windows 的 Z 序里子窗口恒在主窗口之上，
    /// 任何「窗口内图层」都压不住它——包括 ZIndex = int.MaxValue 的自绘光标层。
    /// 只有把弹层收进同一个 VisualLayerManager，光标才能稳定排在弹层前面。
    /// </summary>
    public static Win32PlatformOptions CreateWin32PlatformOptions() => new()
    {
        OverlayPopups = true,
    };

    /// <summary>Avalonia 引导（保持不变，供设计器与测试复用）。</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .With(CreateWin32PlatformOptions())
                  .WithInterFont()
                  .LogToTrace();

    private static void EnsureConfigFileWritable()
    {
        try
        {
            var defaultFile = Environment.GetEnvironmentVariable("CKAN_CONFIG_FILE")
                ?? System.IO.Path.Combine(CKAN.IO.CKANPathUtils.AppDataPath, "config.json");
            var dir = System.IO.Path.GetDirectoryName(defaultFile);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            // 测试能否以读写方式打开/创建
            using var fs = System.IO.File.Open(defaultFile, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.ReadWrite);
        }
        catch (Exception ex)
        {
            var fallback = System.IO.Path.Combine(AppContext.BaseDirectory, "config.json");
            Environment.SetEnvironmentVariable("CKAN_CONFIG_FILE", fallback);
            Log.Warn($"默认配置路径不可写 ({ex.Message})，重定向 CKAN_CONFIG_FILE 至: {fallback}");
        }
    }
}
