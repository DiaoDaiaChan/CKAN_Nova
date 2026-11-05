using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Relations;

/// <summary>
/// 依赖关系树与可视化关系网投影视图模型（F-28 / UI-18）；不持有导航或 Core 对象。
/// 详情面板默认为紧凑树视图（参考 CKAN GUI 的 DependsGraphTree），关系网作为可选的轨道画布；
/// 支持按分类计数筛选、正向/反向关系切换、循环依赖截断保护及节点双击导航。
/// </summary>
public sealed partial class RelationTreeViewModel : ObservableObject
{
    public const int MaximumDepth = 8;

    public static IReadOnlyList<RelationLegendItem> DefaultLegends { get; } = new RelationLegendItem[]
    {
        new(RelationKind.Depends, "依赖", "[依赖]", "CkanRelationDependsBrush", "运行必需的前置依赖模组"),
        new(RelationKind.Recommends, "推荐", "[推荐]", "CkanRelationRecommendsBrush", "官方或作者推荐搭配使用的模组"),
        new(RelationKind.Suggests, "建议", "[建议]", "CkanRelationSuggestsBrush", "可选的功能增强或辅助模组"),
        new(RelationKind.Supports, "支持", "[支持]", "CkanRelationSupportsBrush", "兼容并提供扩展支持的模组"),
        new(RelationKind.Conflicts, "冲突", "[冲突]", "CkanRelationConflictsBrush", "与本模组冲突不可同时安装"),
        new(RelationKind.Provides, "提供", "[提供]", "CkanRelationProvidesBrush", "提供通用虚拟特性或接口"),
        new(RelationKind.Virtual, "虚拟", "[虚拟]", "CkanRelationVirtualBrush", "由具体模组实现的抽象规范节点"),
        new(RelationKind.Incompatible, "不兼容", "[不兼容]", "CkanRelationIncompatibleBrush", "版本或环境不满足当前运行要求"),
        new(RelationKind.Installed, "已安装", "[已安装]", "CkanRelationInstalledBrush", "本地当前已就绪安装的模组"),
    };

    private readonly IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> graph;
    private readonly IReadOnlyDictionary<string, string> names;
    private readonly HashSet<string> known;
    private readonly HashSet<string> missing;
    private readonly Action<string>? navigate;

