using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Relations;

/// <summary>
/// 可视化关系网节点模型（F-28 / UI-18 / 空间天体拓扑设计）。
/// 承载节点标识、显示名、语义关系类别、天体轨道层级与坐标，以及悬停选中等交互状态。
/// </summary>
public sealed partial class RelationGraphNode : ObservableObject
{
    public RelationGraphNode(
        string identifier,
        string name,
        RelationKind kind,
        bool isRoot = false,
        bool isKnown = true,
        bool isSatisfied = true)
    {
        var id = identifier ?? string.Empty;
        Identifier = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
        Kind = kind;
        IsRoot = isRoot;
        IsKnown = isKnown;
        IsSatisfied = isSatisfied;
    }

    public string Identifier { get; }
    public string Name { get; }
    public RelationKind Kind { get; }
    public bool IsRoot { get; }
    public bool IsKnown { get; }
    public bool IsSatisfied { get; }

    /// <summary>天体轨道层级：0 = 中心主星 (Root), 1 = 内圈引力轨道 (Depends), 2 = 中圈推荐拓展 (Recommends/Suggests/Supports), 3 = 外圈冲突威胁 (Conflicts/Incompatible)</summary>
    public int OrbitRing { get; set; }

    /// <summary>在轨道上的相对角度（弧度值：0 ~ 2π）</summary>
    public double OrbitAngle { get; set; }

    /// <summary>节点在图谱坐标系下的 X 坐标</summary>
    public double X { get; set; }

    /// <summary>节点在图谱坐标系下的 Y 坐标</summary>
    public double Y { get; set; }

    /// <summary>天体核心半径（像素）</summary>
    public double Radius { get; set; } = 20.0;

    [ObservableProperty]
    private bool isHovered;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isFiltered;

    /// <summary>语义关系分类徽章文案（如 [依赖]、[冲突]）</summary>
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

    /// <summary>显示标题</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? Identifier : Name;

    /// <summary>副标题与标识展示</summary>
    public string SubtitleText => string.IsNullOrWhiteSpace(Name) || string.Equals(Name, Identifier, StringComparison.OrdinalIgnoreCase)
        ? BadgeText
        : $"{BadgeText} {Identifier}";
}
