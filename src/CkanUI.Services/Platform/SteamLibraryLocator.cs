using System.Security;

using CkanUI.Core.Steam;
using CkanUI.Services.Abstractions;

using log4net;

namespace CkanUI.Services.Platform;

/// <summary>
/// 扫 Steam 库找 KSP 安装目录（03 篇 §5「自动查找」，06 篇 UI-32「Steam 导入」）。
/// </summary>
/// <remarks>
/// <para>根目录来源按顺序：注册表 <c>HKCU\Software\Valve\Steam\SteamPath</c>（<see cref="SteamRegistryProbe"/>）
/// → <c>%ProgramFiles(x86)%\Steam</c> → <c>%ProgramFiles%\Steam</c>。这与上游
/// <c>external/CKAN/Core/IO/SteamLibrary.cs:18-22</c> 一致：注册表是**前置**来源，而不是兜底。</para>
/// <para>每个根下先读 <c>config/libraryfolders.vdf</c>，再读旧版 <c>steamapps/libraryfolders.vdf</c>：
/// 现代 Steam 只维护前者，只读后者在真机上多半扫不到库（本仓 v1 的缺陷即此）。两个都缺时仍把根自己当库根——
/// 默认安装的 KSP 就在 <c>&lt;根&gt;/steamapps/common/Kerbal Space Program</c>。</para>
/// <para>只读：不写任何文件、不改注册表。</para>
/// </remarks>
public sealed class SteamLibraryLocator : ISteamLibraryLocator
{
    /// <summary>Steam 库清单文件名。</summary>
    public const string LibraryFoldersFileName = "libraryfolders.vdf";

    /// <summary>vdf 相对 Steam 根的两个位置，<c>config</c> 优先（现代 Steam 的写法）。</summary>
    private static readonly string[] LibraryFolderSubDirectories = ["config", "steamapps"];

    private static readonly ILog Log = LogManager.GetLogger(typeof(SteamLibraryLocator));

    private readonly SteamRegistryProbe _probe;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, string?> _readTextFile;
    private readonly Func<IEnumerable<string>> _fallbackRoots;

    /// <summary>构造探测器。</summary>
    /// <param name="probe">注册表探测器；<c>null</c> 用默认实现。</param>
    /// <param name="directoryExists">目录存在性判断，默认 <see cref="Directory.Exists(string)"/>。</param>
    /// <param name="readTextFile">读文本文件，文件不存在时返回 <c>null</c>；默认读磁盘。</param>
    /// <param name="fallbackRoots">注册表之外的候选 Steam 根，默认取 <c>ProgramFiles(x86)</c> 与 <c>ProgramFiles</c>。</param>
    public SteamLibraryLocator(
        SteamRegistryProbe? probe = null,
        Func<string, bool>? directoryExists = null,
        Func<string, string?>? readTextFile = null,
        Func<IEnumerable<string>>? fallbackRoots = null)
    {
        _probe = probe ?? new SteamRegistryProbe();
        _directoryExists = directoryExists ?? Directory.Exists;
        _readTextFile = readTextFile ?? DefaultReadTextFile;
        _fallbackRoots = fallbackRoots ?? DefaultFallbackRoots;
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<string>>> DiscoverGameDirsAsync(CancellationToken ct = default)
    {
        try
        {
            var found = new List<string>();
            foreach (var root in SteamRoots())
            {
                ct.ThrowIfCancellationRequested();
                CollectFromLibraryFiles(root, found);

                // 根自己也是一个库：绿色版/没有 vdf 时全靠这条。
                CollectCandidate(Path.Combine(root, "steamapps", "common", SteamLibraryParser.GameFolderName), found);
            }

            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Ok(found));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Fail("扫描 Steam 库已取消。"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Log.Warn("扫描 Steam 库失败。", ex);
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Fail("扫描 Steam 库失败。", ex.Message, retryable: true));
        }
    }

    /// <summary>候选 Steam 根（注册表优先，其次默认安装路径；已去重、已去空白）。</summary>
    public IReadOnlyList<string> SteamRoots()
    {
        var roots = new List<string>();

        Add(roots, _probe.TryGetSteamPath());
        foreach (var fallback in _fallbackRoots())
        {
            Add(roots, fallback);
        }

        return roots;

        static void Add(List<string> list, string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            var normalized = root.Trim().Replace('/', '\\');
            if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(normalized);
            }
        }
    }

    private void CollectFromLibraryFiles(string root, List<string> found)
    {
        foreach (var subDirectory in LibraryFolderSubDirectories)
        {
            var path = Path.Combine(root, subDirectory, LibraryFoldersFileName);
            var content = _readTextFile(path);
            if (content is null)
            {
                continue;
            }

            foreach (var candidate in SteamLibraryParser.CandidateGameDirs(SteamLibraryParser.ParseLibraryRoots(content)))
            {
                CollectCandidate(candidate, found);
            }
        }
    }

    private void CollectCandidate(string candidate, List<string> found)
    {
        if (_directoryExists(candidate) && !found.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            found.Add(candidate);
        }
    }

    private static string? DefaultReadTextFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 单个 vdf 读不了就当它不存在：别的根上还有机会找到。
            Log.Warn("读取 Steam 库清单失败。", ex);
            return null;
        }
    }

    private static IEnumerable<string> DefaultFallbackRoots()
    {
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Steam");
        }

        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Steam");
        }
    }
}
