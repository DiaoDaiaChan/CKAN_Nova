using CkanUI.Localization;
using System.ComponentModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Unmanaged;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 未管理文件页（规格 F-42 / UI-10 / V-07）。扫描当前实例 GameData 中未登记为受管的目录与文件，
/// 支持刷新、全展开/全折叠、筛选、多选删除（两次确认 + 可预演）与在文件夹中显示。
/// </summary>
/// <remarks>删除是破坏性操作：预演走 <c>dryRun: true</c> 只做校验不落盘；真正删除必须先经过两次确认。</remarks>
public sealed partial class UnmanagedPageViewModel : PageViewModel
{
    private const string GameDataFolder = "GameData";

    private readonly IUnmanagedService _service;
    private readonly IInstanceService _instances;
    private readonly IErrorReporter _errors;
    private readonly IDialogService _dialogs;
    private readonly List<UnmanagedNodeViewModel> _flat = new();

    private IReadOnlyList<UnmanagedFileInfo> _entries = Array.Empty<UnmanagedFileInfo>();
    private InstanceInfo? _instance;
    private bool _subscribed;
    private bool _loaded;
    private bool _suppressSelection;

    /// <summary>设计时（XAML 预览）用的空实现。</summary>
    public UnmanagedPageViewModel()
        : this(new EmptyUnmanagedService(), new EmptyInstanceService(), new EmptyErrorReporter(), new EmptyDialogService())
    {
    }

    /// <summary>运行时构造：扫描服务 + 当前实例 + 错误上报 + 模态确认。</summary>
    public UnmanagedPageViewModel(
        IUnmanagedService service,
        IInstanceService instances,
        IErrorReporter errors,
        IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(dialogs);
        _service = service;
        _instances = instances;
        _errors = errors;
        _dialogs = dialogs;
        _instance = instances.Current;
    }

    /// <inheritdoc />
    public override string Title => Text("未管理文件", LocalizationKeys.NavRailUnmanaged);

    /// <summary>宿主注入的「在文件夹中显示」能力（App 侧实现，ViewModels 不碰进程）。</summary>
    public IUnmanagedShellLauncher? ShellLauncher { get; set; }

    /// <summary>树根节点集合（目录在前，同级按名称排序）。</summary>
    [ObservableProperty]
    private IReadOnlyList<UnmanagedNodeViewModel> _nodes = Array.Empty<UnmanagedNodeViewModel>();

    /// <summary>当前选中的节点（选中即勾选，供「在文件夹中显示」使用）。</summary>
    [ObservableProperty]
    private UnmanagedNodeViewModel? _selectedNode;

    /// <summary>状态条文案。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误条正文；为空表示无错误。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>错误详情（技术细节，可折叠展示）。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>是否忙碌（扫描/删除中）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>忙碌遮罩文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>筛选关键字（按相对路径匹配，不区分大小写）。</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>未管理文件数。</summary>
    [ObservableProperty]
    private int _fileCount;

    /// <summary>未管理目录数。</summary>
    [ObservableProperty]
    private int _directoryCount;

    /// <summary>未管理文件总字节数。</summary>
    [ObservableProperty]
    private long _totalSizeBytes;

    /// <summary>已选中的顶层项数（选中目录时其后代不再重复计数）。</summary>
    [ObservableProperty]
    private int _selectedCount;

    /// <summary>已选中的顶层项字节合计。</summary>
    [ObservableProperty]
    private long _selectedSizeBytes;

    /// <summary>删除预演结果（dry-run 返回的路径，未落盘）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _previewPaths = Array.Empty<string>();

    /// <summary>当前选中要删除的相对路径（目录含其后代）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _selectedPaths = Array.Empty<string>();

    /// <summary>是否已选定实例（决定空态展示哪一张引导卡片）。</summary>
    public bool HasInstance => _instance is not null;

    /// <summary>是否展示「还没有选定实例」引导卡片。</summary>
    public bool ShowNoInstance => _instance is null;

    /// <summary>树是否非空。</summary>
    public bool HasNodes => Nodes.Count > 0;

    /// <summary>是否展示「GameData 没有未管理内容」空态。</summary>
    public bool ShowEmptyState => _loaded && _instance is not null && !HasNodes && !IsBusy && !HasError;

