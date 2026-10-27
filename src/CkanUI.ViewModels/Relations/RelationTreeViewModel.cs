using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Relations;

/// <summary>依赖关系树投影；不持有导航或 Core 对象。</summary>
public sealed partial class RelationTreeViewModel : ObservableObject
{
    public const int MaximumDepth = 8;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> graph;
    private readonly IReadOnlyDictionary<string, string> names;
    private readonly HashSet<string> known;
    private readonly Action<string>? navigate;
    public RelationTreeViewModel(string rootIdentifier, IReadOnlyDictionary<string, IReadOnlyList<RelationEdge>> graph, IReadOnlyDictionary<string,string>? names = null, IEnumerable<string>? knownIdentifiers = null, Action<string>? navigate = null)
    { RootIdentifier=rootIdentifier; this.graph=graph ?? new Dictionary<string,IReadOnlyList<RelationEdge>>(); this.names=names ?? new Dictionary<string,string>(); known=new(knownIdentifiers ?? this.graph.Keys,StringComparer.OrdinalIgnoreCase); this.navigate=navigate; Rebuild(); }
    public string RootIdentifier { get; }
    public ObservableCollection<RelationNodeViewModel> Roots { get; } = new();
    [ObservableProperty] private bool isReverse;
    [ObservableProperty] private string statusText=string.Empty;
    partial void OnIsReverseChanged(bool value) => Rebuild();
    public void ToggleExpanded(RelationNodeViewModel node) => node.IsExpanded=!node.IsExpanded;
    public void Navigate(RelationNodeViewModel node) { if(node.Kind is not null && node.IsKnown) navigate?.Invoke(node.Identifier); }
    [RelayCommand] private void ToggleDirection() => IsReverse=!IsReverse;
    private void Rebuild() { Roots.Clear(); var root=new RelationNodeViewModel(RootIdentifier,NameOf(RootIdentifier),isKnown:known.Contains(RootIdentifier)); Roots.Add(root); Populate(root,RootIdentifier,0,new(StringComparer.OrdinalIgnoreCase){RootIdentifier}); StatusText=root.IsKnown?string.Empty:"未知"; }
    private void Populate(RelationNodeViewModel parent,string current,int depth,HashSet<string> visited)
    { IEnumerable<RelationEdge> edges=IsReverse?graph.SelectMany(pair=>pair.Value.Where(e=>e.Target==current).Select(e=>new RelationEdge(e.Kind,pair.Key))):graph.TryGetValue(current,out var values)?values:Array.Empty<RelationEdge>(); foreach(var group in edges.GroupBy(e=>e.Kind)){var category=new RelationNodeViewModel(string.Empty,Label(group.Key),group.Key);parent.Children.Add(category);foreach(var edge in group){var id=edge.Target;var isKnown=known.Contains(id);var cycle=visited.Contains(id);var truncated=depth+1>=MaximumDepth&&!cycle;var child=new RelationNodeViewModel(id,isKnown?NameOf(id):$"{id}（未知）",group.Key,isKnown,truncated);category.Children.Add(child);if(cycle||truncated||!isKnown)continue;visited.Add(id);Populate(child,id,depth+1,visited);visited.Remove(id);}} }
    private string NameOf(string id)=>names.TryGetValue(id,out var name)?name:id;
    private static string Label(RelationKind kind)=>kind switch{RelationKind.Installed=>"已安装",RelationKind.Incompatible=>"不兼容",RelationKind.Virtual=>"虚拟",RelationKind.Provides=>"提供",RelationKind.Depends=>"依赖",RelationKind.Recommends=>"推荐",RelationKind.Suggests=>"建议",RelationKind.Supports=>"支持",RelationKind.Conflicts=>"冲突",_=>"未知"};
}
