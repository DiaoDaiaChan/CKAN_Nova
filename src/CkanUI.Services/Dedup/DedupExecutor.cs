using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Dedup;

/// <summary>
/// 去重执行器（F-41）：把每组除保留副本外的文件改成指向保留副本的硬链接。
/// </summary>
/// <remarks>
/// 写盘口径：**先建临时名再 Move**（<c>CreateLink(临时名, 保留副本)</c> → <c>File.Move(临时名, 目标, overwrite)</c>），
/// 这样目标路径要么还是原文件、要么已经是完整可用的硬链接，不会出现「删掉了但没接上」的中间态。
/// 原子单位是「组」：组内任一文件失败，就把本组已建好的链接回滚成独立副本（用 <see cref="File.Copy(string,string,bool)"/>
/// 从保留副本复原），然后继续处理下一组——单个文件失败不会中断整批。
/// 预演（<see cref="DedupExecuteRequest.DryRun"/>）只做校验，不创建任何临时文件。
/// </remarks>
public sealed class DedupExecutor
{
    private const string TempFilePrefix = ".ckanui-dedup-";

    private readonly IDedupLinker _linker;

    public DedupExecutor(IDedupLinker linker)
    {
        _linker = linker ?? throw new ArgumentNullException(nameof(linker));
    }

