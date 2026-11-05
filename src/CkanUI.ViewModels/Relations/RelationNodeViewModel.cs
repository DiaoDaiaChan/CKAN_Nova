using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Relations;

/// <summary>关系树中的模组或关系类别节点（F-28 / UI-18）。</summary>
public sealed partial class RelationNodeViewModel(
    string identifier,
    string name,
    RelationKind? kind = null,
    bool isKnown = true,
    bool truncated = false) : ObservableObject
{
    public string Identifier { get; } = identifier;
    public string Name { get; } = name;
    public RelationKind? Kind { get; } = kind;
    public bool IsKnown { get; } = isKnown;
    public bool Truncated { get; } = truncated;

    /// <summary>该依赖目标是否已知缺失（未安装且无可用提供者）；仅依赖叶子节点有意义。</summary>
    public bool IsMissing { get; init; }

    /// <summary>分类节点的直属条目数量。</summary>
    public int Count { get; init; }

    public ObservableCollection<RelationNodeViewModel> Children { get; } = new();
    [ObservableProperty] private bool isExpanded = true;

    /// <summary>是否为分类中间节点（Identifier 为空且 Kind 有值）。</summary>
    public bool IsCategory => Kind is not null && string.IsNullOrEmpty(Identifier);

    /// <summary>是否拥有关系种类。</summary>
    public bool HasKind => Kind is not null;

    /// <summary>关系语义徽章文本（如 [依赖]、[冲突]）。</summary>
    public string BadgeText => Kind switch
    {
        RelationKind.Depends => "[依赖]",
        RelationKind.Recommends => "[推荐]",
        RelationKind.Suggests => "[建议]",
        RelationKind.Supports => "[支持]",
        RelationKind.Conflicts => "[冲突]",
        RelationKind.Provides => "[提供]",
        RelationKind.Virtual => "[虚拟]",
        RelationKind.Incompatible => "[不兼容]",
        RelationKind.Installed => "[已安装]",
        _ => string.Empty
    };

    /// <summary>是否具有徽章文本。</summary>
    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    /// <summary>是否在视图中显示分类语义徽章。</summary>
    public bool ShowBadge => IsCategory && HasBadge;

    /// <summary>是否在视图中显示叶子模组关系彩色圆点。</summary>
    public bool ShowDot => HasKind && !IsCategory;

    /// <summary>分类节点的条目计数文本（如 3）。</summary>
    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>是否在分类节点上显示条目计数（信息密度：一眼看出每类多少项）。</summary>
    public bool ShowCount => IsCategory && Count > 0;

    /// <summary>依赖叶子节点是否已知缺失，需要在行尾显示「缺失」状态。</summary>
    public bool ShowMissing => !IsCategory && Kind == RelationKind.Depends && IsMissing;

    /// <summary>叶子节点标识符（与名称不同时作为副标题展示，便于检索与核对）。</summary>
    public string IdentifierText => Identifier;

    /// <summary>是否显示标识符副标题。</summary>
    public bool ShowIdentifier => !IsCategory
        && !string.IsNullOrWhiteSpace(Identifier)
        && !string.Equals(Name, Identifier, StringComparison.Ordinal);

    /// <summary>对应主题 Token 画刷键（完全对齐 Tokens.axaml 与九色图例）。</summary>
    public string ColorTokenKey => Kind switch
    {
        RelationKind.Depends => "CkanRelationDependsBrush",
        RelationKind.Recommends => "CkanRelationRecommendsBrush",
        RelationKind.Suggests => "CkanRelationSuggestsBrush",
        RelationKind.Supports => "CkanRelationSupportsBrush",
        RelationKind.Conflicts => "CkanRelationConflictsBrush",
        RelationKind.Provides => "CkanRelationProvidesBrush",
        RelationKind.Virtual => "CkanRelationVirtualBrush",
        RelationKind.Incompatible => "CkanRelationIncompatibleBrush",
        RelationKind.Installed => "CkanRelationInstalledBrush",
        _ => "CkanTextPrimaryBrush"
    };
}
