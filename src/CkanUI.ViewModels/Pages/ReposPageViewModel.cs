using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Repos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 仓库管理页视图模型（06 篇 UI-06 / VM-06，03 篇 §6 V-04）：仓库列表、优先级调整、增删、单仓库与整表刷新。
/// </summary>
/// <remarks>
/// <para>铁律 7：列表状态一律整体替换 <see cref="Rows"/>，不做增量 <c>CollectionChanged</c>。</para>
/// <para>订阅只在页面激活期间建立，<see cref="PageViewModel.Deactivate"/> 后不再收到服务层事件。</para>
/// <para>所有可见状态都以只读属性暴露，便于无头测试直接断言文案（04 篇 §1）。</para>
/// </remarks>
public sealed partial class ReposPageViewModel : PageViewModel
{
    private readonly IRepositoryService _repositories;
    private readonly IErrorReporter _errors;
    private readonly IDialogService _dialogs;
    private bool _subscribed;

    /// <summary>设计时/无参构造：用空实现占位，供 XAML 预览与测试使用。</summary>
    public ReposPageViewModel()
        : this(new EmptyRepositoryService(), new EmptyErrorReporter(), new EmptyDialogService())
    {
    }

    /// <summary>注入仓库服务、错误上报与确认对话框。</summary>
    public ReposPageViewModel(IRepositoryService repositories, IErrorReporter errors, IDialogService dialogs)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