    /// <summary>空态标题。</summary>
    public string EmptyStateTitle => Text("GameData 里没有未管理的内容", LocalizationKeys.UnmanagedEmptyTitle);

    /// <summary>空态说明。</summary>
    public string EmptyStateHint => Text("所有文件都已被注册表登记为受管，没有需要清理的残留。", LocalizationKeys.UnmanagedEmptyHint);

    /// <summary>无实例时的标题。</summary>
    public string NoInstanceTitle => Text("还没有选定游戏实例", LocalizationKeys.UnmanagedNoInstanceTitle);

    /// <summary>无实例时的说明。</summary>
    public string NoInstanceHint => Text("请先在「实例」页选择或添加一个实例。", LocalizationKeys.UnmanagedNoInstanceHint);

    /// <summary>统计条文案。</summary>
    public string StatsText => Text(
        "未管理：{0} 个文件、{1} 个目录，合计 {2}",
        LocalizationKeys.UnmanagedStats,
        FileCount,
        DirectoryCount,
        UnmanagedFormat.FormatSize(TotalSizeBytes));

    /// <summary>选中统计文案。</summary>
    public string SelectionText => SelectedCount == 0
        ? Text("未选中任何项", LocalizationKeys.UnmanagedSelectionNone)
        : Text(
            "已选中 {0} 项，合计 {1}",
            LocalizationKeys.UnmanagedSelectionSummary,
            SelectedCount,
            UnmanagedFormat.FormatSize(SelectedSizeBytes));

    /// <summary>是否有选中项。</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>是否处于空闲状态。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>是否有筛选条件。</summary>
    public bool HasFilter => !string.IsNullOrWhiteSpace(FilterText);

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误详情。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>能否刷新。</summary>
    public bool CanRefresh => !IsBusy;

    /// <summary>能否预演删除。</summary>
    public bool CanPreviewDelete => HasSelection && !IsBusy;

    /// <summary>能否删除选中项。</summary>
    public bool CanDeleteSelected => HasSelection && !IsBusy;

    /// <summary>能否把选中项接管为受管安装（只接管 .dll，见 12 篇 F-42）。</summary>
    public bool CanAdoptSelected => HasSelection && !IsBusy;

    /// <summary>能否在文件夹中显示（需要选中节点且宿主提供了委托）。</summary>
    public bool CanShowInFolder => SelectedNode is not null && ShellLauncher is not null && !IsBusy;

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (!_subscribed)
        {
            _instances.CurrentChanged += OnCurrentInstanceChanged;
            _subscribed = true;
        }

        if (_instance != _instances.Current || !_loaded)
        {
            _instance = _instances.Current;
            _ = RefreshAsync();
        }
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (!_subscribed)
        {
            return;
        }

