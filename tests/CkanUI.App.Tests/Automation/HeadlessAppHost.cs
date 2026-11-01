using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace CkanUI.App.Tests.Automation;

/// <summary>
/// 无头 Avalonia 应用宿主：在内存中初始化 Avalonia 运行时与资源字典，
/// 支持无物理显示环境、无真实窗口、无 OS 物理输入模拟的跨平台自动化测试。
/// </summary>
public static class HeadlessAppHost
{
    private static bool _initialized;
    private static readonly object _lock = new();

    public static void EnsureInitialized()
    {
        lock (_lock)
        {
            if (_initialized) return;

            // 隔离 CKAN 配置文件路径到测试目录，避免不同平台下读写系统临时目录或 AppData 权限问题
            var tempDir = Path.Combine(AppContext.BaseDirectory, "headless_temp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var testConfig = Path.Combine(tempDir, "config.json");
            Environment.SetEnvironmentVariable("CKAN_CONFIG_FILE", testConfig);

            // Skia 的原生库跟着测试程序集放在 runtimes/<rid>/native 下；TestRunner 宿主的
            // 输出目录里没有它，原生解析会退到 PATH，可能命中机器上某个旧版 libSkiaSharp。
            // 把测试程序集旁边的原生目录提到 PATH 最前面，让真渲染加载到配套的版本。
            PrependNativeAssetDirectory();

            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    // 关掉桩渲染器：改用 Skia 真渲染，测试才能用 CaptureRenderedFrame 拿到像素。
                    UseHeadlessDrawing = false
                })
                .SetupWithoutStarting();

            _initialized = true;
        }
    }

    /// <summary>
    /// 在无头模式下创建一个托管指定内容的测试窗口，并完成首帧布局与任务排空。
    /// </summary>
    private static void PrependNativeAssetDirectory()
    {
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var assemblyDirectory = Path.GetDirectoryName(typeof(HeadlessAppHost).Assembly.Location);
        if (assemblyDirectory is null)
        {
            return;
        }

        var nativeDirectory = Path.Combine(assemblyDirectory, "runtimes", rid, "native");
        if (!Directory.Exists(nativeDirectory))
        {
            return;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!path.Contains(nativeDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", nativeDirectory + Path.PathSeparator + path);
        }
    }

    public static Window CreateTestWindow(Control content, double width = 1280, double height = 800)
    {
        EnsureInitialized();

        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content
        };

        window.Show();
        PumpJobs();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        PumpJobs();

        return window;
    }

    /// <summary>
    /// 推进并排空当前 UI 调度队列中的所有待处理任务。
    /// </summary>
    public static void PumpJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
