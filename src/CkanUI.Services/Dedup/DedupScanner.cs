using System.Security.Cryptography;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Dedup;

/// <summary>扫描期的计数与「跳过记录」收集（记录条数有上限，避免大实例把内存吃光）。</summary>
public sealed class DedupScanTally
{
    /// <summary>逐文件跳过记录的条数上限（计数不受此限制）。</summary>
    public const int MaxSkipRecords = 200;

    /// <summary>GameData 内枚举到的文件总数。</summary>
    public int ScannedFiles { get; set; }

    /// <summary>因尺寸过小被跳过的文件数。</summary>
    public int SmallFiles { get; set; }

    /// <summary>因受管被跳过的文件数。</summary>
    public int ManagedFiles { get; set; }

    /// <summary>因是符号链接/重解析点被跳过的数量（文件或目录）。</summary>
    public int LinkFiles { get; set; }

    /// <summary>因读取失败（权限、被占用、GameData 缺失）被跳过的数量。</summary>
    public int UnreadableFiles { get; set; }

    /// <summary>进入内容哈希的候选文件数。</summary>
    public int CandidateFiles { get; set; }

    /// <summary>跳过记录总数。</summary>
    public int SkippedCount { get; set; }

    /// <summary>记一条跳过（计数 + 有上限的记录）。</summary>
    public void Skip(DedupSkipKind kind, DedupSkipInfo info, List<DedupSkipInfo> sink)
    {
        SkippedCount++;
        switch (kind)
        {
            case DedupSkipKind.TooSmall:
                SmallFiles++;
                break;
            case DedupSkipKind.Managed:
                ManagedFiles++;
                break;
            case DedupSkipKind.Link:
                LinkFiles++;
                break;
            default:
                // OutsideGameData / MissingInstance / Unreadable / CrossVolume / Unsupported
                // 都归到「读不出来」这一类计数（报告里不再细分，原因仍逐条记录）。
                UnreadableFiles++;
                break;
        }

        if (sink.Count < MaxSkipRecords)
        {
            sink.Add(info);
        }
    }
}

/// <summary>
/// 去重扫描（F-41）：在 GameData 内枚举候选文件 → 按大小预分组 → 对同尺寸的文件做流式 SHA256 →
/// 按「内容哈希 + 尺寸 + 卷」成组。
/// </summary>
/// <remarks>
/// 安全边界（全部在这里落地）：只在 GameData 内、跳过符号链接/重解析点（含链接目录，不跟进去遍历）、
/// 默认跳过受管文件、只保留 ≥2 个副本的组、跨卷的重复内容拆成两组分别报告（跨卷无法硬链接）。
/// 一切读写失败都降级成跳过原因，不抛给调用方。
/// </remarks>
public static class DedupScanner
{
    /// <summary>默认最小候选尺寸 128 KiB（沿用旧 GUI <c>InstalledFilesDeduplicator.MinDedupFileSize</c>）。</summary>
    public const long DefaultMinimumFileSizeBytes = 128 * 1024;

    private const int HashBufferSize = 128 * 1024;
    private const string TempFilePrefix = ".ckanui-dedup-";

    /// <summary>枚举实例 GameData 内的候选文件。</summary>
    public static IReadOnlyList<DedupCandidateFile> Enumerate(
        DedupInstanceContext context,
        long minimumFileSizeBytes,
        bool includeManagedFiles,
        DedupScanTally tally,
        List<DedupSkipInfo> skipped,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tally);
        ArgumentNullException.ThrowIfNull(skipped);

        var candidates = new List<DedupCandidateFile>();
        string root;
        try
        {
            root = Path.GetFullPath(context.GameDataDir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            tally.Skip(DedupSkipKind.MissingInstance, new DedupSkipInfo(
                context.InstanceName, string.Empty, DedupSkipKind.MissingInstance, $"GameData 路径不合法：{context.GameDataDir}"), skipped);
            return candidates;
        }

        if (!Directory.Exists(root))
        {
            tally.Skip(DedupSkipKind.MissingInstance, new DedupSkipInfo(
                context.InstanceName, string.Empty, DedupSkipKind.MissingInstance, $"找不到 GameData 目录：{root}"), skipped);
            return candidates;
        }

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = stack.Pop();

            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                tally.Skip(DedupSkipKind.Unreadable, new DedupSkipInfo(
                    context.InstanceName, Relative(root, directory), DedupSkipKind.Unreadable, $"读不到目录内容：{ex.Message}"), skipped);
                continue;
            }

