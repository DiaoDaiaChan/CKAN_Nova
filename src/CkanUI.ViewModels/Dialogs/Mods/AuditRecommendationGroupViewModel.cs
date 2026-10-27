using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs.Mods;

/// <summary>
/// 按原发起模组分组的推荐候选组（F-36 / UI-17）。
/// 包含发起模组名称、组内候选列表以及针对该组的快速勾选操作。
/// </summary>
public sealed partial class AuditRecommendationGroupViewModel : ObservableObject
{
    private readonly ObservableCollection<AuditRecommendationItemViewModel> _items;

    public AuditRecommendationGroupViewModel(
        string sourceIdentifier,
        string sourceName,
        IEnumerable<AuditRecommendationItemViewModel> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentifier);
        ArgumentNullException.ThrowIfNull(items);

        SourceIdentifier = sourceIdentifier;
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? sourceIdentifier : sourceName;
        _items = new ObservableCollection<AuditRecommendationItemViewModel>(items);
        Items = new ReadOnlyObservableCollection<AuditRecommendationItemViewModel>(_items);

        foreach (var item in _items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        Refresh();
    }

    /// <summary>发起模组标识。</summary>
    public string SourceIdentifier { get; }

    /// <summary>发起模组名称。</summary>
    public string SourceName { get; }

    /// <summary>组头标题展示。</summary>
    public string Header => string.Equals(SourceIdentifier, SourceName, StringComparison.OrdinalIgnoreCase)
        ? SourceName
        : $"{SourceName} ({SourceIdentifier})";

    /// <summary>组内候选列表。</summary>
    public ReadOnlyObservableCollection<AuditRecommendationItemViewModel> Items { get; }

    /// <summary>组内项总数。</summary>
    public int TotalCount => _items.Count;

    /// <summary>可勾选数。</summary>
    public int SelectableCount => _items.Count(i => i.CanSelect);

    /// <summary>已勾选数。</summary>
    public int SelectedCount => _items.Count(i => i.IsSelected);

    /// <summary>选择统计文案（如 "2/3"）。</summary>
    public string CountText => $"{SelectedCount}/{TotalCount}";

    /// <summary>组内是否有项。</summary>
    public bool HasItems => _items.Count > 0;

    /// <summary>组内全选。</summary>
    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in _items)
        {
            if (item.CanSelect)
            {
                item.IsSelected = true;
            }
        }

        Refresh();
    }

    /// <summary>组内全不选。</summary>
    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var item in _items)
        {
            item.IsSelected = false;
        }

        Refresh();
    }

    /// <summary>刷新组内统计。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectableCount));
        OnPropertyChanged(nameof(CountText));
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuditRecommendationItemViewModel.IsSelected))
        {
            Refresh();
        }
    }
}
