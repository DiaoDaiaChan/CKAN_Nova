using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Relations;

/// <summary>
/// 依赖关系树投影视图模型（F-28 / UI-18）；不持有导航或 Core 对象。
/// 支持九色图例展示、正向/反向关系构建切换、循环依赖截断保护与节点双击导航。
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
    private readonly Action<string>? navigate;

    public RelationTreeViewModel(
        string rootIdentifier,
        IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> graph,
        IReadOnlyDictionary<string, string>? names = null,
        IEnumerable<string>? knownIdentifiers = null,
        Action<string>? navigate = null)
    {
        RootIdentifier = rootIdentifier;
        this.graph = graph ?? new Dictionary<string, IReadOnlyList<RelationEdge>>();
        this.names = names ?? new Dictionary<string, string>();
        known = new(knownIdentifiers ?? this.graph.Keys, StringComparer.OrdinalIgnoreCase);
        this.navigate = navigate;
        Rebuild();
    }

    public string RootIdentifier { get; }

    public ObservableCollection<RelationNodeViewModel> Roots { get; } = new();

    public IReadOnlyList<RelationLegendItem> Legends => DefaultLegends;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectionButtonText))]
    [NotifyPropertyChangedFor(nameof(DirectionStatusText))]
    private bool isReverse;

    [ObservableProperty]
    private string statusText = string.Empty;

    public string DirectionButtonText => IsReverse ? "切换为正向关系 (依赖的模组)" : "切换为反向关系 (被依赖的模组)";

    public string DirectionStatusText => IsReverse ? "反向关系模式（谁依赖/推荐/冲突当前模组）" : "正向关系模式（当前模组的依赖/推荐/冲突）";

    partial void OnIsReverseChanged(bool value) => Rebuild();

    public void ToggleExpanded(RelationNodeViewModel node) => node.IsExpanded = !node.IsExpanded;

    public void Navigate(RelationNodeViewModel node)
    {
        if (node.Kind is not null && node.IsKnown && !string.IsNullOrWhiteSpace(node.Identifier))
        {
            navigate?.Invoke(node.Identifier);
        }
    }

    [RelayCommand]
    private void ToggleDirection() => IsReverse = !IsReverse;

    private void Rebuild()
    {
        Roots.Clear();
        var root = new RelationNodeViewModel(RootIdentifier, NameOf(RootIdentifier), isKnown: known.Contains(RootIdentifier));
        Roots.Add(root);
        Populate(root, RootIdentifier, 0, new(StringComparer.OrdinalIgnoreCase) { RootIdentifier });
        StatusText = root.IsKnown ? string.Empty : "未知";
    }

    private IEnumerable<RelationEdge> GetEdges(string current)
    {
        if (IsReverse)
        {
            return graph.SelectMany(pair => pair.Value
                .Where(e => string.Equals(e.Target, current, StringComparison.OrdinalIgnoreCase))
                .Select(e => new RelationEdge(e.Kind, pair.Key)));
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
            var category = new RelationNodeViewModel(string.Empty, Label(group.Key, IsReverse), group.Key);
            parent.Children.Add(category);

            foreach (var edge in group)
            {
                var id = edge.Target;
                var isKnown = known.Contains(id);
                var cycle = visited.Contains(id);
                var truncated = depth + 1 >= MaximumDepth && !cycle;
                var child = new RelationNodeViewModel(id, isKnown ? NameOf(id) : $"{id}（未知）", group.Key, isKnown, truncated);
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