    public RelationTreeViewModel(
        string rootIdentifier,
        IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> graph,
        IReadOnlyDictionary<string, string>? names = null,
        IEnumerable<string>? knownIdentifiers = null,
        Action<string>? navigate = null,
        IEnumerable<string>? missingIdentifiers = null)
    {
        RootIdentifier = rootIdentifier;
        this.graph = graph ?? new Dictionary<string, IReadOnlyList<RelationEdge>>();
        this.names = names ?? new Dictionary<string, string>();
        known = new(knownIdentifiers ?? this.graph.Keys, StringComparer.OrdinalIgnoreCase);
        missing = new(missingIdentifiers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        this.navigate = navigate;
        Rebuild();
    }

    public string RootIdentifier { get; }

    /// <summary>经典树状视图节点根集合（支持向下展开与循环依赖截断）。</summary>
    public ObservableCollection<RelationNodeViewModel> Roots { get; } = new();

    /// <summary>可视化天体轨道关系网节点集合。</summary>
    public ObservableCollection<RelationGraphNode> GraphNodes { get; } = new();

    /// <summary>可视化天体轨道关系网引力连线集合。</summary>
    public ObservableCollection<RelationGraphEdge> GraphEdges { get; } = new();

    /// <summary>当前实际存在的分类计数胶囊（同时充当筛选入口，仅展示有内容的分类）。</summary>
    public ObservableCollection<RelationKindChip> KindChips { get; } = new();

    public IReadOnlyList<RelationLegendItem> Legends => DefaultLegends;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectionButtonText))]
    [NotifyPropertyChangedFor(nameof(DirectionStatusText))]
    [NotifyPropertyChangedFor(nameof(DirectionChipText))]
    private bool isReverse;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>当前视图模式：true 为可视化关系网 (Graph)，false 为紧凑层级树 (Tree)。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeButtonText))]
    [NotifyPropertyChangedFor(nameof(IsTreeView))]
    private bool isGraphView;

    /// <summary>当前选中的图谱节点。</summary>
    [ObservableProperty]
    private RelationGraphNode? selectedGraphNode;

    /// <summary>当前激活的分类筛选（null 表示全量展示）。</summary>
    [ObservableProperty]
    private RelationKind? activeKindFilter;

    public string DirectionButtonText => IsReverse ? "切换为正向关系 (依赖的模组)" : "切换为反向关系 (被依赖的模组)";

    public string DirectionStatusText => IsReverse ? "反向关系模式（谁依赖/推荐/冲突当前模组）" : "正向关系模式（当前模组的依赖/推荐/冲突）";

    public string ViewModeButtonText => IsGraphView ? "树状图" : "关系网";

    public bool IsTreeView => !IsGraphView;

    /// <summary>方向切换胶囊的短文案。</summary>
    public string DirectionChipText => IsReverse ? "反向关系" : "正向关系";

    /// <summary>只有图谱包含多个模组的边时反向关系才有数据，详情面板（单键图谱）下隐藏反向切换。</summary>
    public bool CanToggleDirection => graph.Count > 1;

    public bool ShowDirectionStatus => CanToggleDirection;

    public bool HasKindChips => KindChips.Count > 0;

    public bool IsEmpty => Roots.Count == 0 || Roots[0].Children.Count == 0;

    public int MissingDependencyCount => GetEdges(RootIdentifier)
        .Count(e => e.Kind == RelationKind.Depends && !IsSatisfiedEdge(e));

    public bool HasMissingDependencies => MissingDependencyCount > 0;

    public string MissingDependencyText => MissingDependencyCount == 0
        ? "全部必需依赖均已满足"
        : $"{MissingDependencyCount} 项必需依赖尚未满足";

    partial void OnIsReverseChanged(bool value) => Rebuild();

    partial void OnActiveKindFilterChanged(RelationKind? value)
    {
        ApplyFilter();
        RebuildTree();
        SyncChipState();
        NotifyDerived();
    }

    public void ToggleExpanded(RelationNodeViewModel node) => node.IsExpanded = !node.IsExpanded;

    public void Navigate(RelationNodeViewModel node)
    {
        if (node.Kind is not null && node.IsKnown && !string.IsNullOrWhiteSpace(node.Identifier))
        {
            navigate?.Invoke(node.Identifier);
        }
    }

    [RelayCommand]
    public void NavigateById(string? identifier)
    {
        if (!string.IsNullOrWhiteSpace(identifier) && known.Contains(identifier))
        {
            navigate?.Invoke(identifier);
        }
    }

    [RelayCommand]
    private void ToggleDirection() => IsReverse = !IsReverse;

    [RelayCommand]
    private void ToggleViewMode() => IsGraphView = !IsGraphView;

    [RelayCommand]
    private void ShowTreeView() => IsGraphView = false;

    [RelayCommand]
    private void ShowGraphView() => IsGraphView = true;

    [RelayCommand]
    public void FilterKind(RelationKind? kind)
    {
        ActiveKindFilter = ActiveKindFilter == kind ? null : kind;
    }

    private void Rebuild()
    {
        RebuildTree();
        RebuildGraph();
        RebuildChips();
        NotifyDerived();
    }

    private void RebuildChips()
    {
        var counts = new Dictionary<RelationKind, int>();
        foreach (var edge in GetEdges(RootIdentifier))
        {
            counts.TryGetValue(edge.Kind, out var current);
            counts[edge.Kind] = current + 1;
        }

        KindChips.Clear();
        foreach (var legend in DefaultLegends)
        {
            if (!counts.TryGetValue(legend.Kind, out var count) || count <= 0)
            {
                continue;
            }

            KindChips.Add(new RelationKindChip(legend.Kind, legend.Label, count, legend.ColorTokenKey, legend.Description)
            {
                IsActive = ActiveKindFilter == legend.Kind
            });
        }
    }

    private void SyncChipState()
    {
        foreach (var chip in KindChips)
        {
            chip.IsActive = ActiveKindFilter == chip.Kind;
        }
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(HasKindChips));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(MissingDependencyCount));
        OnPropertyChanged(nameof(HasMissingDependencies));
        OnPropertyChanged(nameof(MissingDependencyText));
        OnPropertyChanged(nameof(CanToggleDirection));
        OnPropertyChanged(nameof(ShowDirectionStatus));
    }

    private void RebuildTree()
    {
        Roots.Clear();
        var root = new RelationNodeViewModel(RootIdentifier, NameOf(RootIdentifier), isKnown: known.Contains(RootIdentifier));
        Roots.Add(root);
        Populate(root, RootIdentifier, 0, new(StringComparer.OrdinalIgnoreCase) { RootIdentifier });
        StatusText = root.IsKnown ? string.Empty : "未知";
    }

    private void RebuildGraph()
    {
        GraphNodes.Clear();
        GraphEdges.Clear();

        var isRootKnown = known.Contains(RootIdentifier);
        var rootNode = new RelationGraphNode(
            RootIdentifier,
            NameOf(RootIdentifier),
            RelationKind.Installed,
            isRoot: true,
            isKnown: isRootKnown)
        {
            OrbitRing = 0,
            OrbitAngle = 0,
            X = 0,
            Y = 0,
            Radius = 26.0
        };
        GraphNodes.Add(rootNode);

        var directEdges = GetEdges(RootIdentifier).ToList();
        if (directEdges.Count == 0)
        {
            return;
        }

        // 按语义分层归入同心引力轨道：
        // 轨道 1 (内圈 - 110px)：强硬依赖 (Depends)
        // 轨道 2 (中圈 - 190px)：推荐、建议、支持与虚拟特性 (Recommends, Suggests, Supports, Provides, Virtual)
        // 轨道 3 (外圈 - 270px)：冲突与不兼容警告 (Conflicts, Incompatible)
        var ring1Edges = new List<RelationEdge>();
        var ring2Edges = new List<RelationEdge>();
        var ring3Edges = new List<RelationEdge>();

        var distinctTargetMap = new Dictionary<string, RelationEdge>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in directEdges)
        {
            if (string.Equals(edge.Target, RootIdentifier, StringComparison.OrdinalIgnoreCase)) continue;

            if (distinctTargetMap.TryGetValue(edge.Target, out _))
            {
                // 保留优先级更高的分类 (Conflicts > Depends > Recommends > Others)
                if (edge.Kind == RelationKind.Conflicts || edge.Kind == RelationKind.Incompatible)
                {
                    distinctTargetMap[edge.Target] = edge;
                }
            }
            else
            {
                distinctTargetMap[edge.Target] = edge;
            }
        }

        foreach (var edge in distinctTargetMap.Values)
        {
            switch (edge.Kind)
            {
                case RelationKind.Depends:
                    ring1Edges.Add(edge);
                    break;
                case RelationKind.Conflicts:
                case RelationKind.Incompatible:
                    ring3Edges.Add(edge);
                    break;
                default:
                    ring2Edges.Add(edge);
                    break;
            }
        }

        LayoutRing(rootNode, ring1Edges, ringIndex: 1, baseRadius: 110.0, baseAngleOffset: 0.0);
        LayoutRing(rootNode, ring2Edges, ringIndex: 2, baseRadius: 190.0, baseAngleOffset: Math.PI / 4.0);
        LayoutRing(rootNode, ring3Edges, ringIndex: 3, baseRadius: 270.0, baseAngleOffset: Math.PI / 6.0);

        ApplyFilter();
    }

    private void LayoutRing(
        RelationGraphNode rootNode,
        IReadOnlyList<RelationEdge> edges,
        int ringIndex,
        double baseRadius,
        double baseAngleOffset)
    {
        if (edges.Count == 0) return;

        var step = (2.0 * Math.PI) / edges.Count;
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            var id = edge.Target;
            var isKnown = known.Contains(id);
            var angle = baseAngleOffset + (i * step);
            var x = baseRadius * Math.Cos(angle);
            var y = baseRadius * Math.Sin(angle);

            var node = new RelationGraphNode(
                id,
                isKnown ? NameOf(id) : $"{id}（未知）",
                edge.Kind,
                isRoot: false,
                isKnown: isKnown,
                isSatisfied: edge.IsSatisfied)
            {
                OrbitRing = ringIndex,
                OrbitAngle = angle,
                X = x,
                Y = y,
                Radius = 18.0
            };

            GraphNodes.Add(node);
            GraphEdges.Add(new RelationGraphEdge(rootNode, node, edge.Kind));
        }
    }

    /// <summary>边的满足状态：显式标记优先，其次回落到调用方给出的缺失集合。</summary>
    private bool IsSatisfiedEdge(RelationEdge edge) => edge.IsSatisfied && !missing.Contains(edge.Target);

    private void ApplyFilter()
    {
        foreach (var node in GraphNodes)
        {
            if (node.IsRoot)
            {
                node.IsFiltered = false;
            }
            else
            {
                node.IsFiltered = ActiveKindFilter.HasValue && node.Kind != ActiveKindFilter.Value;
            }
        }
    }

    private IEnumerable<RelationEdge> GetEdges(string current)
    {
        if (IsReverse)
        {
            return graph.SelectMany(pair => pair.Value
                .Where(e => string.Equals(e.Target, current, StringComparison.OrdinalIgnoreCase))
                .Select(e => new RelationEdge(e.Kind, pair.Key, e.IsSatisfied)));
        }

        if (graph.TryGetValue(current, out var values))
        {
            return values;
        }

        foreach (var (key, edges) in graph)
        {
            if (string.Equals(key, current, StringComparison.OrdinalIgnoreCase))
            {
                return edges;
            }
        }

        return Array.Empty<RelationEdge>();
    }

    private void Populate(RelationNodeViewModel parent, string current, int depth, HashSet<string> visited)
    {
        var edges = GetEdges(current);
        foreach (var group in edges.GroupBy(e => e.Kind))
        {
            if (ActiveKindFilter.HasValue && group.Key != ActiveKindFilter.Value)
            {
                continue;
            }

            var category = new RelationNodeViewModel(string.Empty, Label(group.Key, IsReverse), group.Key)
            {
                Count = group.Count()
            };
            parent.Children.Add(category);

            foreach (var edge in group)
            {
                var id = edge.Target;
                var isKnown = known.Contains(id);
                var cycle = visited.Contains(id);
                var truncated = depth + 1 >= MaximumDepth && !cycle;
                var child = new RelationNodeViewModel(id, isKnown ? NameOf(id) : $"{id}（未知）", group.Key, isKnown, truncated)
                {
                    IsMissing = group.Key == RelationKind.Depends && !IsSatisfiedEdge(edge)
                };
                category.Children.Add(child);

                if (cycle || truncated || !isKnown)
                {
                    continue;
                }

                visited.Add(id);
                Populate(child, id, depth + 1, visited);
                visited.Remove(id);
            }
        }
    }

    private string NameOf(string id)
    {
        if (names.TryGetValue(id, out var name))
        {
            return name;
        }

        foreach (var (key, val) in names)
        {
            if (string.Equals(key, id, StringComparison.OrdinalIgnoreCase))
            {
                return val;
            }
        }

        return id;
    }

    public static string Label(RelationKind kind, bool isReverse = false) => isReverse
        ? kind switch
        {
            RelationKind.Installed => "已安装",
            RelationKind.Incompatible => "谁与我不兼容",
            RelationKind.Virtual => "虚拟",
            RelationKind.Provides => "谁提供了我",
            RelationKind.Depends => "谁依赖了我",
            RelationKind.Recommends => "谁推荐了我",
            RelationKind.Suggests => "谁建议了我",
            RelationKind.Supports => "谁支持了我",
            RelationKind.Conflicts => "谁与我冲突",
            _ => "未知"
        }
        : kind switch
        {
            RelationKind.Installed => "已安装",
            RelationKind.Incompatible => "不兼容",
            RelationKind.Virtual => "虚拟",
            RelationKind.Provides => "提供",
            RelationKind.Depends => "依赖",
            RelationKind.Recommends => "推荐",
            RelationKind.Suggests => "建议",
            RelationKind.Supports => "支持",
            RelationKind.Conflicts => "冲突",
            _ => "未知"
        };
}
