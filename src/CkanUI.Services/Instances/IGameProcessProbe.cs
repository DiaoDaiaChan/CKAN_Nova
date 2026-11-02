using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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
        => OperatingSystem.IsWindows() ? ScanWindows() : ScanManaged();

    /// <summary>
    /// Windows 快路径：<c>EnumProcesses</c> 拿 pid，<c>QueryFullProcessImageName</c> 读路径。
    /// 慢的根源是 <see cref="Process.MainModule"/>：每个进程都要读一次 PEB/模块表，本机 570 个进程实测 820-840ms，
    /// 而这条路径同一台机器只有 2-3ms。两者都是"能读到就返回绝对路径、读不到就跳过"，判定结果一致。
    /// </summary>
    private static IReadOnlyList<string> ScanWindows()
    {
        const uint QueryLimitedInformation = 0x1000;
        var pids = new uint[512];
        uint bytesReturned;
        while (true)
        {
            if (!EnumProcesses(pids, (uint)pids.Length * sizeof(uint), out bytesReturned))
            {
                // 枚举失败就退回托管路径：快路径出问题不能让"游戏在不在跑"变成永远 false。
                return ScanManaged();
            }

            if (bytesReturned < (uint)pids.Length * sizeof(uint) || pids.Length >= 32 * 1024)
            {
                break;
            }

            pids = new uint[pids.Length * 2];
        }

        var count = (int)(bytesReturned / sizeof(uint));
        var paths = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new StringBuilder(2048);

        for (var index = 0; index < count; index++)
        {
            var handle = OpenProcess(QueryLimitedInformation, false, (int)pids[index]);
            if (handle == IntPtr.Zero)
            {
                // 系统进程 / 已退出 / 权限不足：与 MainModule 路径一样当作读不到。
                continue;
            }

            try
            {
                buffer.Clear();
                var size = buffer.Capacity;
                if (QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0)
                {
                    var path = buffer.ToString(0, size);
                    if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                    {
                        paths.Add(path);
                    }
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        return paths;
    }

    /// <summary>非 Windows 与快路径失败时的托管实现。</summary>
    private static IReadOnlyList<string> ScanManaged()
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

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcesses([Out] uint[] processIds, uint arraySizeInBytes, out uint bytesReturned);

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

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
