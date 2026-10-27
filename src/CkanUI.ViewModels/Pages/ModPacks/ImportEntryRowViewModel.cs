using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>导入预览表格的一行：标识、来源文件、状态、原因，以及是否勾选。</summary>
public sealed partial class ImportEntryRowViewModel : ObservableObject
{
    /// <summary>用一条预览条目构造行。</summary>
    /// <param name="entry">服务层给出的预览条目。</param>
    public ImportEntryRowViewModel(ModPackEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        Identifier = entry.Identifier;
        SourceName = string.IsNullOrWhiteSpace(entry.SourceName) ? "—" : entry.SourceName;
        StateText = ModPackStateText.Label(entry.State);
        StatusText = Describe(entry);
        ReasonText = string.IsNullOrWhiteSpace(entry.Reason) ? "—" : entry.Reason!;
        _isSelected = entry.IsSelectable;
    }

    /// <summary>对应的服务层条目。</summary>
    public ModPackEntry Entry { get; }

    /// <summary>模组标识。</summary>
    public string Identifier { get; }

    /// <summary>来源文件（.ckan 文件名）。</summary>
    public string SourceName { get; }

    /// <summary>条目状态。</summary>
    public ModPackEntryState State => Entry.State;

    /// <summary>状态的中文标签。</summary>
    public string StateText { get; }

    /// <summary>版本变化的简述。</summary>
    public string StatusText { get; }

    /// <summary>原因（冲突 / 找不到时必读；没有原因时为“—”）。</summary>
    public string ReasonText { get; }

    /// <summary>这一行是否可勾选（只有会安装 / 升级 / 降级的条目可以）。</summary>
    public bool CanSelect => Entry.IsSelectable;

    /// <summary>是否是需要用户注意的问题行。</summary>
    public bool IsProblem => Entry.IsProblem;

    /// <summary>是否被勾选参与导入。</summary>
    [ObservableProperty]
    private bool _isSelected;

    private static string Describe(ModPackEntry entry)
    {
        var target = string.IsNullOrWhiteSpace(entry.TargetVersion) ? "最新版本" : entry.TargetVersion!;
        return entry.State switch
        {
            ModPackEntryState.Install => "将安装 " + target,
            ModPackEntryState.Upgrade => entry.InstalledVersion + " → " + target,
            ModPackEntryState.Downgrade => entry.InstalledVersion + " → " + target + "（降级）",
            ModPackEntryState.SameVersion => "已安装 " + (entry.InstalledVersion ?? target),
            ModPackEntryState.Missing => "仓库索引里没有这个模组",
            ModPackEntryState.Conflict => string.IsNullOrWhiteSpace(entry.RequestedVersion)
                ? "来源之间版本不一致"
                : "请求 " + entry.RequestedVersion + "，索引里没有",
            _ => "—"
        };
    }
}
