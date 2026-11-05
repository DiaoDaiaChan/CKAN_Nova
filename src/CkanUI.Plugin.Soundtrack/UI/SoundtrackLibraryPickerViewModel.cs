using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Soundtrack.UI;

/// <summary>
/// 独立乐曲库窗口的状态：搜索、扩展名筛选、只看未加入、排序，以及把一行加回页面选中列表。
/// </summary>
/// <remarks>
/// 行对象由页面共享，不在这里复制模型；加入后行上的 IsAdded 会立刻反映到页面和窗口两边。
/// 窗口只负责挑歌，真正的重名检查和写入仍走 <see cref="SoundtrackPageViewModel.AddTrack"/>。
/// </remarks>
public sealed partial class SoundtrackLibraryPickerViewModel : ObservableObject
{
    private const string AllExtensions = "全部扩展名";
    private const string SortByName = "名称 A→Z";
    private const string SortBySizeAscending = "大小 小→大";
    private const string SortBySizeDescending = "大小 大→小";
    private const string SortByPath = "路径 A→Z";

    private readonly IReadOnlyList<SoundtrackLibraryRowViewModel> _all;
    private readonly Func<SoundtrackLibraryRowViewModel, bool> _add;
    private readonly Action<SoundtrackLibraryRowViewModel>? _play;

    public SoundtrackLibraryPickerViewModel(
        IEnumerable<SoundtrackLibraryRowViewModel> rows,
        Func<SoundtrackLibraryRowViewModel, bool> add,
        Action<SoundtrackLibraryRowViewModel>? play = null,
        string playlistName = "")
    {
        _all = rows.ToList();
        _add = add ?? throw new ArgumentNullException(nameof(add));
        _play = play;
        PlaylistName = string.IsNullOrWhiteSpace(playlistName) ? "未选择播放列表" : playlistName;

        ExtensionFilters.Add(AllExtensions);
        foreach (var extension in _all
                     .Select(row => row.Extension)
                     .Where(extension => !string.IsNullOrWhiteSpace(extension))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase))
        {
            ExtensionFilters.Add(extension);
        }

        SortModes.Add(SortByName);
        SortModes.Add(SortBySizeAscending);
        SortModes.Add(SortBySizeDescending);
        SortModes.Add(SortByPath);

        RefreshRows();
    }

    public string PlaylistName { get; }

    public string HeaderText => $"目标列表：{PlaylistName}";

    public ObservableCollection<SoundtrackLibraryRowViewModel> Rows { get; } = new();

    public ObservableCollection<string> ExtensionFilters { get; } = new();

    public ObservableCollection<string> SortModes { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _extensionFilter = AllExtensions;

    [ObservableProperty]
    private bool _onlyUnadded;

    [ObservableProperty]
    private string _sortMode = SortByName;

    [ObservableProperty]
    private SoundtrackLibraryRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public string SummaryText
        => $"共 {_all.Count} 首 · 当前显示 {Rows.Count} 首";

    public bool HasRows => Rows.Count > 0;

    partial void OnSearchTextChanged(string value) => RefreshRows();

    partial void OnExtensionFilterChanged(string value) => RefreshRows();

    partial void OnOnlyUnaddedChanged(bool value) => RefreshRows();

    partial void OnSortModeChanged(string value) => RefreshRows();

    [RelayCommand]
    private void Add(SoundtrackLibraryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsAdded)
        {
            StatusText = $"「{row.Name}」已经在「{PlaylistName}」里了。";
            return;
        }

        var added = _add(row);
        StatusText = added
            ? $"已加入「{row.Name}」。"
            : $"「{row.Name}」没有加入：可能已经在列表里，或页面没有选中播放列表。";
        OnPropertyChanged(nameof(SummaryText));
        RefreshRows();
    }

    [RelayCommand]
    private void Play(SoundtrackLibraryRowViewModel? row)
    {
        if (row is not null)
        {
            _play?.Invoke(row);
        }
    }

    private void RefreshRows()
    {
        var query = SearchText.Trim();
        IEnumerable<SoundtrackLibraryRowViewModel> rows = _all;

        if (query.Length > 0)
        {
            rows = rows.Where(row =>
                row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (OnlyUnadded)
        {
            rows = rows.Where(row => !row.IsAdded);
        }

        if (!string.Equals(ExtensionFilter, AllExtensions, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(ExtensionFilter))
        {
            rows = rows.Where(row =>
                string.Equals(row.Extension, ExtensionFilter, StringComparison.OrdinalIgnoreCase));
        }

        rows = SortMode switch
        {
            SortBySizeAscending => rows.OrderBy(row => row.Model.SizeBytes),
            SortBySizeDescending => rows.OrderByDescending(row => row.Model.SizeBytes),
            SortByPath => rows.OrderBy(row => row.FullPath, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
        };

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        if (SelectedRow is not null && !Rows.Contains(SelectedRow))
        {
            SelectedRow = null;
        }

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasRows));
    }
}
