using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 安装文件过滤器对话框视图模型（F-16 / UI-31）。
/// </summary>
public sealed partial class InstallFiltersDialogViewModel : DialogViewModelBase
{
    private readonly IInstallFiltersService _filtersService;

    [ObservableProperty]
    private string _newFilterText = string.Empty;

    [ObservableProperty]
    private FilterItemViewModel? _selectedFilter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    public ObservableCollection<FilterItemViewModel> Filters { get; } = new();

    public InstallFiltersDialogViewModel(IInstallFiltersService filtersService)
        : base("安装文件过滤器")
    {
        _filtersService = filtersService ?? throw new ArgumentNullException(nameof(filtersService));

        PrimaryButtonText = "保存";
        SecondaryButtonText = "取消";
    }

    /// <summary>
    /// 初始化加载过滤器规则。
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        ValidationError = null;
        try
        {
            var list = await _filtersService.LoadAsync().ConfigureAwait(true);
            Filters.Clear();
            foreach (var item in list)
            {
                Filters.Add(new FilterItemViewModel(item, RemoveFilter));
            }
        }
        catch (Exception ex)
        {
            ValidationError = $"加载过滤器失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 添加新规则。
    /// </summary>
    [RelayCommand]
    public void AddFilter()
    {
        ValidationError = null;
        var text = NewFilterText?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            ValidationError = "过滤规则不能为空。";
            return;
        }

        if (Filters.Any(f => string.Equals(f.Pattern, text, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationError = $"规则“{text}”已存在，无需重复添加。";
            return;
        }

        Filters.Add(new FilterItemViewModel(text, RemoveFilter));
        NewFilterText = string.Empty;
    }

    /// <summary>
    /// 移除单条规则。
    /// </summary>
    [RelayCommand]
    public void RemoveFilter(FilterItemViewModel? item)
    {
        if (item is null) return;
        Filters.Remove(item);
        ValidationError = null;
    }

    /// <summary>
    /// 恢复默认规则列表。
    /// </summary>
    [RelayCommand]
    public void ResetDefaults()
    {
        ValidationError = null;
        var defaults = _filtersService.GetDefaultFilters();
        Filters.Clear();
        foreach (var item in defaults)
        {
            Filters.Add(new FilterItemViewModel(item, RemoveFilter));
        }
    }

    /// <summary>
    /// 保存并持久化规则。
    /// </summary>
    public async Task SaveAsync()
    {
        ValidationError = null;
        IsSaving = true;
        try
        {
            var list = Filters.Select(f => f.Pattern.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            await _filtersService.SaveAsync(list).ConfigureAwait(true);
            IsConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            ValidationError = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <inheritdoc />
    protected override void OnPrimary()
    {
        _ = SaveAsync();
    }

    /// <inheritdoc />
    protected override bool CloseAfterPrimary => false;
}

/// <summary>
/// 过滤器条目视图模型。
/// </summary>
public sealed partial class FilterItemViewModel : ObservableObject
{
    private readonly Action<FilterItemViewModel> _removeAction;

    [ObservableProperty]
    private string _pattern;

    public FilterItemViewModel(string pattern, Action<FilterItemViewModel> removeAction)
    {
        _pattern = pattern;
        _removeAction = removeAction ?? throw new ArgumentNullException(nameof(removeAction));
    }

    [RelayCommand]
    public void Remove() => _removeAction(this);
}
