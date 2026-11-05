using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// 把进程的内存时间轴写到文件里，供外部脚本（tools/perf-probe/measure-instance.mjs）回收。
/// 起因：用户那边接入大型整合包会涨到 4GB，而我们三套本地实例都复现不到；
/// 应用内日志（log4net）在这台机器上写不进 %LOCALAPPDATA%，只有探针自己指定的路径能落盘，
/// 所以这里复用同一个目录，不依赖任何日志框架。
/// 采样只读计数器，不触发 GC（<see cref="GC.GetTotalMemory(bool)"/> 传 false），不影响被测的帧间隔。
/// </summary>
public static class MemoryTimeline
{
    private static Timer? _timer;
    private static Stopwatch? _clock;
    private static string? _path;
    private static int _failures;

    /// <summary>按环境变量 <c>CKANUI_MEM_TRACE</c> 启动；没设就跟随 <paramref name="fallbackPath"/> 加 <c>.mem</c> 后缀。</summary>
    /// <param name="fallbackPath">导航 trace 的路径，作为默认落盘位置。</param>
    /// <param name="intervalMs">采样间隔，默认 2000ms。</param>
    public static void Start(string fallbackPath, int intervalMs = 2000)
    {
        if (_timer is not null)
        {
            return;
        }

        var configured = Environment.GetEnvironmentVariable("CKANUI_MEM_TRACE");
        _path = string.IsNullOrWhiteSpace(configured) ? fallbackPath + ".mem" : configured;
        _clock = Stopwatch.StartNew();
        Write("start");

        _timer = new Timer(_ => Write("tick"), null, intervalMs, intervalMs);
    }

    /// <summary>停止采样（进程退出时也会自然停）。</summary>
    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static void Write(string kind)
    {
        if (_path is null || _clock is null || _failures >= 3)
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var ws = process.WorkingSet64 / (1024d * 1024d);
            var priv = process.PrivateMemorySize64 / (1024d * 1024d);
            var heap = GC.GetTotalMemory(forceFullCollection: false) / (1024d * 1024d);
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "[mem] {0} t={1:0.0}s ws={2:0.0}MB private={3:0.0}MB heap={4:0.0}MB gen0={5} gen2={6} threads={7}",
                kind,
                _clock.Elapsed.TotalSeconds,
                ws,
                priv,
                heap,
                GC.CollectionCount(0),
                GC.CollectionCount(2),
                process.Threads.Count);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // 落盘失败只少数几次就放弃，绝不因为诊断把应用拖垮。
            _failures++;
        }
    }
}