            foreach (var child in directories)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // 不跟进重解析点目录（junction / 目录符号链接）：否则会跨卷遍历、重复计数，
                    // 还可能把 GameData 之外的内容当成候选。
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        tally.Skip(DedupSkipKind.Link, new DedupSkipInfo(
                            context.InstanceName, Relative(root, child), DedupSkipKind.Link, "目录是符号链接或重解析点，已跳过。"), skipped);
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    tally.Skip(DedupSkipKind.Unreadable, new DedupSkipInfo(
                        context.InstanceName, Relative(root, child), DedupSkipKind.Unreadable, $"读不到目录属性：{ex.Message}"), skipped);
                    continue;
                }

                stack.Push(child);
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (name.StartsWith(TempFilePrefix, StringComparison.Ordinal))
                {
                    // 上一轮执行失败留下的临时名，不算候选也不计入统计。
                    continue;
                }

                tally.ScannedFiles++;

                FileAttributes attributes;
                long size;
                try
                {
                    attributes = File.GetAttributes(file);
                    size = new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    tally.Skip(DedupSkipKind.Unreadable, new DedupSkipInfo(
                        context.InstanceName, Relative(root, file), DedupSkipKind.Unreadable, $"读不到文件属性：{ex.Message}"), skipped);
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    tally.Skip(DedupSkipKind.Link, new DedupSkipInfo(
                        context.InstanceName, Relative(root, file), DedupSkipKind.Link, "文件是符号链接或重解析点，已跳过。"), skipped);
                    continue;
                }

                var relative = Relative(root, file);
                var managed = context.IsManaged(relative);
                if (managed && !includeManagedFiles)
                {
                    tally.Skip(DedupSkipKind.Managed, new DedupSkipInfo(
                        context.InstanceName, relative, DedupSkipKind.Managed, "受管文件，跳过（去重会破坏卸载语义）。"), skipped);
                    continue;
                }

                if (size < minimumFileSizeBytes)
                {
                    // 小文件数量可能上万：只计数不逐条记录。
                    tally.Skip(DedupSkipKind.TooSmall, new DedupSkipInfo(
                        context.InstanceName, relative, DedupSkipKind.TooSmall, "小于最小候选尺寸。"), skipped);
                    continue;
                }

                candidates.Add(new DedupCandidateFile(
                    context.InstanceName, relative, file, size, DedupVolume.Resolve(file), managed));
            }
        }

        tally.CandidateFiles += candidates.Count;
        return candidates;
    }

    /// <summary>把候选文件按「内容哈希 + 尺寸 + 卷」分组，只返回 ≥2 个不同实体的组。</summary>
    public static IReadOnlyList<DedupGroup> Group(
        IReadOnlyList<DedupCandidateFile> candidates,
        IDedupLinker linker,
        DedupScanTally tally,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(linker);
        ArgumentNullException.ThrowIfNull(tally);

        var groups = new List<DedupGroup>();

        // ① 先按大小分组：只有尺寸相同的文件才可能内容相同，这样能省掉绝大多数哈希。
        foreach (var sizeBucket in candidates.GroupBy(file => file.SizeBytes).Where(bucket => bucket.Count() >= 2))
        {
            ct.ThrowIfCancellationRequested();
            var hashed = new List<(DedupCandidateFile File, string Hash)>();
            foreach (var file in sizeBucket)
            {
                ct.ThrowIfCancellationRequested();
                var hash = TryHash(file.AbsolutePath, ct);
                if (hash is null)
                {
                    tally.UnreadableFiles++;
                    tally.SkippedCount++;
                    continue;
                }

                hashed.Add((file, hash));
            }

            // ② 内容哈希 + 卷：跨卷的重复内容不能硬链接，因此拆成两组分别报告。
            foreach (var contentBucket in hashed.GroupBy(item => (item.Hash, item.File.VolumeId)))
            {
                if (contentBucket.Count() < 2)
                {
                    continue;
                }

                var files = contentBucket
                    .Select(item => item.File)
                    .OrderBy(file => file.InstanceName, StringComparer.Ordinal)
                    .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
                    .ToArray();

                var distinct = CountDistinct(files, linker);
                if (distinct == 1)
                {
                    // 已经全是同一份数据（之前去过重），没有可释放空间。
                    continue;
                }

                groups.Add(new DedupGroup(contentBucket.Key.Hash, sizeBucket.Key, contentBucket.Key.VolumeId, files)
                {
                    DistinctFileCount = distinct,
                });
            }
        }

        return groups
            .OrderByDescending(group => group.ReclaimableBytes)
            .ThenBy(group => group.ContentHash, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>流式 SHA256（128 KiB 缓冲 + 每块检查取消），失败返回 null。</summary>
    private static string? TryHash(string path, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, HashBufferSize, FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[HashBufferSize];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>组内不同文件实体的个数；任一侧读不到标识时返回 null（按文件数计）。</summary>
    private static int? CountDistinct(IReadOnlyList<DedupCandidateFile> files, IDedupLinker linker)
    {
        var identities = new List<DedupFileIdentity>(files.Count);
        foreach (var file in files)
        {
            if (!linker.TryGetIdentity(file.AbsolutePath, out var identity) || !identity.IsKnown)
            {
                return null;
            }

            if (!identities.Any(existing => existing.SameAs(identity)))
            {
                identities.Add(identity);
            }
        }

        return identities.Count;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