        _instances.CurrentChanged -= OnCurrentInstanceChanged;
        _subscribed = false;
    }

    /// <summary>刷新：重新扫描 GameData。</summary>
    [RelayCommand]
    private Task RefreshAsync() => RunAsync(LoadCoreAsync, "扫描未管理文件", Text("正在扫描 GameData…", LocalizationKeys.UnmanagedBusyScan));

    /// <summary>全展开。</summary>
    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var node in Nodes)
        {
            node.ExpandAll();
        }

        SetStatus(DirectoryCount == 0
            ? Text("没有可展开的目录。", LocalizationKeys.UnmanagedStatusNoExpandable)
            : Text(
            "已展开全部目录（共 {0} 个）。",
            LocalizationKeys.UnmanagedStatusExpandedAll,
            DirectoryCount));
    }

    /// <summary>全折叠。</summary>
    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in Nodes)
        {
            node.CollapseAll();
        }

        SetStatus(DirectoryCount == 0
            ? Text("没有可折叠的目录。", LocalizationKeys.UnmanagedStatusNoCollapsible)
            : Text(
            "已折叠全部目录（共 {0} 个）。",
            LocalizationKeys.UnmanagedStatusCollapsedAll,
            DirectoryCount));
    }

    /// <summary>重置筛选。</summary>
    [RelayCommand]
    private void ResetFilter()
    {
        FilterText = string.Empty;
        SetStatus(Text("已重置筛选。", LocalizationKeys.UnmanagedStatusFilterReset));
    }

    /// <summary>全选 / 取消全选。</summary>
    [RelayCommand]
    private void ToggleSelectAll()
    {
        var select = SelectedCount == 0;
        _suppressSelection = true;
        try
        {
            foreach (var node in _flat)
            {
                node.IsSelected = select;
            }
        }
        finally
        {
            _suppressSelection = false;
        }

        RefreshSelectionState();
        SetStatus(select
            ? Text(
            "已选中 {0} 项。",
            LocalizationKeys.UnmanagedStatusSelectAll,
            SelectedCount)
            : Text("已取消全部选中。", LocalizationKeys.UnmanagedStatusSelectNone));
    }

    /// <summary>预演删除（dry-run，只校验不落盘）。</summary>
    [RelayCommand]
    private Task PreviewDeleteAsync() => RunAsync(async () =>
    {
        if (_instance is not { } instance)
        {
            SetError(Text("还没有选定游戏实例。", LocalizationKeys.UnmanagedErrorNoInstance), null);
            return;
        }

        var paths = SelectedPaths;
        if (paths.Count == 0)
        {
            SetError(Text("没有选中要删除的内容。", LocalizationKeys.UnmanagedErrorNoDeleteSelection), null);
            return;
        }

        var preview = await _service.DeleteAsync(instance, paths, dryRun: true);
        if (!preview.IsSuccess)
        {
            SetError(UnmanagedErrorText.Compose(preview.Message, Text("预演删除失败。", LocalizationKeys.UnmanagedErrorPreviewFailed), preview.Retryable), preview.Detail);
            return;
        }

        PreviewPaths = preview.Value ?? Array.Empty<string>();
        SetStatus(Text(
            "预演：将删除 {0} 项（未落盘）。",
            LocalizationKeys.UnmanagedStatusPreview,
            PreviewPaths.Count));
    }, "预演删除未管理文件", Text("正在预演删除…", LocalizationKeys.UnmanagedBusyPreview));

    /// <summary>删除选中项：两次确认后真正落盘。</summary>
    [RelayCommand]
    private Task DeleteSelectedAsync() => RunAsync(async () =>
    {
        if (_instance is not { } instance)
        {
            SetError(Text("还没有选定游戏实例。", LocalizationKeys.UnmanagedErrorNoInstance), null);
            return;
        }

        var paths = SelectedPaths;
        if (paths.Count == 0)
        {
            SetError(Text("没有选中要删除的内容。", LocalizationKeys.UnmanagedErrorNoDeleteSelection), null);
            return;
        }

        var total = UnmanagedFormat.FormatSize(SelectedSizeBytes);
        var detail = string.Join(Environment.NewLine, paths.Take(10));
        if (paths.Count > 10)
        {
            detail += Environment.NewLine + "…";
        }

        var first = await _dialogs.ConfirmAsync(
            Text("删除未管理文件", LocalizationKeys.UnmanagedConfirmDeleteTitle),
            Text(
            "确定要删除选中的 {0} 项（{1}）吗？",
            LocalizationKeys.UnmanagedConfirmDeleteMessage,
            paths.Count,
            total),
            Text("删除会直接从磁盘移除文件，无法撤销。", LocalizationKeys.UnmanagedConfirmDeleteDetail) + Environment.NewLine + detail);
        if (!first)
        {
            SetStatus(Text("已取消删除。", LocalizationKeys.UnmanagedStatusDeleteCancelled));
            return;
        }

        var second = await _dialogs.ConfirmAsync(
            Text("再次确认删除", LocalizationKeys.UnmanagedConfirmDeleteAgainTitle),
            Text(
            "删除后无法恢复：{0} 项，{1}。要继续吗？",
            LocalizationKeys.UnmanagedConfirmDeleteAgainMessage,
            paths.Count,
            total),
            Text("受管文件不在本页删除范围；含受管内容的目录会被服务拒绝。", LocalizationKeys.UnmanagedConfirmDeleteAgainDetail));
        if (!second)
        {
            SetStatus(Text("已取消删除。", LocalizationKeys.UnmanagedStatusDeleteCancelled));
            return;
        }

        var result = await _service.DeleteAsync(instance, paths, dryRun: false);
        if (!result.IsSuccess)
        {
            SetError(UnmanagedErrorText.Compose(result.Message, Text("删除未管理文件失败。", LocalizationKeys.UnmanagedErrorDeleteFailed), result.Retryable), result.Detail);
            return;
        }

        var deleted = result.Value?.Count ?? paths.Count;
        PreviewPaths = Array.Empty<string>();
        await LoadCoreAsync();
        SetStatus(Text(
            "已删除 {0} 项未管理内容。",
            LocalizationKeys.UnmanagedStatusDeleted,
            deleted));
    }, "删除未管理文件", Text("正在删除…", LocalizationKeys.UnmanagedBusyDelete));

    /// <summary>
    /// 把选中项接管为受管安装：写进注册表 <c>installed_dlls</c>（12 篇 F-42 / INT-09）。
    /// 只接管 <c>.dll</c>；拒绝项会带原因显示在错误条里，不会静默丢弃。
    /// </summary>
    [RelayCommand]
    private Task AdoptSelectedAsync() => RunAsync(async () =>
    {
        if (_instance is not { } instance)
        {
            SetError(Text("还没有选定游戏实例。", LocalizationKeys.UnmanagedErrorNoInstance), null);
            return;
        }

        var paths = SelectedPaths;
        if (paths.Count == 0)
        {
            SetError(Text("没有选中要接管的内容。", LocalizationKeys.UnmanagedErrorNoAdoptSelection), null);
            return;
        }

        var detail = string.Join(Environment.NewLine, paths.Take(10));
        if (paths.Count > 10)
        {
            detail += Environment.NewLine + "…";
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Text("接管为受管安装", LocalizationKeys.UnmanagedConfirmAdoptTitle),
            Text(
            "确定把选中的 {0} 项接管为受管安装吗？",
            LocalizationKeys.UnmanagedConfirmAdoptMessage,
            paths.Count),
            Text("接管会把 .dll 登记进注册表（installed_dlls），写前自动生成 registry.json 快照。", LocalizationKeys.UnmanagedConfirmAdoptDetailRegistry)
            + Environment.NewLine
            + Text("只有 .dll 可以接管；目录会展开成其中的 DLL。", LocalizationKeys.UnmanagedConfirmAdoptDetailDllOnly)
            + Environment.NewLine
            + detail);
        if (!confirmed)
        {
            SetStatus(Text("已取消接管。", LocalizationKeys.UnmanagedStatusAdoptCancelled));
            return;
        }

        var result = await _service.AdoptAsync(instance, paths, dryRun: false);
        if (!result.IsSuccess)
        {
            SetError(UnmanagedErrorText.Compose(result.Message, Text("接管未管理文件失败。", LocalizationKeys.UnmanagedErrorAdoptFailed), result.Retryable), result.Detail);
            return;
        }

        var outcome = result.Value ?? UnmanagedAdoptResult.Empty;
        await LoadCoreAsync();
        SetStatus(Text(
            "已接管 {0} 个、跳过 {1} 个、拒绝 {2} 个。",
            LocalizationKeys.UnmanagedStatusAdopted,
            outcome.AdoptedCount,
            outcome.SkippedCount,
            outcome.RefusedCount));

        if (outcome.HasRefusals)
        {
            var refused = outcome.Items
                .Where(item => item.Outcome == UnmanagedAdoptOutcome.Refused)
                .Select(item => string.IsNullOrWhiteSpace(item.Reason)
                    ? item.RelativePath
                    : item.RelativePath + "：" + item.Reason);
            SetError(
                Text(
            "有 {0} 项被拒绝接管。",
            LocalizationKeys.UnmanagedErrorAdoptRefused,
            outcome.RefusedCount),
                string.Join(Environment.NewLine, refused));
        }
    }, "接管未管理文件", Text("正在接管…", LocalizationKeys.UnmanagedBusyAdopt));

    /// <summary>在系统文件管理器中定位选中项。</summary>
    [RelayCommand]
    private async Task ShowInFolderAsync()
    {
        if (SelectedNode is not { } node)
        {
            SetError(Text("请先选中一个文件或目录。", LocalizationKeys.UnmanagedErrorNoRevealSelection), null);
            return;
        }

        if (_instance is not { } instance)
        {
            SetError(Text("还没有选定游戏实例。", LocalizationKeys.UnmanagedErrorNoInstance), null);
            return;
        }

        if (ShellLauncher is not { } launcher)
        {
            SetError(Text("当前宿主没有提供「在文件夹中显示」能力。", LocalizationKeys.UnmanagedErrorNoRevealChannel), null);
            return;
        }

        var absolute = Path.Combine(
            instance.GameDir,
            GameDataFolder,
            node.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = await launcher.ShowInFolderAsync(absolute);
        if (!result.IsSuccess)
        {
            SetError(UnmanagedErrorText.Compose(result.Message, Text("打开文件夹失败。", LocalizationKeys.UnmanagedErrorRevealFailed), result.Retryable), result.Detail);
            return;
        }

        SetStatus(Text(
            "已在文件管理器中定位：{0}",
            LocalizationKeys.UnmanagedStatusRevealed,
            node.RelativePath));
    }

    /// <summary>关闭错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    private async Task LoadCoreAsync()
    {
        if (_instances.Current is not { } instance)
        {
            ApplyEntries(null, Array.Empty<UnmanagedFileInfo>());
            SetStatus(Text("还没有选定游戏实例。", LocalizationKeys.UnmanagedErrorNoInstance));
            return;
        }

        var result = await _service.ScanAsync(instance);
        if (!result.IsSuccess)
        {
            ApplyEntries(instance, Array.Empty<UnmanagedFileInfo>());
            SetError(UnmanagedErrorText.Compose(result.Message, Text("扫描未管理文件失败。", LocalizationKeys.UnmanagedErrorScanFailed), result.Retryable), result.Detail);
            return;
        }

        var entries = result.Value ?? Array.Empty<UnmanagedFileInfo>();
        // 树构建是纯投影（几万个条目 → 节点），放在 UI 线程会让刷新按钮点完掉 200ms+ 的帧。
        var offThread = !string.Equals(Environment.GetEnvironmentVariable("CKANUI_UNMANAGED_SYNC"), "1", StringComparison.Ordinal);
        var prebuilt = offThread
            ? await Task.Run(() => UnmanagedTreeBuilder.Build(entries, FilterText)).ConfigureAwait(true)
            : null;
        ApplyEntries(instance, entries, prebuilt);
        SetStatus(entries.Count == 0
            ? Text("GameData 里没有未管理的内容。", LocalizationKeys.UnmanagedStatusEmptyScan)
            : Text(
            "已扫描到 {0} 个未管理文件、{1} 个目录。",
            LocalizationKeys.UnmanagedStatusScanned,
            FileCount,
            DirectoryCount));
    }

    private void ApplyEntries(InstanceInfo? instance, IReadOnlyList<UnmanagedFileInfo> entries, IReadOnlyList<UnmanagedNodeViewModel>? prebuilt = null)
    {
        _instance = instance;
        _entries = entries;
        _loaded = true;

        var files = 0;
        var directories = 0;
        long size = 0;
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                directories++;
            }
            else
            {
                files++;
                size += entry.SizeBytes;
            }
        }

        FileCount = files;
        DirectoryCount = directories;
        TotalSizeBytes = size;
        RebuildTree(prebuilt);
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(ShowNoInstance));
    }

    private void RebuildTree(IReadOnlyList<UnmanagedNodeViewModel>? prebuilt = null)
    {
        UntrackNodes();

        var nodes = prebuilt ?? UnmanagedTreeBuilder.Build(_entries, FilterText);
        _flat.Clear();
        foreach (var node in nodes)
        {
            _flat.AddRange(node.Walk());
        }

        Nodes = nodes;
        TrackNodes();

        if (HasFilter)
        {
            foreach (var node in _flat)
            {
                if (node.IsDirectory)
                {
                    node.IsExpanded = true;
                }
            }
        }

        if (SelectedNode is { } selected && !_flat.Contains(selected))
        {
            SelectedNode = null;
        }

        RefreshSelectionState();
        OnPropertyChanged(nameof(HasNodes));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(CanShowInFolder));
    }

    private void TrackNodes()
    {
        foreach (var node in _flat)
        {
            node.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void UntrackNodes()
    {
        foreach (var node in _flat)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UnmanagedNodeViewModel.IsSelected) || _suppressSelection)
        {
            return;
        }

        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in _flat)
        {
            if (node.IsSelected)
            {
                selected.Add(node.RelativePath);
            }
        }

        var topMost = new List<string>();
        long size = 0;
        foreach (var node in _flat)
        {
            if (!node.IsSelected || HasSelectedAncestor(node.RelativePath, selected))
            {
                continue;
            }

            topMost.Add(node.RelativePath);
            size += node.SizeBytes;
        }

        SelectedPaths = topMost;
        SelectedCount = topMost.Count;
        SelectedSizeBytes = size;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanPreviewDelete));
        OnPropertyChanged(nameof(CanAdoptSelected));
    }

    private static bool HasSelectedAncestor(string relativePath, HashSet<string> selected)
    {
        var index = relativePath.LastIndexOf('/');
        while (index > 0)
        {
            var ancestor = relativePath[..index];
            if (selected.Contains(ancestor))
            {
                return true;
            }

            index = ancestor.LastIndexOf('/');
        }

        return false;
    }

    private async Task RunAsync(Func<Task> action, string context, string busyText)
    {
        ArgumentNullException.ThrowIfNull(action);
        IsBusy = true;
        BusyText = busyText;
        ErrorText = null;
        ErrorDetail = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _errors.Report(ex, context);
            SetError(UnmanagedErrorText.FromException(ex, Text("未管理文件操作失败。", LocalizationKeys.UnmanagedErrorOperationFailed)), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string text)
    {
        StatusText = text;
        ErrorText = null;
        ErrorDetail = null;
    }

    private void SetError(string text, string? detail)
    {
        ErrorText = text;
        ErrorDetail = detail;
    }

    private void OnCurrentInstanceChanged(InstanceInfo? instance) => _ = RefreshAsync();

    partial void OnSelectedNodeChanged(UnmanagedNodeViewModel? value)
    {
        if (value is not null && !value.IsSelected)
        {
            value.IsSelected = true;
        }

        OnPropertyChanged(nameof(CanShowInFolder));
    }

    partial void OnNodesChanged(IReadOnlyList<UnmanagedNodeViewModel> value)
    {
        OnPropertyChanged(nameof(HasNodes));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanPreviewDelete));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanAdoptSelected));
        OnPropertyChanged(nameof(CanShowInFolder));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnFilterTextChanged(string value)
    {
        RebuildTree();
        OnPropertyChanged(nameof(HasFilter));
    }

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    partial void OnFileCountChanged(int value) => OnPropertyChanged(nameof(StatsText));

    partial void OnDirectoryCountChanged(int value) => OnPropertyChanged(nameof(StatsText));

    partial void OnTotalSizeBytesChanged(long value) => OnPropertyChanged(nameof(StatsText));

    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(SelectionText));

    partial void OnSelectedSizeBytesChanged(long value) => OnPropertyChanged(nameof(SelectionText));

    private sealed class EmptyUnmanagedService : IUnmanagedService
    {
        public Task<OperationResult<IReadOnlyList<UnmanagedFileInfo>>> ScanAsync(
            InstanceInfo instance, CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Ok(Array.Empty<UnmanagedFileInfo>()));

        public Task<OperationResult<UnmanagedAdoptResult>> AdoptAsync(
            InstanceInfo instance, IReadOnlyCollection<string> relativePaths,
            bool dryRun = false, CancellationToken ct = default)
            => Task.FromResult(OperationResult<UnmanagedAdoptResult>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<IReadOnlyList<string>>> DeleteAsync(
            InstanceInfo instance,
            IReadOnlyCollection<string> relativePaths,
            bool dryRun = true,
            CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<string>>.Ok(Array.Empty<string>()));
    }

    private sealed class EmptyInstanceService : IInstanceService
    {
        public IReadOnlyList<InstanceInfo> Instances => Array.Empty<InstanceInfo>();

        public InstanceInfo? Current => null;

        public event Action? InstancesChanged
        {
            add { }
            remove { }
        }

        public event Action<InstanceInfo?>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Array.Empty<InstanceInfo>()));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));
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
        public Task<bool> ConfirmAsync(string title, string message, string? detail = null) => Task.FromResult(false);

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
            => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title, Func<IProgress<double>, CancellationToken, Task> work)
            => Task.FromResult(OperationResult<bool>.Fail("设计时未提供实现。", null, false));
    }
}
