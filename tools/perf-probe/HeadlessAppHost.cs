using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CkanUI.App;

namespace CkanUI.Tools.PerfProbe;

/// <summary>
/// 无头 Avalonia 测试宿主：初始化 Avalonia 运行时、Skia 后端与资源字典。
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

            var tempDir = Path.Combine(AppContext.BaseDirectory, "perf_temp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var testConfig = Path.Combine(tempDir, "config.json");
            Environment.SetEnvironmentVariable("CKAN_CONFIG_FILE", testConfig);

            AppBuilder.Configure<CkanUI.App.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = true
                })
                .SetupWithoutStarting();

            _initialized = true;
        }
    }

    public static void PumpJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
