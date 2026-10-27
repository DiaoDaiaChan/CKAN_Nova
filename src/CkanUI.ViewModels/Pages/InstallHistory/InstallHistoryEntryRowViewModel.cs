using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.InstallHistory;

/// <summary>历史列表里的一行（时间戳 + 操作 + 结果），并自带当时的已装集合分组。</summary>
public sealed class InstallHistoryEntryRowViewModel
{
    private readonly InstallHistoryEntry _entry;

    public InstallHistoryEntryRowViewModel(InstallHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;

        var modules = entry.Snapshot?.Modules ?? Array.Empty<InstallHistoryModule>();
        InstalledModules = modules
            .Where(module => module.IsInstalled)
            .Select(module => new InstallHistoryModuleRowViewModel(module))
            .ToArray();
        MissingModules = modules
            .Where(module => !module.IsInstalled)
            .Select(module => new InstallHistoryModuleRowViewModel(module))
            .ToArray();
    }

    /// <summary>原始条目（命令里要用时间戳）。</summary>
    public InstallHistoryEntry Entry => _entry;

    /// <summary>时间戳。</summary>
    public DateTimeOffset Timestamp => _entry.Timestamp;

    /// <summary>时间戳文案（本地时间，秒级）。</summary>
    public string TimestampText => _entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>操作类型文案。</summary>
    public string OperationText => InstallHistoryText.OperationLabel(_entry.Operation);

    /// <summary>结果文案。</summary>
    public string OutcomeText => InstallHistoryText.OutcomeLabel(_entry.Outcome);

    /// <summary>是否失败（部分失败也算失败，列表里要标红）。</summary>
    public bool IsFailure => _entry.Outcome is InstallHistoryOutcome.PartiallyFailed or InstallHistoryOutcome.Failed;

    /// <summary>变更条数文案。</summary>
    public string ChangeCountText => _entry.ChangeCount == 0 ? "无模组变更" : "共 " + _entry.ChangeCount + " 个模组变更";

    /// <summary>列表主文案：时间戳｜操作｜结果｜变更数。</summary>
    public string SummaryText => TimestampText + "｜" + OperationText + "｜" + OutcomeText + "｜" + ChangeCountText;

    /// <summary>备注（失败原因等），没有时为空串。</summary>
    public string MessageText => _entry.Message ?? string.Empty;

    /// <summary>是否有备注。</summary>
    public bool HasMessage => MessageText.Length > 0;

    /// <summary>这条历史是否带快照（带快照才能作为重装目标）。</summary>
    public bool HasSnapshot => _entry.Snapshot is not null;

    /// <summary>快照里的模组数。</summary>
    public int SnapshotModuleCount => _entry.Snapshot?.Modules.Count ?? 0;

    /// <summary>已装分组（当前仍装着）。</summary>
    public IReadOnlyList<InstallHistoryModuleRowViewModel> InstalledModules { get; }

    /// <summary>未装分组（当前没装或版本不同）。</summary>
    public IReadOnlyList<InstallHistoryModuleRowViewModel> MissingModules { get; }

    /// <summary>已装数。</summary>
    public int InstalledCount => InstalledModules.Count;

    /// <summary>未装数。</summary>
    public int MissingCount => MissingModules.Count;

    /// <summary>分组统计文案。</summary>
    public string GroupCountText => "已装 " + InstalledCount + " 个｜未装 " + MissingCount + " 个";

    /// <summary>已装分组标题。</summary>
    public string InstalledGroupTitle => "已安装（" + InstalledCount + "）";

    /// <summary>未装分组标题。</summary>
    public string MissingGroupTitle => "未安装（" + MissingCount + "）";

    /// <summary>是否有已装分组内容。</summary>
    public bool HasInstalledModules => InstalledCount > 0;

    /// <summary>是否有未装分组内容。</summary>
    public bool HasMissingModules => MissingCount > 0;

    /// <summary>能否从这条历史重装（要有非空快照）。</summary>
    public bool CanRestore => HasSnapshot && SnapshotModuleCount > 0;
}
