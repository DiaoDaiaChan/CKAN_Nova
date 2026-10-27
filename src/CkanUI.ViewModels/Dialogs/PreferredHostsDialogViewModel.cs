using System.Collections.ObjectModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 首选下载主机管理对话框 ViewModel（F-15 / UI-30）。
/// 提供主机优先级排序（上移/下移）、增删、已知主机选择与持久化。
/// </summary>
public sealed partial class PreferredHostsDialogViewModel : DialogViewModelBase
{
    private readonly IPreferredHostsService? _service;

    [ObservableProperty]
    private ObservableCollection<PreferredHostItemViewModel> _preferredHosts = new();

    [ObservableProperty]
    private PreferredHostItemViewModel? _selectedHost;

    [ObservableProperty]
    private ObservableCollection<string> _knownHosts = new();

    [ObservableProperty]
    private string? _selectedKnownHost;

    [ObservableProperty]
    private string _customHostInput = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// 设计器和测试无参构造。
    /// </summary>
    public PreferredHostsDialogViewModel() : this(null)
    {
    }

    /// <summary>
    /// 完整依赖注入构造函数。
    /// </summary>
    public PreferredHostsDialogViewModel(IPreferredHostsService? service)
        : base("首选下载主机")
    {
        _service = service;
        PrimaryButtonText = "保存";
        SecondaryButtonText = "取消";
    }

    /// <summary>
    /// 初始化加载首选主机与已知主机。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null)
        {
            // 设计器或无服务模式，填充演示数据
            PopulateInitialData(
                new[]
                {
                    new HostPriorityItemDto("github.com", 1),
                    new HostPriorityItemDto("spacedock.info", 2),
                    new HostPriorityItemDto(null, 3, true),
                    new HostPriorityItemDto("archive.org", 4)
                },
                new[]
                {
                    "archive.org",
                    "github.com",
                    "gitlab.com",
                    "spacedock.info"
                });
            return;
        }

        try
        {
            IsLoading = true;
            ValidationError = null;

            var preferred = await _service.GetPreferredHostsAsync(cancellationToken);
            var known = await _service.GetKnownHostsAsync(cancellationToken);

            PopulateInitialData(preferred, known);
        }
        catch (Exception ex)
        {
            ValidationError = $"加载首选主机失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void PopulateInitialData(IEnumerable<HostPriorityItemDto> preferred, IEnumerable<string> known)
    {
        PreferredHosts.Clear();
        var priority = 1;
        var hasPlaceholder = false;

        foreach (var item in preferred)
        {
            var isPlaceholder = item.IsPlaceholder || string.IsNullOrWhiteSpace(item.Host);
            if (isPlaceholder)
            {
                if (hasPlaceholder)
                {
                    continue; // 保证占位符最多一个
                }
                hasPlaceholder = true;
            }
            PreferredHosts.Add(new PreferredHostItemViewModel(isPlaceholder ? null : item.Host, priority++, isPlaceholder));
        }

        // 若原配置完全没有「其余主机」占位符，默认追加在末尾
        if (!hasPlaceholder)
        {
            PreferredHosts.Add(new PreferredHostItemViewModel(null, priority++, true));
        }

        KnownHosts.Clear();
        foreach (var k in known.OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
        {
            KnownHosts.Add(k);
        }

        ReindexPriorities();
        NotifyCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (SelectedHost is null) return;
        var index = PreferredHosts.IndexOf(SelectedHost);
        if (index <= 0) return;

        PreferredHosts.Move(index, index - 1);
        ReindexPriorities();
        NotifyCommandStates();
    }

    public bool CanMoveUp => SelectedHost is not null && PreferredHosts.IndexOf(SelectedHost) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (SelectedHost is null) return;
        var index = PreferredHosts.IndexOf(SelectedHost);
        if (index < 0 || index >= PreferredHosts.Count - 1) return;

        PreferredHosts.Move(index, index + 1);
        ReindexPriorities();
        NotifyCommandStates();
    }

    public bool CanMoveDown => SelectedHost is not null && PreferredHosts.IndexOf(SelectedHost) >= 0 && PreferredHosts.IndexOf(SelectedHost) < PreferredHosts.Count - 1;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedHost is null) return;

