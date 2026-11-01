namespace CkanUI.ViewModels.Relations;

/// <summary>
/// 可视化关系网连接线模型（引力/依赖链路拓扑）。
/// </summary>
public sealed class RelationGraphEdge
{
    public RelationGraphNode Source { get; }
    public RelationGraphNode Target { get; }
    public RelationKind Kind { get; }

    public RelationGraphEdge(RelationGraphNode source, RelationGraphNode target, RelationKind kind)
    {
        Source = source;
        Target = target;
        Kind = kind;
    }
}
