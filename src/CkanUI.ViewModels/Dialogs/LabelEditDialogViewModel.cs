using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 标签编辑条目项（供列表绑定与本地草稿编辑）。
/// </summary>
public sealed partial class LabelEditItemViewModel : ObservableObject
{
    private readonly string? _originalName;

    public LabelEditItemViewModel(LabelInfo info)
    {
        _originalName = info.Name;
        Name = info.Name;
        ColorHex = info.ColorHex ?? string.Empty;
        IsGlobal = info.IsGlobal;
        HideFromOtherFilters = info.HideFromOtherFilters;
        NotifyOnChanges = info.NotifyOnChanges;
        RemoveOnChanges = info.RemoveOnChanges;
        AlertOnInstall = info.AlertOnInstall;
        RemoveOnInstall = info.RemoveOnInstall;
        HoldVersion = info.HoldVersion;
        IgnoreMissingFiles = info.IgnoreMissingFiles;
        ModuleCount = info.ModuleCount;
        IsNew = false;
    }

    public LabelEditItemViewModel(string currentInstanceName, bool isGlobal = true)
    {
        _originalName = null;
        Name = string.Empty;
        ColorHex = "#90EE90";
        IsGlobal = isGlobal;
        InstanceName = currentInstanceName;
        IsNew = true;
    }

    public string? OriginalName => _originalName;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _colorHex = string.Empty;

    [ObservableProperty]
    private bool _isGlobal = true;

    [ObservableProperty]
    private string? _instanceName;

    [ObservableProperty]
    private bool _hideFromOtherFilters;

    [ObservableProperty]
    private bool _notifyOnChanges;

    [ObservableProperty]
    private bool _removeOnChanges;

    [ObservableProperty]
    private bool _alertOnInstall;

    [ObservableProperty]
    private bool _removeOnInstall;

    [ObservableProperty]
    private bool _holdVersion;

    [ObservableProperty]
    private bool _ignoreMissingFiles;

    [ObservableProperty]
    private int _moduleCount;

    public bool HasColor => !string.IsNullOrWhiteSpace(ColorHex);

    partial void OnColorHexChanged(string value)
    {
        OnPropertyChanged(nameof(HasColor));
    }

    public LabelEditRequest ToRequest() => new(Name.Trim())
    {
        ColorHex = string.IsNullOrWhiteSpace(ColorHex) ? null : ColorHex.Trim(),
        IsGlobal = IsGlobal,
        HideFromOtherFilters = HideFromOtherFilters,
        NotifyOnChanges = NotifyOnChanges,
        RemoveOnChanges = RemoveOnChanges,
        AlertOnInstall = AlertOnInstall,
        RemoveOnInstall = RemoveOnInstall,
        HoldVersion = HoldVersion,
        IgnoreMissingFiles = IgnoreMissingFiles,
    };
}

