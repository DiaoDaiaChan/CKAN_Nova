using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Unmanaged;

namespace CkanUI.ViewModels.Pages.Dedup;

/// <summary>一组重复内容在表格里的一行（组内文件数 + 可释放字节）。</summary>
public sealed record DedupGroupRowViewModel(
    string ContentHash,
    string HashText,
    long SizeBytes,
    string SizeText,
    int FileCount,
    long ReclaimableBytes,
    string ReclaimableText,
    string VolumeText,
    string InstanceNamesText,
    IReadOnlyList<DedupFileRowViewModel> Files)
{
    /// <summary>从服务层候选组投影。</summary>
    public static DedupGroupRowViewModel From(DedupGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var files = group.Files.Select(DedupFileRowViewModel.From).ToArray();
        return new DedupGroupRowViewModel(
            group.ContentHash,
            group.ContentHash.Length <= 12 ? group.ContentHash : group.ContentHash[..12],
            group.SizeBytes,
            UnmanagedFormat.FormatSize(group.SizeBytes),
            files.Length,
            group.ReclaimableBytes,
            UnmanagedFormat.FormatSize(group.ReclaimableBytes),
            $"卷 {group.VolumeId}",
            string.Join("、", files.Select(file => file.InstanceName).Distinct(StringComparer.Ordinal)),
            files);
    }

    /// <summary>保留副本（组内第一份，其余将成为它的硬链接）。</summary>
    public string KeepText => Files.Count == 0 ? "—" : Files[0].DisplayName;

    /// <summary>组标题。</summary>
    public string HeaderText => $"{SizeText} × {FileCount} 份，可释放 {ReclaimableText}";
}

/// <summary>组内单个文件的一行。</summary>
public sealed record DedupFileRowViewModel(string InstanceName, string RelativePath, string DisplayName)
{
    /// <summary>从服务层候选文件投影。</summary>
    public static DedupFileRowViewModel From(DedupCandidateFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new DedupFileRowViewModel(file.InstanceName, file.RelativePath, $"{file.InstanceName}: {file.RelativePath}");
    }
}

/// <summary>执行报告在表格里的一行（预演/已建链/跳过/失败/回滚）。</summary>
public sealed record DedupResultRowViewModel(
    string DisplayName,
    string OutcomeText,
    string Message,
    string FreedText)
{
    /// <summary>从服务层执行结果投影。</summary>
    public static DedupResultRowViewModel From(DedupLinkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new DedupResultRowViewModel(
            result.DisplayName,
            OutcomeTextOf(result.Outcome),
            result.Message,
            result.FreedBytes > 0 ? UnmanagedFormat.FormatSize(result.FreedBytes) : "—");
    }

    private static string OutcomeTextOf(DedupLinkOutcome outcome) => outcome switch
    {
        DedupLinkOutcome.Planned => "预演",
        DedupLinkOutcome.Linked => "已建链",
        DedupLinkOutcome.RolledBack => "已回滚",
        DedupLinkOutcome.Failed => "失败",
        _ => "跳过",
    };
}
