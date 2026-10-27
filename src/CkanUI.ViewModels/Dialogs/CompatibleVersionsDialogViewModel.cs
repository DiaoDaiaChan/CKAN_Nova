using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 兼容游戏版本管理对话框视图模型（F-14 / UI-11）。
/// 允许用户为当前游戏实例追加兼容游戏版本，并支持通配、自定义版本添加、风险确认与状态刷新。
/// </summary>
public sealed partial class CompatibleVersionsDialogViewModel : DialogViewModelBase
{
    private readonly ICompatibleVersionsService? _service;
    private readonly string? _instanceName;
    private readonly HashSet<string> _initialSelected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>游戏实例名称。</summary>
    [ObservableProperty]
    private string _instanceDisplayName = string.Empty;

    /// <summary>原生游戏版本。</summary>
    [ObservableProperty]
    private string _realVersion = string.Empty;

    /// <summary>自定义版本输入框文本。</summary>
    [ObservableProperty]
    private string _customVersionInput = string.Empty;

    /// <summary>是否正在加载中。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>是否正在保存中。</summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>是否需要显示修改兼容版本的风险警告提示。</summary>
    [ObservableProperty]
    private bool _isWarningPromptVisible;

    /// <summary>风险警告提示内容。</summary>
    [ObservableProperty]
    private string _warningMessage =
        "修改兼容游戏版本可能会让不完全支持当前版本的模组可用并安装，存在导致游戏崩溃或存档损坏的风险。\n确定要应用这些更改吗？";

    /// <summary>当前所有可选与已选的版本项列表。</summary>
    public ObservableCollection<CompatibleVersionItemViewModel> VersionItems { get; } = new();

    /// <summary>外部注入的风险确认委托（用于弹窗提示，如未指定则在对话框内部显式确认）。</summary>
    public Func<string, Task<bool>>? WarningConfirmationHandler { get; set; }

    /// <summary>
    /// 无参构造函数（支持设计器和假数据模式）。
    /// </summary>
    public CompatibleVersionsDialogViewModel() : this(null, null)
    {
    }

    /// <summary>
    /// 完整依赖注入构造函数。
    /// </summary>
    public CompatibleVersionsDialogViewModel(
        ICompatibleVersionsService? service,
        string? instanceName = null)
        : base("兼容游戏版本管理")
    {
        _service = service;
        _instanceName = instanceName;
        PrimaryButtonText = "保存";
        SecondaryButtonText = "取消";
    }

    /// <summary>
    /// 从服务异步加载版本快照。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null)
        {
            // 设计器或无服务模式，填充演示数据
            InstanceDisplayName = "DefaultInstance";
            RealVersion = "1.12.5";
            PopulateItems(new[]
            {
                new CompatibleVersionItemDto("1.12.5", true, true, false),
                new CompatibleVersionItemDto("1.12", true, false, true),
                new CompatibleVersionItemDto("1.12.4", false, false, false),
                new CompatibleVersionItemDto("1.12.3", false, false, false),
                new CompatibleVersionItemDto("1.11", false, false, true),
            });
            return;
        }

        IsLoading = true;
        ValidationError = null;
        try
        {
            var snapshot = await _service.LoadAsync(_instanceName, cancellationToken).ConfigureAwait(true);
            InstanceDisplayName = snapshot.InstanceName;
            RealVersion = snapshot.RealVersion;
            PopulateItems(snapshot.Items);
        }
        catch (Exception ex)
        {
            ValidationError = $"加载兼容游戏版本失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PopulateItems(IEnumerable<CompatibleVersionItemDto> items)
    {
        VersionItems.Clear();
        _initialSelected.Clear();

        foreach (var item in items)
        {
            var vm = new CompatibleVersionItemViewModel(
                item.VersionString,
                item.IsSelected,
                item.IsRealVersion,
                item.IsWildcard);

            if (item.IsSelected)
            {
                _initialSelected.Add(item.VersionString);
            }

            VersionItems.Add(vm);
        }
    }

    /// <summary>
    /// 添加自定义输入的游戏版本或通配符。
    /// </summary>
    [RelayCommand]
    public void AddCustomVersion()
    {
        var input = CustomVersionInput?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            ValidationError = "请输入有效的版本号。";
            return;
        }

        // 检查是否已存在
        var existing = VersionItems.FirstOrDefault(i => string.Equals(i.VersionString, input, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsSelected = true;
            CustomVersionInput = string.Empty;
            ValidationError = null;
            return;
        }

        bool isWildcard = !input.Contains('.') || input.Split('.').Length <= 2 || input.EndsWith(".*", StringComparison.Ordinal);
        var newItem = new CompatibleVersionItemViewModel(
            versionString: input,
            isSelected: true,
            isRealVersion: false,
            isWildcard: isWildcard);

        // 插入在真实版本之后
        int insertIndex = VersionItems.Count > 0 && VersionItems[0].IsRealVersion ? 1 : 0;
        VersionItems.Insert(insertIndex, newItem);
        CustomVersionInput = string.Empty;
        ValidationError = null;
    }

    /// <summary>
    /// 全选/取消全选所有已知版本（不包含真实版本，真实版本始终保持选中）。
    /// </summary>
    [RelayCommand]
    public void SelectAll(bool select)
    {
        foreach (var item in VersionItems)
        {
            if (!item.IsRealVersion)
            {
                item.IsSelected = select;
            }
        }
    }

    /// <summary>
    /// 重置为初始加载时的状态。
    /// </summary>
    [RelayCommand]
    public void ResetToInitial()
    {
        foreach (var item in VersionItems)
        {
            if (item.IsRealVersion)
            {
                item.IsSelected = true;
            }
            else
            {
                item.IsSelected = _initialSelected.Contains(item.VersionString);
            }
        }
        ValidationError = null;
        IsWarningPromptVisible = false;
    }

    /// <summary>
    /// 确认保存更改。包含风险提示判定与服务保存。
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsSaving) return;

        var selected = VersionItems
            .Where(i => i.IsSelected)
            .Select(i => i.VersionString)
            .ToList();

        // 判定是否有变更
        var currentSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        bool hasChanges = !currentSet.SetEquals(_initialSelected);

        // 如果有修改，并且追加了非原生版本，需要进行风险提示
        if (hasChanges)
        {
            if (WarningConfirmationHandler is not null)
            {
                bool confirmed = await WarningConfirmationHandler(WarningMessage).ConfigureAwait(true);
                if (!confirmed)
                {
                    return;
                }
            }
            else if (!IsWarningPromptVisible)
            {
                // 如果没有外部委托，激活视图内置的警告确认区域
                IsWarningPromptVisible = true;
                return;
            }
        }

        IsSaving = true;
        ValidationError = null;
        try
        {
            if (_service is not null)
            {
                await _service.SaveAsync(selected, _instanceName, triggerRefresh: true).ConfigureAwait(true);
            }

            IsConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            ValidationError = $"保存兼容游戏版本失败：{ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// 取消内置警告提示。
    /// </summary>
    [RelayCommand]
    public void DismissWarning()
    {
        IsWarningPromptVisible = false;
    }

    /// <inheritdoc />
    protected override void OnPrimary()
    {
        // 覆盖 DialogViewModelBase.PrimaryCommand，执行 SaveAsync
        _ = SaveAsync();
    }

    /// <inheritdoc />
    protected override bool CloseAfterPrimary => false; // 由 SaveAsync 控制何时关闭
}