        Form = new RepositoryFormViewModel();
        ApplyRows(_repositories.Repositories);
        SetStatus(Text("仓库列表已从本地注册表载入。", LocalizationKeys.ReposStatusLoadedLocal));
    }

    /// <summary>页面标题（导航项文案）。</summary>
    public override string Title => Text("仓库", LocalizationKeys.NavRailRepos);

    /// <summary>新增仓库的内联表单。</summary>
    public RepositoryFormViewModel Form { get; }

    /// <summary>当前仓库列表（按优先级升序，整表替换）。</summary>
    [ObservableProperty]
    private IReadOnlyList<RepositoryRowViewModel> _rows = Array.Empty<RepositoryRowViewModel>();

    /// <summary>当前选中的仓库。</summary>
    [ObservableProperty]
    private RepositoryRowViewModel? _selectedRow;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>页面级错误文案（03 篇 §10）。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>错误详情（可折叠显示）。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>是否有操作正在进行。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>进行中的操作文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>新增表单是否展开。</summary>
    [ObservableProperty]
    private bool _isFormOpen;

    // ---- 派生状态（视图只绑这些） ------------------------------------------

    /// <summary>有仓库行。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>显示空状态（无仓库且没有操作在进行）。</summary>
    public bool ShowEmptyState => Rows.Count == 0 && !IsBusy;

    /// <summary>仓库数量。</summary>
    public int RepositoryCount => Rows.Count;

    /// <summary>「共 N 个仓库」。</summary>
    public string CountText => Text("共 {0} 个仓库", LocalizationKeys.ReposCountSummary, Rows.Count);

    /// <summary>有页面级错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>有错误详情。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>空闲（按钮可用）。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>有选中项。</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>可以移除（至少剩两个仓库时）。</summary>
    public bool CanRemoveSelected => SelectedRow is not null && Rows.Count > 1;

    /// <summary>可以上移（不在首位）。</summary>
    public bool CanMoveUpSelected => SelectedIndex > 0;

    /// <summary>可以下移（不在末位）。</summary>
    public bool CanMoveDownSelected => SelectedIndex >= 0 && SelectedIndex < Rows.Count - 1;

    /// <summary>可以刷新选中的仓库。</summary>
    public bool CanRefreshSelected => SelectedRow is not null && IsIdle;

    /// <summary>可以编辑（空闲时才能改列表）。</summary>
    public bool CanEdit => IsIdle;

    /// <summary>表单是否展开。</summary>
    public bool HasForm => IsFormOpen;

    /// <summary>空状态标题（03 篇 §10）。</summary>
    public string EmptyStateTitle => Text("还没有配置任何仓库", LocalizationKeys.ReposEmptyTitle);

    /// <summary>空状态说明。</summary>
    public string EmptyStateHint => Text("仓库决定模组索引从哪来。", LocalizationKeys.ReposEmptyHint);

    /// <summary>选中仓库的一行摘要。</summary>
    public string SelectedDetailText => SelectedRow is { } row
        ? $"{row.Name}｜{row.PriorityText}｜{row.StatusText}"
        : Text("选择一个仓库查看详情。", LocalizationKeys.ReposEmptySelectHint);

    /// <summary>选中仓库的地址（未选中时为空串）。</summary>
    public string SelectedUriText => SelectedRow?.Uri ?? string.Empty;

    /// <summary>选中仓库的上次刷新文案。</summary>
    public string SelectedUpdatedText => SelectedRow?.UpdatedText ?? string.Empty;

    /// <summary>选中仓库的失败详情（无则空串）。</summary>
    public string SelectedFailureText => SelectedRow?.FailureText ?? string.Empty;

    private int SelectedIndex
    {
        get
        {
            if (SelectedRow is not { } row)
            {
                return -1;
            }

            for (var index = 0; index < Rows.Count; index++)
            {
                if (string.Equals(Rows[index].Name, row.Name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// 按仓库名或索引地址选中一行（深链 <c>ckan://repo/&lt;name&gt;</c> 的落点，ARC-05）。
    /// </summary>
    /// <param name="nameOrUri">仓库名或索引地址，大小写不敏感。</param>
    /// <returns>是否命中并选中；列表里没有匹配项时返回 <c>false</c>，不改动当前选中。</returns>
    public bool SelectRepository(string? nameOrUri)
    {
        if (string.IsNullOrWhiteSpace(nameOrUri))
        {
            return false;
        }

        var value = nameOrUri.Trim();
        var target = Rows.FirstOrDefault(row => string.Equals(row.Name, value, StringComparison.OrdinalIgnoreCase))
                     ?? Rows.FirstOrDefault(row => string.Equals(row.Uri, value, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return false;
        }

        SelectedRow = target;
        return true;
    }

    // ---- 生命周期 ----------------------------------------------------------

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (_subscribed)
        {
            return;
        }

        _repositories.RepositoriesChanged += OnRepositoriesChanged;
        _repositories.RefreshProgress += OnRefreshProgress;
        _subscribed = true;
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (!_subscribed)
        {
            return;
        }

        _repositories.RepositoriesChanged -= OnRepositoriesChanged;
        _repositories.RefreshProgress -= OnRefreshProgress;
        _subscribed = false;
    }

    // ---- 命令 --------------------------------------------------------------

    /// <summary>重新读取注册表里的仓库列表（不联网）。</summary>
    [RelayCommand]
    private Task ReloadAsync() => RunAsync(
        async () => ApplyResult(await _repositories.LoadRepositoriesAsync(), Text("仓库列表已重新读取。", LocalizationKeys.ReposStatusReloaded)),
        "读取仓库列表",
        Text("正在读取仓库列表…", LocalizationKeys.ReposBusyLoading));

    /// <summary>展开新增表单。</summary>
    [RelayCommand]
    private void BeginAdd()
    {
        Form.Reset();
        ErrorText = null;
        ErrorDetail = null;
        IsFormOpen = true;
    }

    /// <summary>收起新增表单。</summary>
    [RelayCommand]
    private void CancelAdd()
    {
        Form.Reset();
        IsFormOpen = false;
    }

    /// <summary>校验并保存新仓库。</summary>
    [RelayCommand]
    private Task AddAsync() => RunAsync(
        async () =>
        {
            if (Form.IsEmpty)
            {
                Form.ValidationText = Text("请填写仓库名称和地址。", LocalizationKeys.ReposErrorNameRequired);
                SetError(Text("请填写仓库名称和地址。", LocalizationKeys.ReposErrorNameRequired), null);
                return;
            }

            var name = Form.Name?.Trim();
            var result = await _repositories.AddRepositoryAsync(Form.ToRequest());
            if (result.IsSuccess)
            {
                Form.Reset();
                IsFormOpen = false;
                ApplyResult(result, Text("已添加仓库「{0}」。", LocalizationKeys.ReposStatusAdded, name));
                return;
            }

            Form.ValidationText = result.Message ?? Text("保存失败。", LocalizationKeys.ReposErrorSaveFailed);
            ApplyResult(result, string.Empty);
        },
        "新增仓库",
        Text("正在保存仓库列表…", LocalizationKeys.ReposBusySaving));

    /// <summary>移除选中的仓库（先确认；最后一个不允许移除）。</summary>
    [RelayCommand]
    private Task RemoveAsync() => RunAsync(
        async () =>
        {
            if (SelectedRow is not { } row)
            {
                return;
            }

            var confirmed = await _dialogs.ConfirmAsync(
                Text("移除仓库", LocalizationKeys.ReposRemoveTitle),
                Text("确定要移除仓库「{0}」吗？", LocalizationKeys.ReposRemoveConfirm, row.Name),
                Text("移除后该仓库提供的索引将不再参与搜索，已安装的模组不受影响。", LocalizationKeys.ReposRemoveHint));
            if (!confirmed)
            {
                SetStatus(Text("已取消移除仓库。", LocalizationKeys.ReposStatusRemoveCancelled));
                return;
            }

            ApplyResult(await _repositories.RemoveRepositoryAsync(row.Name), Text("已移除仓库「{0}」。", LocalizationKeys.ReposStatusRemoved, row.Name));
        },
        Text("移除仓库", LocalizationKeys.ReposRemoveTitle),
        Text("正在保存仓库列表…", LocalizationKeys.ReposBusySaving));

    /// <summary>上移选中的仓库（提高优先级）。</summary>
    [RelayCommand]
    private Task MoveUpAsync() => MoveAsync(-1);

    /// <summary>下移选中的仓库（降低优先级）。</summary>
    [RelayCommand]
    private Task MoveDownAsync() => MoveAsync(1);

    /// <summary>只刷新选中仓库的索引。</summary>
    [RelayCommand]
    private Task RefreshSelectedAsync() => RunAsync(
        async () =>
        {
            if (SelectedRow is not { } row)
            {
                return;
            }

            ApplyResult(await _repositories.RefreshRepositoryAsync(row.Name), Text("仓库「{0}」已刷新。", LocalizationKeys.ReposStatusRefreshed, row.Name));
        },
        "刷新仓库索引",
        Text("正在刷新仓库索引…", LocalizationKeys.ReposBusyRefreshing));

    /// <summary>刷新全部仓库的索引（复用服务层的批量刷新与进度）。</summary>
    [RelayCommand]
    private Task RefreshAllAsync() => RunAsync(
        async () =>
        {
            // 服务层的 async 方法在 UI 线程起跑时会沿用 UI 的同步上下文，续体回到 UI 线程再解析索引，
            // 点一次「刷新全部」就会冻住 0.5–0.7s。整段放线程池起跑，续体不回到 UI 线程。
            var offThread = !string.Equals(Environment.GetEnvironmentVariable("CKANUI_REPOS_SYNC"), "1", StringComparison.Ordinal);
            var result = offThread
                ? await Task.Run(() => _repositories.RefreshAsync()).ConfigureAwait(true)
                : await _repositories.RefreshAsync().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetError(ReposErrorText.Compose(result.Message, Text("仓库索引刷新失败。", LocalizationKeys.ReposErrorRefreshFailed), result.Retryable), result.Detail);
                return;
            }

            var entries = offThread
                ? await Task.Run(() => _repositories.LoadRepositoriesAsync()).ConfigureAwait(true)
                : await _repositories.LoadRepositoriesAsync().ConfigureAwait(true);
            ApplyResult(entries, Text("仓库索引已刷新。", LocalizationKeys.ReposStatusRefreshDone));
        },
        "刷新仓库索引",
        Text("正在刷新仓库索引…", LocalizationKeys.ReposBusyRefreshing));

    /// <summary>恢复为官方默认仓库列表（先确认；需要联网）。</summary>
    [RelayCommand]
    private Task RestoreDefaultsAsync() => RunAsync(
        async () =>
        {
            var confirmed = await _dialogs.ConfirmAsync(
                Text("恢复默认仓库", LocalizationKeys.ReposRestoreTitle),
                Text("恢复为官方默认仓库列表？", LocalizationKeys.ReposRestoreConfirm),
                Text("当前仓库列表会被替换，需要联网获取官方列表。", LocalizationKeys.ReposRestoreHint));
            if (!confirmed)
            {
                SetStatus(Text("已取消恢复默认仓库。", LocalizationKeys.ReposStatusRestoreCancelled));
                return;
            }

            ApplyResult(await _repositories.RestoreDefaultRepositoriesAsync(), Text("已恢复为官方默认仓库列表。", LocalizationKeys.ReposStatusRestored));
        },
        Text("恢复默认仓库", LocalizationKeys.ReposRestoreTitle),
        Text("正在获取官方仓库列表…", LocalizationKeys.ReposBusyFetchingDefaults));

    /// <summary>关闭页面级错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    // ---- 内部实现 ----------------------------------------------------------

    private Task MoveAsync(int delta) => RunAsync(
        async () =>
        {
            if (SelectedRow is not { } row)
            {
                return;
            }

            var result = await _repositories.MoveRepositoryAsync(row.Name, delta);
            ApplyResult(result, delta < 0 ? Text("已上移仓库「{0}」。", LocalizationKeys.ReposStatusMovedUp, row.Name) : Text("已下移仓库「{0}」。", LocalizationKeys.ReposStatusMovedDown, row.Name));
        },
        "调整优先级",
        Text("正在保存仓库列表…", LocalizationKeys.ReposBusySaving));

    private async Task RunAsync(Func<Task> body, string context, string busyText)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = busyText;
        ErrorText = null;
        ErrorDetail = null;
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            _errors.Report(ex, context);
            SetError(ReposErrorText.FromException(ex, Text("仓库列表操作失败。", LocalizationKeys.ReposErrorOperation)), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResult(OperationResult<IReadOnlyList<RepositoryEntryInfo>> result, string successMessage)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess && result.Value is not null)
        {
            ApplyRows(result.Value);
            SetStatus(successMessage);
            return;
        }

        // 失败时服务层可能已把「刷新失败」标记投影到行上，重新取一次，保持界面与磁盘一致。
        ApplyRows(_repositories.Repositories);
        SetError(ReposErrorText.Compose(result.Message, Text("仓库列表操作失败。", LocalizationKeys.ReposErrorOperation), result.Retryable), result.Detail);
    }

    private void ApplyRows(IReadOnlyList<RepositoryEntryInfo> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var previousName = SelectedRow?.Name;
        var ordered = entries
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        var rows = new List<RepositoryRowViewModel>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            rows.Add(RepositoryRowViewModel.From(ordered[index], index > 0, index < ordered.Length - 1));
        }

        Rows = rows;
        SelectedRow = previousName is null
            ? rows.FirstOrDefault()
            : rows.FirstOrDefault(row => string.Equals(row.Name, previousName, StringComparison.Ordinal))
              ?? rows.FirstOrDefault();
    }

    private void SetStatus(string message) => StatusText = message;

    private void SetError(string message, string? detail)
    {
        ErrorText = message;
        ErrorDetail = detail;
        StatusText = message;
    }

    private void OnRepositoriesChanged(IReadOnlyList<RepositoryEntryInfo> entries)
    {
        ApplyRows(entries);
        if (!IsBusy)
        {
            SetStatus(Text("仓库列表已更新。", LocalizationKeys.ReposStatusUpdated));
        }
    }

    private void OnRefreshProgress(OperationProgress progress)
    {
        if (progress is null)
        {
            return;
        }

        BusyText = string.IsNullOrEmpty(progress.Message) ? progress.Title : progress.Message!;
        IsBusy = progress.State is OperationState.Running or OperationState.Pending;
        if (progress.State == OperationState.Failed)
        {
            SetError(BusyText, null);
        }
    }

    partial void OnRowsChanged(IReadOnlyList<RepositoryRowViewModel> value) => NotifyListState();

    partial void OnSelectedRowChanged(RepositoryRowViewModel? value) => NotifyListState();

    partial void OnIsBusyChanged(bool value) => NotifyListState();

    partial void OnIsFormOpenChanged(bool value) => OnPropertyChanged(nameof(HasForm));

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(RepositoryCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanRemoveSelected));
        OnPropertyChanged(nameof(CanMoveUpSelected));
        OnPropertyChanged(nameof(CanMoveDownSelected));
        OnPropertyChanged(nameof(CanRefreshSelected));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(SelectedDetailText));
        OnPropertyChanged(nameof(SelectedUriText));
        OnPropertyChanged(nameof(SelectedUpdatedText));
        OnPropertyChanged(nameof(SelectedFailureText));
    }

    // ---- 无参构造用的空实现（与 InstancesPageViewModel 同构） -----------------

    private sealed class EmptyRepositoryService : IRepositoryService
    {
        public RepositorySnapshot Current => RepositorySnapshot.Empty;

        public event Action<RepositorySnapshot>? Updated
        {
            add { }
            remove { }
        }

        public event Action<OperationProgress>? RefreshProgress
        {
            add { }
            remove { }
        }

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty));

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<RepositorySnapshot>.Fail("尚未接入仓库服务。"));
    }

    private sealed class EmptyErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();

        public void Report(Exception exception, string context, string? userMessage = null)
        {
        }

        public void Report(string context, string userMessage, string? detail = null)
        {
        }
    }

    private sealed class EmptyDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
            => Task.FromResult(false);

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options,
                                     int defaultIndex = 0)
            => Task.FromResult(defaultIndex);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title, Func<IProgress<double>, CancellationToken, Task> work)
            => Task.FromResult(OperationResult<bool>.Ok(true));
    }
}
