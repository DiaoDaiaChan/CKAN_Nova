using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Relations;

/// <summary>
/// 关系类别计数胶囊（F-28 / UI-18）。
/// 只呈现当前模组实际拥有的关系类别，并把数量、语义颜色与筛选状态收拢到一个可点击控件里，
/// 取代原先固定九项、与内容无关且占满整行的静态图例。
/// </summary>
public sealed partial class RelationKindChip : ObservableObject
{
    public RelationKindChip(RelationKind kind, string label, int count, string colorTokenKey, string description)
    {
        Kind = kind;
        Label = label;
        Count = count;
        ColorTokenKey = colorTokenKey;
        Description = description;
        ToolTipText = $"{label} {count} 项 · {description}（点击筛选）";
    }

    public RelationKind Kind { get; }

    public string Label { get; }

    public int Count { get; }

    public string ColorTokenKey { get; }

    public string Description { get; }

    public string ToolTipText { get; }

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>该类别当前是否处于筛选激活态。</summary>
    [ObservableProperty]
    private bool isActive;
}