/// <summary>
/// 标签组管理 / 编辑对话框视图模型（F-23 / F-24, UI-24b）。
/// 管理整个标签列表或单标签编辑，支持新建标签、修改属性、删除自定义标签。
/// </summary>
public sealed partial class LabelEditDialogViewModel : DialogViewModelBase
{
    private static readonly Regex HexColorRegex = new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    // 系统预置保护标签名（上游 ModuleLabelList 预设）：Hidden, Favourites, Held
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hidden", "Favourites", "Held"
    };

    // 常用预设颜色，方便用户一键选取
    public static readonly IReadOnlyList<string> PresetColors = new[]
    {
        "#90EE90", // PaleGreen (Favourites 默认)
        "#87CEFA", // LightSkyBlue
        "#FFD700", // Gold
        "#FFA07A", // LightSalmon
        "#DDA0DD", // Plum
        "#F08080", // LightCoral
        "#20B2AA", // LightSeaGreen
        "#B0C4DE", // LightSteelBlue
        "#D3D3D3", // LightGray
        "#FF69B4", // HotPink
    };

    private readonly ILabelService _labelService;

    public LabelEditDialogViewModel(ILabelService labelService)
        : base("管理标签组")
    {
        _labelService = labelService ?? throw new ArgumentNullException(nameof(labelService));
        PrimaryButtonText = "保存并关闭";
        SecondaryButtonText = "取消";
    }

    public ObservableCollection<LabelEditItemViewModel> Items { get; } = new();

    public IReadOnlyList<string> AvailablePresetColors => PresetColors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelected))]
    [NotifyPropertyChangedFor(nameof(IsReservedLabel))]
    private LabelEditItemViewModel? _selectedItem;

    public bool HasSelectedItem => SelectedItem is not null;

    public bool IsReservedLabel => SelectedItem is not null && !SelectedItem.IsNew &&
                                   ReservedNames.Contains(SelectedItem.OriginalName ?? SelectedItem.Name);

    public bool CanDeleteSelected => SelectedItem is not null && !IsReservedLabel;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string CurrentInstanceName => _labelService.InstanceName ?? "全局";

    public bool HasActiveInstance => _labelService.HasActiveInstance;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ValidationError = null;
        StatusMessage = string.Empty;
        Items.Clear();

        var result = await _labelService.RefreshAsync(ct).ConfigureAwait(false);
        var labels = result.IsSuccess && result.Value is not null ? result.Value : _labelService.Labels;

        foreach (var info in labels)
        {
            Items.Add(new LabelEditItemViewModel(info));
        }

        if (Items.Count > 0)
        {
            SelectedItem = Items[0];
        }
    }

    [RelayCommand]
    private void AddLabel()
    {
        ValidationError = null;
        var newItem = new LabelEditItemViewModel(CurrentInstanceName, isGlobal: !HasActiveInstance);
        var baseName = "新标签";
        var candidate = baseName;
        var counter = 1;
        while (Items.Any(i => string.Equals(i.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName}{counter++}";
        }
        newItem.Name = candidate;

        Items.Add(newItem);
        SelectedItem = newItem;
    }

    [RelayCommand]
    private void SelectPresetColor(string hex)
    {
        if (SelectedItem is not null && !string.IsNullOrWhiteSpace(hex))
        {
            SelectedItem.ColorHex = hex;
        }
    }

    [RelayCommand]
    private void ClearColor()
    {
        if (SelectedItem is not null)
        {
            SelectedItem.ColorHex = string.Empty;
        }
    }

    [RelayCommand]
    private async Task DeleteLabelAsync()
    {
        if (SelectedItem is null) return;

        ValidationError = null;
        if (IsReservedLabel)
        {
            ValidationError = "系统内置标签（如 Favourites、Hidden、Held）不能删除。";
            return;
        }

        var item = SelectedItem;
        if (!item.IsNew && !string.IsNullOrWhiteSpace(item.OriginalName))
        {
            var deleteResult = await _labelService.DeleteLabelAsync(item.OriginalName).ConfigureAwait(false);
            if (!deleteResult.IsSuccess)
            {
                ValidationError = deleteResult.Message ?? "删除标签组失败。";
                return;
            }
        }

        var currentIndex = Items.IndexOf(item);
        Items.Remove(item);

        if (Items.Count > 0)
        {
            var nextIndex = Math.Clamp(currentIndex, 0, Items.Count - 1);
            SelectedItem = Items[nextIndex];
        }
        else
        {
            SelectedItem = null;
        }

        StatusMessage = $"标签“{item.Name}”已删除。";
    }

    /// <inheritdoc />
    protected override bool Validate()
    {
        ValidationError = null;

        var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            var name = item.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ValidationError = "标签名称不能为空。";
                SelectedItem = item;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(item.ColorHex) && !HexColorRegex.IsMatch(item.ColorHex.Trim()))
            {
                ValidationError = $"标签“{name}”的颜色格式无效，请输入类似 #90EE90 的十六进制色值或置空。";
                SelectedItem = item;
                return false;
            }

            if (!nameSet.Add(name))
            {
                ValidationError = $"存在重复的标签名称“{name}”。";
                SelectedItem = item;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 保存所有修改并关闭对话框。
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!Validate())
        {
            return;
        }

        // 保存新增与修改
        foreach (var item in Items)
        {
            var req = item.ToRequest();
            if (item.IsNew)
            {
                var res = await _labelService.CreateLabelAsync(req).ConfigureAwait(false);
                if (!res.IsSuccess)
                {
                    ValidationError = res.Message ?? $"创建标签“{item.Name}”失败。";
                    SelectedItem = item;
                    return;
                }
                item.IsNew = false;
            }
            else
            {
                var res = await _labelService.UpdateLabelAsync(req).ConfigureAwait(false);
                if (!res.IsSuccess)
                {
                    ValidationError = res.Message ?? $"更新标签“{item.Name}”失败。";
                    SelectedItem = item;
                    return;
                }
            }
        }

        await _labelService.RefreshAsync().ConfigureAwait(false);
        IsConfirmed = true;
        Close();
    }

    /// <inheritdoc />
    protected override void OnPrimary()
    {
        _ = SaveAsync();
    }

    /// <inheritdoc />
    protected override bool CloseAfterPrimary => false;
}
