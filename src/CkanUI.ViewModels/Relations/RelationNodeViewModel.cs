using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Relations;

/// <summary>关系树中的模组或关系类别节点。</summary>
public sealed partial class RelationNodeViewModel(string identifier, string name, RelationKind? kind = null, bool isKnown = true, bool truncated = false) : ObservableObject
{
    public string Identifier { get; } = identifier;
    public string Name { get; } = name;
    public RelationKind? Kind { get; } = kind;
    public bool IsKnown { get; } = isKnown;
    public bool Truncated { get; } = truncated;
    public ObservableCollection<RelationNodeViewModel> Children { get; } = new();
    [ObservableProperty] private bool isExpanded = true;
}
