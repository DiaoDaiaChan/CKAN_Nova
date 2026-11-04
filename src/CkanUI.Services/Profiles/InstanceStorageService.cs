using System.Security.Cryptography;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;
using CkanUI.Services.Dedup;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Profiles;

/// <summary>
/// 多实例磁盘占用扫描 + 配置档内容去重。
/// </summary>
/// <remarks>
/// 扫描只读：统计 live <c>GameData</c>/<c>CKAN</c> 与 <c>CKANProfiles/&lt;档&gt;/GameData</c> 的字节数，
/// 单目录读失败只进 <see cref="InstanceStorageReport.Diagnostics"/>，不中断整个报告。
/// 去重把多个配置档里内容相同的普通文件合并成硬链接（保留一份数据、多个目录项），
/// 只在同卷、同长度、同 SHA-256 且长度 ≥128 KiB 时动手；默认 dryRun 只估算。
/// </remarks>
public sealed class InstanceStorageService : IInstanceStorageService
{
    private const string GameDataName = "GameData";
    private const string CkanDirectoryName = "CKAN";
    private const int MaxDiagnostics = 64;
    private const int MaxWarnings = 32;
    private const long MinimumDedupLength = 128 * 1024;
    private const string LinkTempPrefix = ".ckanui-profilelink-";

