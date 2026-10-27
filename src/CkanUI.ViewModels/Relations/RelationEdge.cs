namespace CkanUI.ViewModels.Relations;

/// <summary>一个模组指向关系目标的有向边。</summary>
public sealed record RelationEdge(RelationKind Kind, string Target);