    /// <summary>执行（或预演）整批去重计划。</summary>
    public DedupExecuteReport Execute(
        DedupExecuteRequest request,
        IReadOnlyDictionary<string, DedupInstanceContext> contexts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(contexts);

        var support = _linker.Support;
        if (support != DedupPlatformSupport.Supported)
        {
            // 平台不支持硬链接：如实返回「不支持」结果，不做任何写盘，也不抛异常。
            var skipped = request.Groups
                .SelectMany(group => group.Files.Select(file => new DedupLinkResult(
                    file.InstanceName,
                    file.RelativePath,
                    file.AbsolutePath,
                    group.KeepFile?.AbsolutePath ?? string.Empty,
                    DedupLinkOutcome.Skipped,
                    "当前平台不支持创建硬链接，未做任何改动。",
                    0)))
                .ToArray();
            return new DedupExecuteReport(request.DryRun, support, skipped, 0);
        }

        var results = new List<DedupLinkResult>();
        long freed = 0;
        foreach (var group in request.Groups)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in ExecuteGroup(group, request, contexts, ct))
            {
                if (entry.Outcome is DedupLinkOutcome.Linked or DedupLinkOutcome.Planned)
                {
                    // 预演也要给出「预计可释放」，界面用它做二次确认文案。
                    freed += entry.FreedBytes;
                }

                results.Add(entry.ToResult());
            }
        }

        return new DedupExecuteReport(request.DryRun, support, results, freed);
    }

    private List<Entry> ExecuteGroup(
        DedupGroup group,
        DedupExecuteRequest request,
        IReadOnlyDictionary<string, DedupInstanceContext> contexts,
        CancellationToken ct)
    {
        var entries = new List<Entry>();

        // ① 选保留副本：组内第一份通过安全检查的文件（组已按实例名 + 相对路径稳定排序）。
        DedupCandidateFile? keep = null;
        foreach (var file in group.Files)
        {
            var reason = Check(file, group, request.IncludeManagedFiles, contexts, keep: null);
            if (reason is null)
            {
                keep = file;
                break;
            }

            entries.Add(Entry.Skipped(file, string.Empty, reason));
        }

        if (keep is null)
        {
            return entries;
        }

        // ② 其余文件改成指向保留副本的硬链接。
        var linked = new List<Entry>();
        foreach (var file in group.Files)
        {
            if (ReferenceEquals(file, keep))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();

            var reason = Check(file, group, request.IncludeManagedFiles, contexts, keep);
            if (reason is not null)
            {
                entries.Add(Entry.Skipped(file, keep.AbsolutePath, reason));
                continue;
            }

            if (request.DryRun)
            {
                entries.Add(Entry.Planned(file, keep.AbsolutePath, group.SizeBytes));
                continue;
            }

            var failure = TryLink(file, keep);
            if (failure is null)
            {
                var done = Entry.Linked(file, keep.AbsolutePath, group.SizeBytes);
                entries.Add(done);
                linked.Add(done);
                continue;
            }

            entries.Add(Entry.Failed(file, keep.AbsolutePath, failure));
            Rollback(linked, keep);
            break;
        }

        return entries;
    }

    /// <summary>本组回滚：把已经改成硬链接的文件复原成独立副本（内容本就相同，复制保留了数据）。</summary>
    private static void Rollback(IReadOnlyList<Entry> linked, DedupCandidateFile keep)
    {
        for (var index = linked.Count - 1; index >= 0; index--)
        {
            var entry = linked[index];
            var temp = TempPathFor(entry.File.AbsolutePath);
            try
            {
                File.Copy(keep.AbsolutePath, temp, overwrite: true);
                File.Move(temp, entry.File.AbsolutePath, overwrite: true);
                entry.Outcome = DedupLinkOutcome.RolledBack;
                entry.Message = "本组后续文件失败，已回滚为独立副本。";
                entry.FreedBytes = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(temp);
                entry.Outcome = DedupLinkOutcome.Failed;
                entry.Message = $"回滚失败：{ex.Message}（该文件仍是硬链接）。";
                entry.FreedBytes = 0;
            }
        }
    }

    /// <summary>真正的写盘：临时名建链 + 校验 + 原子替换。</summary>
    private string? TryLink(DedupCandidateFile file, DedupCandidateFile keep)
    {
        var temp = TempPathFor(file.AbsolutePath);
        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            _linker.CreateLink(temp, keep.AbsolutePath);

            // 能读到文件标识时校验一次：新链接必须与保留副本是同一份数据。
            if (_linker.TryGetIdentity(keep.AbsolutePath, out var keepIdentity)
                && _linker.TryGetIdentity(temp, out var tempIdentity)
                && !tempIdentity.SameAs(keepIdentity))
            {
                TryDelete(temp);
                return "硬链接校验失败：新链接与保留副本不是同一份数据。";
            }

            File.Move(temp, file.AbsolutePath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temp);
            return ex switch
            {
                UnauthorizedAccessException => $"权限不足，未能替换文件：{ex.Message}",
                NotSupportedException => $"当前文件系统不支持硬链接：{ex.Message}",
                _ => $"替换文件失败：{ex.Message}",
            };
        }
    }

    /// <summary>安全检查（安全边界②③⑤ + 存在性 + GameData 边界）。返回 null 表示可以做。</summary>
    private string? Check(
        DedupCandidateFile file,
        DedupGroup group,
        bool includeManaged,
        IReadOnlyDictionary<string, DedupInstanceContext> contexts,
        DedupCandidateFile? keep)
    {
        if (!contexts.TryGetValue(file.InstanceName, out var context))
        {
            return $"实例「{file.InstanceName}」已不存在或读不到受管清单，跳过。";
        }

        if (!context.IsInsideGameData(file.AbsolutePath))
        {
            return "不在 GameData 内，跳过。";
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(file.AbsolutePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return "文件已不存在，跳过。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"读不到文件属性，跳过：{ex.Message}";
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return "不是普通文件，跳过。";
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return "是符号链接或重解析点，跳过。";
        }

        if (!includeManaged && context.IsManaged(context.ToRelative(file.AbsolutePath)))
        {
            return "受管文件，跳过（改动会破坏卸载语义）。";
        }

        var volume = DedupVolume.Resolve(file.AbsolutePath);
        if (!string.Equals(volume, group.VolumeId, DedupVolume.Comparison))
        {
            return $"跨卷（{group.VolumeId} → {volume}），不能硬链接，跳过。";
        }

        if (keep is not null
            && _linker.TryGetIdentity(file.AbsolutePath, out var target)
            && _linker.TryGetIdentity(keep.AbsolutePath, out var keepIdentity)
            && target.SameAs(keepIdentity))
        {
            return "已经是同一份文件（已硬链接），跳过。";
        }

        return null;
    }

    private static string TempPathFor(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var name = TempFilePrefix + Guid.NewGuid().ToString("N") + ".tmp";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
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
            // 临时文件删不掉不影响结论：扫描阶段会忽略这个前缀。
        }
    }

    /// <summary>可变的执行条目（回滚需要就地改结果），最后投影成不可变 DTO。</summary>
    private sealed class Entry
    {
        public required DedupCandidateFile File { get; init; }

        public string KeepPath { get; init; } = string.Empty;

        public DedupLinkOutcome Outcome { get; set; }

        public string Message { get; set; } = string.Empty;

        public long FreedBytes { get; set; }

        public static Entry Skipped(DedupCandidateFile file, string keepPath, string reason) => new()
        {
            File = file,
            KeepPath = keepPath,
            Outcome = DedupLinkOutcome.Skipped,
            Message = reason,
        };

        public static Entry Planned(DedupCandidateFile file, string keepPath, long size) => new()
        {
            File = file,
            KeepPath = keepPath,
            Outcome = DedupLinkOutcome.Planned,
            Message = "预演：将改成硬链接。",
            FreedBytes = size,
        };

        public static Entry Linked(DedupCandidateFile file, string keepPath, long size) => new()
        {
            File = file,
            KeepPath = keepPath,
            Outcome = DedupLinkOutcome.Linked,
            Message = "已改成硬链接。",
            FreedBytes = size,
        };

        public static Entry Failed(DedupCandidateFile file, string keepPath, string reason) => new()
        {
            File = file,
            KeepPath = keepPath,
            Outcome = DedupLinkOutcome.Failed,
            Message = reason,
        };

        public DedupLinkResult ToResult() => new(
            File.InstanceName, File.RelativePath, File.AbsolutePath, KeepPath, Outcome, Message, FreedBytes);
    }
}