    private readonly log4net.ILog _log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);
    private readonly IInstanceService _instances;
    private readonly IDedupLinker _linker;

    /// <summary>注入实例列表与硬链接实现。</summary>
    public InstanceStorageService(IInstanceService instances, IDedupLinker linker)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _linker = linker ?? throw new ArgumentNullException(nameof(linker));
    }

    /// <inheritdoc />
    public Task<OperationResult<InstanceStorageReport>> ScanAsync(CancellationToken ct = default) =>
        GuardAsync(() => Scan(ct), "扫描实例磁盘占用失败。");

    /// <inheritdoc />
    public Task<OperationResult<ProfileDedupReport>> DeduplicateProfilesAsync(
        string instanceName, bool dryRun = true, CancellationToken ct = default) =>
        GuardAsync(() => Deduplicate(instanceName, dryRun, ct), "配置档去重失败。");

    // ── 扫描 ───────────────────────────────────────────────────────────────

    private OperationResult<InstanceStorageReport> Scan(CancellationToken ct)
    {
        var diagnostics = new List<string>();
        var rows = new List<InstanceStorageRow>();

        foreach (var instance in _instances.Instances
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(ScanInstance(instance, diagnostics, ct));
        }

        var report = new InstanceStorageReport(
            rows,
            rows.Sum(row => row.TotalBytes),
            rows.Sum(row => row.ProfileBytes),
            DateTime.UtcNow,
            diagnostics.Take(MaxDiagnostics).ToArray());

        return OperationResult<InstanceStorageReport>.Ok(report);
    }

    private InstanceStorageRow ScanInstance(InstanceInfo instance, List<string> diagnostics, CancellationToken ct)
    {
        var gameDir = instance.GameDir;
        var liveGameData = Path.Combine(gameDir, GameDataName);
        var liveCkan = Path.Combine(gameDir, CkanDirectoryName);

        var (liveBytes, liveFiles) = MeasureDirectory(liveGameData, diagnostics, ct);
        var (ckanBytes, _) = MeasureDirectory(liveCkan, diagnostics, ct);

        var manifest = ProfileManifest.Load(gameDir);
        var profileRows = new List<ProfileStorageRow>();
        long profileBytes = 0;

        foreach (var entry in manifest.Profiles
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var isActive = string.Equals(manifest.ActiveProfile, entry.Name, StringComparison.OrdinalIgnoreCase);
            if (isActive)
            {
                // 生效档的数据就在游戏根目录，已经算进 liveGameData；再算一遍就是重复计费。
                profileRows.Add(new ProfileStorageRow(entry.Name, true, liveBytes, liveFiles));
                continue;
            }

            var profileGameData = Path.Combine(
                ProfileManifest.ResolveProfileDirectory(gameDir, entry.Name), GameDataName);
            var (bytes, files) = MeasureDirectory(profileGameData, diagnostics, ct);
            profileRows.Add(new ProfileStorageRow(entry.Name, false, bytes, files));
            profileBytes += bytes;
        }

        var (free, volume) = ReadVolume(gameDir);
        return new InstanceStorageRow(
            instance.Name,
            gameDir,
            liveBytes,
            ckanBytes,
            profileBytes,
            liveBytes + ckanBytes + profileBytes,
            profileRows,
            free,
            volume);
    }

    private (long Bytes, int Files) MeasureDirectory(string directory, List<string> diagnostics, CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }

        long bytes = 0;
        var files = 0;
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            // 重解析点（符号链接/junction）不算本实例占用，也不跟进去。
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(entry);
                            continue;
                        }

                        bytes += new FileInfo(entry).Length;
                        files++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AddDiagnostic(diagnostics, $"跳过 {entry}：{ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AddDiagnostic(diagnostics, $"读取目录失败 {current}：{ex.Message}");
            }
        }

        return (bytes, files);
    }

    private static (long? Free, long? Volume) ReadVolume(string gameDir)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(gameDir));
            if (string.IsNullOrEmpty(root))
            {
                return (null, null);
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? (drive.AvailableFreeSpace, drive.TotalSize) : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return (null, null);
        }
    }

    // ── 去重 ───────────────────────────────────────────────────────────────

    private OperationResult<ProfileDedupReport> Deduplicate(string instanceName, bool dryRun, CancellationToken ct)
    {
        var warnings = new List<string>();
        var instance = _instances.Instances.FirstOrDefault(item =>
            string.Equals(item.Name, instanceName, StringComparison.OrdinalIgnoreCase));
        if (instance is null)
        {
            return OperationResult<ProfileDedupReport>.Fail($"找不到实例「{instanceName}」。");
        }

        var report = new ProfileDedupReport(instance.Name, dryRun, 0, 0, 0, warnings);
        try
        {
            report = DeduplicateCore(instance, dryRun, warnings, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 契约：去重中途出问题也只降级成警告，不把整份报告判失败。
            _log.Warn("配置档去重中止。", ex);
            AddWarning(warnings, $"去重中止：{ex.Message}");
            report = report with { Warnings = warnings.Take(MaxWarnings).ToArray() };
        }

        return OperationResult<ProfileDedupReport>.Ok(report);
    }

    private ProfileDedupReport DeduplicateCore(
        InstanceInfo instance, bool dryRun, List<string> warnings, CancellationToken ct)
    {
        var report = new ProfileDedupReport(instance.Name, dryRun, 0, 0, 0, warnings);
        var profilesRoot = Path.Combine(instance.GameDir, ProfileManifest.DirectoryName);
        if (!Directory.Exists(profilesRoot))
        {
            warnings.Add("没有 CKANProfiles 目录，跳过。");
            return report with { Warnings = warnings.Take(MaxWarnings).ToArray() };
        }

        var candidates = CollectCandidates(profilesRoot, warnings, ct);
        long reclaimable = 0;
        var duplicates = 0;
        var groups = 0;

        foreach (var lengthGroup in candidates.GroupBy(candidate => candidate.Length)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key))
        {
            ct.ThrowIfCancellationRequested();
            var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var candidate in lengthGroup)
            {
                var hash = TryHash(candidate.Path, warnings);
                if (hash is null)
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out var list))
                {
                    list = new List<string>();
                    byHash[hash] = list;
                }

                list.Add(candidate.Path);
            }

            foreach (var sameContent in byHash.Values.Where(list => list.Count > 1))
            {
                ct.ThrowIfCancellationRequested();
                var ordered = sameContent
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var keeper = ordered[0];
                var merged = 0;

                foreach (var duplicate in ordered.Skip(1))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!DedupVolume.SameVolume(keeper, duplicate))
                    {
                        AddWarning(warnings, $"跨卷跳过：{duplicate}");
                        continue;
                    }

                    if (dryRun)
                    {
                        merged++;
                        reclaimable += lengthGroup.Key;
                        continue;
                    }

                    if (TryLink(keeper, duplicate, warnings))
                    {
                        merged++;
                        reclaimable += lengthGroup.Key;
                    }
                }

                if (merged > 0)
                {
                    groups++;
                    duplicates += merged;
                }
            }
        }

        var result = report with
        {
            GroupCount = groups,
            DuplicateFileCount = duplicates,
            ReclaimableBytes = reclaimable,
            Warnings = warnings.Take(MaxWarnings).ToArray(),
        };

        if (!dryRun)
        {
            _log.Info(FormattableString.Invariant(
                $"配置档去重完成：{instance.Name} 组 {groups}，合并 {duplicates} 个文件，释放 {reclaimable} 字节。"));
        }

        return result;
    }

    private List<(string Path, long Length)> CollectCandidates(
        string profilesRoot, List<string> warnings, CancellationToken ct)
    {
        var candidates = new List<(string Path, long Length)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profileDir in SafeEnumerateDirectories(profilesRoot, warnings))
        {
            ct.ThrowIfCancellationRequested();
            var gameData = Path.Combine(profileDir, GameDataName);
            if (!Directory.Exists(gameData))
            {
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(gameData);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = pending.Pop();
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(current);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AddWarning(warnings, $"读取目录失败 {current}：{ex.Message}");
                    continue;
                }

                foreach (var entry in entries)
                {
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AddWarning(warnings, $"跳过 {entry}：{ex.Message}");
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // 符号链接/junction 一律不碰：链接别人的数据不是去重，是事故。
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(entry);
                        if (info.Length < MinimumDedupLength || !seen.Add(info.FullName))
                        {
                            continue;
                        }

                        candidates.Add((info.FullName, info.Length));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AddWarning(warnings, $"跳过 {entry}：{ex.Message}");
                    }
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root, List<string> warnings)
    {
        try
        {
            return Directory.EnumerateDirectories(root)
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddWarning(warnings, $"读取配置档目录失败 {root}：{ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string? TryHash(string path, List<string> warnings)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddWarning(warnings, $"读取文件失败 {path}：{ex.Message}");
            return null;
        }
    }

    private bool TryLink(string keeper, string duplicate, List<string> warnings)
    {
        var directory = Path.GetDirectoryName(duplicate) ?? ".";
        var temp = Path.Combine(directory, LinkTempPrefix + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            _linker.CreateLink(temp, keeper);

            // 防御：确认临时名真的指向 keeper 的同一份数据，再覆盖原文件。
            if (_linker.TryGetIdentity(temp, out var tempIdentity)
                && _linker.TryGetIdentity(keeper, out var keeperIdentity)
                && !tempIdentity.SameAs(keeperIdentity))
            {
                AddWarning(warnings, $"链接校验失败 {duplicate}，已跳过。");
                return false;
            }

            File.Move(temp, duplicate, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            AddWarning(warnings, $"建立硬链接失败 {duplicate}：{ex.Message}");
            return false;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时链接删不掉只是残留一个文件名，不影响报告。
        }
    }

    private static void AddDiagnostic(List<string> diagnostics, string message)
    {
        if (diagnostics.Count < MaxDiagnostics)
        {
            diagnostics.Add(message);
        }
    }

    private static void AddWarning(List<string> warnings, string message)
    {
        if (warnings.Count < MaxWarnings && !warnings.Contains(message))
        {
            warnings.Add(message);
        }
    }

    private async Task<OperationResult<T>> GuardAsync<T>(Func<OperationResult<T>> action, string failureMessage)
    {
        try
        {
            return await Task.Run(action).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _log.Warn($"{failureMessage}操作已取消。", ex);
            return OperationResult<T>.Fail("操作已取消。", ex.Message, retryable: true);
        }
        catch (Exception ex)
        {
            _log.Error(failureMessage, ex);
            return OperationResult<T>.Fail(failureMessage, ex.ToString(), retryable: true);
        }
    }
}
