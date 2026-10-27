using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CkanUI.Platform.SingleInstance;
using CkanUI.Services.Logging;
using log4net;

namespace CkanUI.App;

/// <summary>进程入口（ARC-01 / ARC-04 / ARC-05）。</summary>
public static class Program
{
    /// <summary>单实例互斥量与命名管道使用同一标识。</summary>
    public const string AppId = "CkanUI";

    private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

    [STAThread]
    public static void Main(string[] args)
    {
        // ①：日志最先就位，否则后面的失败都看不到原因（02 篇 §1）。
        LoggingSetup.Initialize();
        InstallCrashHandlers();
        Log.Info("CKAN UI 启动。");

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
                var window = new Window
                {
                    Title = "CKAN UI 遇到错误",
                    Width = 640,
                    Height = 320,
                    Content = new ScrollViewer
                    {
                        Padding = new Thickness(16),
                        Content = new TextBlock
                        {
                            Text = "程序遇到未处理的错误，详细信息已写入日志。\n\n" + (exception?.Message ?? "未知错误"),
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

    /// <summary>Avalonia 引导（保持不变，供设计器与测试复用）。</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .WithInterFont()
                  .LogToTrace();
}
