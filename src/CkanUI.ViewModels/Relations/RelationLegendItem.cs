namespace CkanUI.ViewModels.Relations;

/// <summary>
/// 模组依赖关系树九色图例展示项（F-28 / UI-18）。
/// </summary>
public sealed record RelationLegendItem(
    RelationKind Kind,
    string Label,
    string BadgeText,
    string ColorTokenKey,
    string Description = "");
