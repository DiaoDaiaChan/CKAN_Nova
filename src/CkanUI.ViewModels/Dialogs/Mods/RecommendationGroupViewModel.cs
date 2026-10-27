using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs.Mods;

/// <summary>推荐对话框里的一组候选（推荐/建议/被支持），持有自己的"全选/全不选"。</summary>
public sealed partial class RecommendationGroupViewModel : ObservableObject
{
    /// <summary>按分组来源构造，<paramref name="items"/> 顺序即展示顺序。</summary>
    public RecommendationGroupViewModel(
        RecommendationKind kind,
        string header,
        IEnumerable<RecommendationCandidateViewModel> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentNullException.ThrowIfNull(items);
        Kind = kind;
        Header = header;
        Items = new ObservableCollection<RecommendationCandidateViewModel>(items);
        foreach (var item in Items)
        {
            item.PropertyChanged += OnItemChanged;
        }
    }

    /// <summary>分组来源。</summary>
    public RecommendationKind Kind { get; }

    /// <summary>分组标题。</summary>
    public string Header { get; }

    /// <summary>组内候选。</summary>
    public ObservableCollection<RecommendationCandidateViewModel> Items { get; }

    /// <summary>组内是否没有候选（视图据此隐藏整组）。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>组内是否有候选。</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>可勾选的候选数。</summary>
    public int SelectableCount => Items.Count(i => i.CanSelect);

    /// <summary>已勾选的候选数。</summary>
    public int SelectedCount => Items.Count(i => i.IsSelected);

    /// <summary>计数展示（如 "2/5"）。</summary>
    public string CountText => SelectedCount + "/" + Items.Count;

    /// <summary>组内全选（跳过 DLC 与冲突项）。</summary>
    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }

        Refresh();
    }

    /// <summary>组内全不选。</summary>
    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }

        Refresh();
    }

    /// <summary>重新通知派生属性（勾选变化后由对话框 VM 调用）。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectableCount));
        OnPropertyChanged(nameof(CountText));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecommendationCandidateViewModel.IsSelected))
        {
            Refresh();
        }
    }
}
