using System.Diagnostics;

namespace CkanUI.Services.Instances;

/// <summary>
/// 探测"某个进程是否正跑在给定目录里"（12 篇 F-33 的游戏运行中检查）。
/// <para>
/// 抽成接口只为可测：真实实现要走 <see cref="Process"/>，在 CI 容器里拿不到
/// <c>MainModule</c> 是常态，测试用替身即可，生产路径失败一律当作"没在跑"。
/// </para>
/// </summary>
public interface IGameProcessProbe
{
    /// <summary>
    /// 返回所有可读的进程可执行文件路径（已规范化、非空、去重）。
    /// 实现不得抛异常：读不到的进程直接跳过。
    /// </summary>
    IReadOnlyList<string> GetProcessImagePaths();
}

/// <summary>基于 <see cref="Process"/> 的实现。任何探测异常都退化成空结果。</summary>
/// <remarks>
/// 一次全进程扫描要读每个进程的 MainModule，实测约 50ms。多个页面（配置页、变更页、
/// 实例页）都会在激活时问一次"游戏在不在跑"，所以结果在 <see cref="CacheTtl"/> 内复用：
/// 一秒之内重复问不会得到不同答案，但用户刚启动/刚退出游戏最多迟一秒才被看到。
/// </remarks>
public sealed class GameProcessProbe : IGameProcessProbe
{
    /// <summary>结果复用窗口。</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(1);

    private readonly Func<IReadOnlyList<string>> _scan;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private IReadOnlyList<string>? _cached;
    private DateTimeOffset _cachedAt;

    /// <summary>生产用构造。</summary>
    public GameProcessProbe()
        : this(null, null)
    {
    }

    /// <summary>可测构造：注入扫描与时钟。</summary>
    /// <param name="scan">真正的扫描；为 null 时读本机进程表。</param>
    /// <param name="clock">时钟；为 null 时用系统时间。</param>
    public GameProcessProbe(Func<IReadOnlyList<string>>? scan, Func<DateTimeOffset>? clock = null)
    {
        _scan = scan ?? ScanProcesses;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetProcessImagePaths()
    {
        lock (_gate)
        {
            var now = _clock();
            if (_cached is { } cached && now - _cachedAt < CacheTtl)
            {
                return cached;
            }

            var paths = _scan();
            _cached = paths;
            _cachedAt = now;
            return paths;
        }
    }

    private static IReadOnlyList<string> ScanProcesses()
    {
        var paths = new List<string>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        foreach (var process in processes)
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }
            catch (Exception)
            {
                // 权限不足 / 进程已退出 / 位数不匹配：都当作读不到。
            }
            finally
            {
                process.Dispose();
            }
        }

        return paths;
    }

    /// <summary>
    /// 判断某个可执行文件路径是否落在游戏目录之内（纯函数，便于测试）。
    /// 只做保守判定：拿不到绝对路径就返回 <c>false</c>。
    /// </summary>
    public static bool IsInsideGameDir(string? imagePath, string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(gameDir))
        {
            return false;
        }

        string fullPath;
        string root;
        try
        {
            fullPath = Path.GetFullPath(imagePath);
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDir));
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrEmpty(root)) return false;
        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(root + '/', StringComparison.OrdinalIgnoreCase);
    }
}
