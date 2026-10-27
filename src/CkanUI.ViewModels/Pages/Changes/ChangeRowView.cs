using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Changes;

/// <summary>
/// 变更明细行的展示投影（03 篇 §4 明细表）：把动作/来源/版本/体积的文案与徽章可见性收在 VM 侧，
/// XAML 里不写格式串、不放转换器（03 篇 §11）。
/// </summary>
/// <remarks>
/// 每行一个轻量 record，明细表整体替换（04 篇 §2.1），不做增量 CollectionChanged。
/// </remarks>
public sealed record ChangeRowView(ChangeItemDto Data)
{
    /// <summary>模组标识符。</summary>
    public string Identifier => Data.Identifier;

    /// <summary>模组显示名（空时回退到标识符）。</summary>
    public string Name => string.IsNullOrWhiteSpace(Data.Name) ? Data.Identifier : Data.Name;

    /// <summary>动作文案：安装 / 升级 / 降级 / 卸载 / 替换。</summary>
    public string ActionText => ChangesFormatter.ActionText(Data.Action);

    /// <summary>来源文案（完整）：用户选择 / 依赖 / 推荐 / 自动安装。</summary>
    public string OriginText => ChangesFormatter.OriginText(Data.Origin);

    /// <summary>来源徽章文案（短）：用户 / 依赖 / 推荐 / 自动。</summary>
    public string OriginBadgeText => ChangesFormatter.OriginBadgeText(Data.Origin);

    /// <summary>从版本（无则"—"）。</summary>
    public string FromVersionText => string.IsNullOrWhiteSpace(Data.FromVersion) ? "—" : Data.FromVersion;

    /// <summary>到版本（无则"—"）。</summary>
    public string ToVersionText => string.IsNullOrWhiteSpace(Data.ToVersion) ? "—" : Data.ToVersion;

    /// <summary>本项需要下载的体积（无下载量时"—"）。</summary>
    public string SizeText => ChangesFormatter.Bytes(Data.DownloadBytes);

    /// <summary>是否卸载类动作（行首用危险色竖条 + 文字说明，不单靠颜色）。</summary>
    public bool IsRemoval => Data.Action == ChangeAction.Remove;

    /// <summary>是否降级（需额外警示：降级可能破坏依赖）。</summary>
    public bool IsDowngrade => Data.Action == ChangeAction.Downgrade;

    /// <summary>是否用户显式选择。</summary>
    public bool IsUserRequested => Data.Origin == ChangeOrigin.UserRequested;

    /// <summary>是否依赖隐式带入。</summary>
    public bool IsDependency => Data.Origin == ChangeOrigin.Dependency;

    /// <summary>是否推荐带入。</summary>
    public bool IsRecommendation => Data.Origin == ChangeOrigin.Recommendation;

    /// <summary>是否自动安装项。</summary>
    public bool IsAutoInstalled => Data.Origin == ChangeOrigin.AutoInstalled;

    /// <summary>是否显示"用户"徽章。</summary>
    public bool ShowUserBadge => IsUserRequested;

    /// <summary>是否显示"依赖"徽章。</summary>
    public bool ShowDependencyBadge => IsDependency;

    /// <summary>是否显示"推荐"徽章。</summary>
    public bool ShowRecommendationBadge => IsRecommendation;

    /// <summary>是否显示"自动"徽章。</summary>
    public bool ShowAutoBadge => IsAutoInstalled;

    /// <summary>行内"移除"按钮文案（来自 <see cref="ChangesLabels"/>，视图不写字面串）。</summary>
    public string RemoveButtonText => ChangesLabels.RemoveItemButton;

    /// <summary>行状态文本（读屏与悬浮提示用，03 篇 §9 状态双通道）。</summary>
    public string StatusText => $"{ActionText} · {OriginText} · {FromVersionText} → {ToVersionText}";

    /// <summary>把 <see cref="ChangeSetPreview"/> 投影成有序明细行：先按动作分组，再按名称排序。</summary>
    public static IReadOnlyList<ChangeRowView> Project(ChangeSetPreview? preview)
    {
        if (preview is not { Items.Count: > 0 } source)
        {
            return Array.Empty<ChangeRowView>();
        }

        var rows = new List<ChangeRowView>(source.Items.Count);
        foreach (var item in source.Items)
        {
            rows.Add(new ChangeRowView(item));
        }

        rows.Sort(CompareRows);
        return rows;
    }

    private static int CompareRows(ChangeRowView left, ChangeRowView right)
    {
        var byAction = ActionRank(left.Data.Action).CompareTo(ActionRank(right.Data.Action));
        if (byAction != 0)
        {
            return byAction;
        }

        return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
    }

    /// <summary>明细表里的动作分组顺序：先装后卸。</summary>
    private static int ActionRank(ChangeAction action) => action switch
    {
        ChangeAction.Install => 0,
        ChangeAction.Upgrade => 1,
        ChangeAction.Downgrade => 2,
        ChangeAction.Replace => 3,
        ChangeAction.Remove => 4,
        _ => 5,
    };
}
