using System.Globalization;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Repos;

/// <summary>
/// 仓库列表行（03 篇 §6 V-04）：一行的全部显示文案在构造时算好，视图只做绑定，无头测试只断言文案。
/// </summary>
/// <remarks>不可变：列表刷新时整表替换（04 篇 §2.1 铁律 7），不做增量 <c>CollectionChanged</c>。</remarks>
public sealed record RepositoryRowViewModel
{
    /// <summary>仓库名（注册表里的键，保存时用它定位）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>索引地址（<c>.sNETKAN</c> 或 <c>.ckan</c> 源）。</summary>
    public string Uri { get; init; } = string.Empty;

    /// <summary>优先级（0 最高；数值小者覆盖大者的元数据）。</summary>
    public int Priority { get; init; }

    /// <summary>上次成功刷新时间；<c>null</c> 表示从未刷新或无法判定。</summary>
    public DateTime? LastUpdatedAt { get; init; }

    /// <summary>超过 <c>TimeTillStale</c>（3 天）未刷新。</summary>
    public bool IsStale { get; init; }

    /// <summary>超过 <c>TimeTillVeryStale</c>（14 天）未刷新。</summary>
    public bool IsVeryStale { get; init; }

    /// <summary>是本游戏的官方默认仓库。</summary>
    public bool IsDefault { get; init; }

    /// <summary>最近一次刷新失败。</summary>
    public bool HasFailure { get; init; }

    /// <summary>失败原因（有 <see cref="HasFailure"/> 时才可能有值）。</summary>
    public string? FailureMessage { get; init; }

    /// <summary>可以上移（不在首位）。</summary>
    public bool CanMoveUp { get; init; }

    /// <summary>可以下移（不在末位）。</summary>
    public bool CanMoveDown { get; init; }

    /// <summary>由服务层快照投影出一行。</summary>
    public static RepositoryRowViewModel From(RepositoryEntryInfo info, bool canMoveUp, bool canMoveDown)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new RepositoryRowViewModel
        {
            Name = info.Name,
            Uri = info.Uri,
            Priority = info.Priority,
            LastUpdatedAt = info.LastUpdatedAt,
            IsStale = info.IsStale,
            IsVeryStale = info.IsVeryStale,
            IsDefault = info.IsDefault,
            HasFailure = info.HasFailure,
            FailureMessage = info.FailureMessage,
            CanMoveUp = canMoveUp,
            CanMoveDown = canMoveDown,
        };
    }

    /// <summary>显示名（与注册表键一致）。</summary>
    public string DisplayName => Name;

    /// <summary>「第 N 位」——对外从 1 开始，内部 priority 从 0 开始。</summary>
    public string PriorityText => $"第 {Priority + 1} 位";

    /// <summary>是否已知上次刷新时间。</summary>
    public bool HasUpdated => LastUpdatedAt is not null;

    /// <summary>「上次刷新 …」或「尚未刷新」。</summary>
    public string UpdatedText => LastUpdatedAt is { } at
        ? "上次刷新 " + at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : "尚未刷新";

    /// <summary>是否需要提示刷新（3 天以上）。</summary>
    public bool NeedsRefresh => IsStale && !HasFailure;

    /// <summary>状态文案：刷新失败 &gt; 已过期 &gt; 需刷新 &gt; 正常。</summary>
    public string StatusText => HasFailure
        ? "刷新失败"
        : IsVeryStale
            ? "已过期"
            : IsStale
                ? "需刷新"
                : "正常";

    /// <summary>失败详情文案（无失败时为空串）。</summary>
    public string FailureText => FailureMessage ?? (HasFailure ? "刷新失败。" : string.Empty);

    /// <summary>「官方默认」角标文案（非默认时为空串，视图据此隐藏）。</summary>
    public string DefaultText => IsDefault ? "官方默认" : string.Empty;

    /// <summary>是否显示「官方默认」角标。</summary>
    public bool ShowDefaultBadge => IsDefault;
}
