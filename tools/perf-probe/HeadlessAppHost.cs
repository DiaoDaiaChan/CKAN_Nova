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

            // 默认把 CKAN 配置指向一次性的空文件，探针看到的是干净环境（不读用户真实实例、
            // 也不会去碰 registry 锁）。要量「带真实实例数据」的页面成本，给
            // CKANUI_PROBE_REAL_CONFIG=1，这里就不覆盖，Core 会读用户自己的 config.json。
            if (Environment.GetEnvironmentVariable("CKANUI_PROBE_REAL_CONFIG") != "1")
            {
                var tempDir = Path.Combine(AppContext.BaseDirectory, "perf_temp", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var testConfig = Path.Combine(tempDir, "config.json");
                Environment.SetEnvironmentVariable("CKAN_CONFIG_FILE", testConfig);
            }

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