        if (SelectedHost.IsPlaceholder)
        {
            ValidationError = "不能删除「其余所有主机」占位项，您可以通过上移或下移调整其优先级。";
            return;
        }

        var index = PreferredHosts.IndexOf(SelectedHost);
        PreferredHosts.Remove(SelectedHost);
        ValidationError = null;

        if (PreferredHosts.Count > 0)
        {
            SelectedHost = PreferredHosts[Math.Min(index, PreferredHosts.Count - 1)];
        }
        else
        {
            SelectedHost = null;
        }

        ReindexPriorities();
        NotifyCommandStates();
    }

    public bool CanRemove => SelectedHost is not null && !SelectedHost.IsPlaceholder;

    [RelayCommand]
    private void AddSelectedKnownHost()
    {
        if (string.IsNullOrWhiteSpace(SelectedKnownHost)) return;
        AddHostInternal(SelectedKnownHost.Trim());
    }

    [RelayCommand]
    private void AddCustomHost()
    {
        if (string.IsNullOrWhiteSpace(CustomHostInput)) return;
        if (AddHostInternal(CustomHostInput.Trim()))
        {
            CustomHostInput = string.Empty;
        }
    }

    /// <summary>
    /// 添加主机。返回是否成功。
    /// </summary>
    public bool AddHostInternal(string host)
    {
        ValidationError = null;
        var trimmed = host.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(8);
        }

        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx >= 0)
        {
            trimmed = trimmed.Substring(0, slashIdx);
        }

        trimmed = trimmed.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ValidationError = "主机名不能为空。";
            return false;
        }

        // 检查重复
        if (PreferredHosts.Any(h => !h.IsPlaceholder && string.Equals(h.Host, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationError = $"主机 '{trimmed}' 已经在首选列表中。";
            return false;
        }

        // 默认插入到「其余所有主机」的前面（如果存在占位符），否则追加到末尾
        var placeholderIndex = -1;
        for (var i = 0; i < PreferredHosts.Count; i++)
        {
            if (PreferredHosts[i].IsPlaceholder)
            {
                placeholderIndex = i;
                break;
            }
        }

        var newItem = new PreferredHostItemViewModel(trimmed, 0, false);
        if (placeholderIndex >= 0)
        {
            PreferredHosts.Insert(placeholderIndex, newItem);
        }
        else
        {
            PreferredHosts.Add(newItem);
        }

        SelectedHost = newItem;
        ReindexPriorities();
        NotifyCommandStates();
        return true;
    }

    /// <summary>
    /// 保存并提交首选主机配置。
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsSaving) return;

        if (_service is null)
        {
            IsConfirmed = true;
            Close();
            return;
        }

        try
        {
            IsSaving = true;
            ValidationError = null;

            var hostsToSave = PreferredHosts.Select(h => h.IsPlaceholder ? null : h.Host).ToList();
            var result = await _service.SavePreferredHostsAsync(hostsToSave);

            if (!result.IsSuccess)
            {
                ValidationError = result.Message ?? "保存失败";
                return;
            }

            IsConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            ValidationError = $"保存失败: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <inheritdoc />
    protected override void OnPrimary()
    {
        // 委托给异步保存
        _ = SaveAsync();
    }

    /// <inheritdoc />
    protected override bool CloseAfterPrimary => false;

    partial void OnSelectedHostChanged(PreferredHostItemViewModel? value)
    {
        NotifyCommandStates();
    }

    private void ReindexPriorities()
    {
        for (var i = 0; i < PreferredHosts.Count; i++)
        {
            PreferredHosts[i].Priority = i + 1;
        }
    }

    private void NotifyCommandStates()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
