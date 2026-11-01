using CkanUI.Localization;
using System.ComponentModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.ModPacks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 导入导出页（F-39 / F-40 / F-41 / F-45、UI-20 / UI-24、03 篇 §7）：
/// 导出当前实例的已装列表、预览并导入 .ckan、比对两个实例的模组差异。
/// </summary>
/// <remarks>
/// 页面只做编排与文案：所有文件读写、依赖求解、安装都在服务层，异常一律经 <see cref="IErrorReporter"/>
/// 转成中文文案，绝不抛给界面（工程约定 §7）。
/// </remarks>
public sealed partial class ModPacksPageViewModel : PageViewModel
{
    private const string ExportContext = "导出模组列表";
    private const string PreviewContext = "导入预览";
    private const string ImportContext = "导入模组";
    private const string CompareContext = "实例对比";
    private const string InstancesContext = "读取实例列表";

    private readonly IModPackService _modPacks;
    private readonly IInstanceService _instances;
    private readonly IErrorReporter _errors;
    private readonly IProgress<OperationProgress> _exportProgress;
    private readonly IProgress<OperationProgress> _importProgress;
    private IReadOnlyList<ImportEntryRowViewModel> _watchedRows = Array.Empty<ImportEntryRowViewModel>();
    private ModPackPreview? _preview;
    private bool _subscribed;

    /// <summary>无参构造：供设计时预览与 XAML 预览使用（所有依赖都是空实现）。</summary>
    public ModPacksPageViewModel()
        : this(new EmptyModPackService(), new EmptyInstanceService(), new EmptyErrorReporter())
    {
    }

    /// <summary>带服务注入的构造函数。</summary>
    /// <param name="modPacks">导入导出服务。</param>
    /// <param name="instances">实例服务（用于挑左右两个实例做对比）。</param>
    /// <param name="errors">错误上报。</param>
    public ModPacksPageViewModel(IModPackService modPacks, IInstanceService instances, IErrorReporter errors)
    {
        _modPacks = modPacks ?? throw new ArgumentNullException(nameof(modPacks));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _exportProgress = new InlineProgress<OperationProgress>(OnExportProgress);
        _importProgress = new InlineProgress<OperationProgress>(OnImportProgress);
        ExportForm = new ExportFormViewModel();
        ApplyInstances();
        SetStatus(Text("选择 .ckan 文件预览导入，或把当前实例的模组列表导出成 .ckan。", LocalizationKeys.ModPacksStatusReady));
    }

    /// <inheritdoc />
    public override string Title => Text("导入导出", LocalizationKeys.NavRailModPacks);

    /// <summary>导出表单。</summary>
    public ExportFormViewModel ExportForm { get; }

    /// <summary>是否已经选中一个可用的游戏实例（页头徽标与导出/导入入口据此切换）。</summary>
    public bool HasInstance => _modPacks.HasActiveInstance;

    /// <summary>当前实例的说明文案。</summary>
    public string InstanceTitleText => _modPacks.InstanceName is { Length: > 0 } name
        ? Text("当前实例：{0}", LocalizationKeys.ModPacksCurrentInstance, name)
        : Text("当前没有选中的游戏实例，导出与导入预览都不可用。", LocalizationKeys.ModPacksNoInstanceHint);

    // ---- 导出 --------------------------------------------------------------

    [ObservableProperty]
    private string _exportResultText = string.Empty;

    /// <summary>导出结果是否可显示。</summary>
    public bool HasExportResult => ExportResultText.Length > 0;

    /// <summary>是否可以导出（有实例且不忙）。</summary>
    public bool CanExport => !IsBusy && _modPacks.HasActiveInstance;

    // ---- 导入 --------------------------------------------------------------

    [ObservableProperty]
    private string _importPathText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ImportEntryRowViewModel> _previewRows = Array.Empty<ImportEntryRowViewModel>();

    [ObservableProperty]
    private ImportEntryRowViewModel? _selectedPreviewRow;

    [ObservableProperty]
    private string _previewSummaryText = string.Empty;

    [ObservableProperty]
    private string _previewWarningText = string.Empty;

    [ObservableProperty]
    private string _importResultText = string.Empty;

    /// <summary>预览表格是否有行。</summary>
    public bool HasPreviewRows => PreviewRows.Count > 0;

    /// <summary>是否显示预览空状态。</summary>
    public bool ShowPreviewEmptyState => PreviewRows.Count == 0 && !IsBusy;

    /// <summary>预览摘要是否可显示。</summary>
    public bool HasPreviewSummary => PreviewSummaryText.Length > 0;

    /// <summary>预览警告是否可显示。</summary>
    public bool HasPreviewWarning => PreviewWarningText.Length > 0;

    /// <summary>可勾选（会真正改动实例）的条目数。</summary>
    public int SelectableCount => PreviewRows.Count(row => row.CanSelect);

    /// <summary>已勾选的条目数。</summary>
    public int SelectedCount => PreviewRows.Count(row => row.CanSelect && row.IsSelected);

    /// <summary>预览条数与可导入条数。</summary>
    public string PreviewCountText => Text("共 {0} 条，可导入 {1} 条", LocalizationKeys.ModPacksPreviewCount, PreviewRows.Count, SelectableCount);

    /// <summary>勾选情况文案。</summary>
    public string SelectionText => Text("已勾选 {0} 条", LocalizationKeys.ModPacksSelectionCount, SelectedCount);

    /// <summary>是否有选中的预览行。</summary>
    public bool HasSelection => SelectedPreviewRow is not null;

    /// <summary>是否可以执行导入。</summary>
    public bool CanImport => !IsBusy && HasPreviewRows && SelectedCount > 0;

    /// <summary>导入结果是否可显示。</summary>
    public bool HasImportResult => ImportResultText.Length > 0;

    /// <summary>预览空状态标题。</summary>
    public string PreviewEmptyStateTitle => Text("还没有导入预览", LocalizationKeys.ModPacksPreviewEmptyTitle);

    /// <summary>预览空状态提示。</summary>
    public string PreviewEmptyStateHint => Text("每行填一个 .ckan 路径，再点「预览导入」。", LocalizationKeys.ModPacksPreviewEmptyHint);

    // ---- 比对 --------------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<string> _instanceNames = Array.Empty<string>();

    [ObservableProperty]
    private string? _leftInstance;

    [ObservableProperty]
    private string? _rightInstance;

    [ObservableProperty]
    private IReadOnlyList<DiffRowViewModel> _diffRows = Array.Empty<DiffRowViewModel>();

    [ObservableProperty]
    private string _diffSummaryText = string.Empty;

    /// <summary>对比表格是否有行。</summary>
    public bool HasDiffRows => DiffRows.Count > 0;

    /// <summary>是否显示对比空状态。</summary>
    public bool ShowDiffEmptyState => DiffRows.Count == 0 && !IsBusy;

    /// <summary>对比摘要是否可显示。</summary>
    public bool HasDiffSummary => DiffSummaryText.Length > 0;

    /// <summary>差异条数文案。</summary>
    public string DiffCountText => DiffRows.Count == 0 ? Text("两个实例的模组列表一致。", LocalizationKeys.ModPacksDiffIdentical) : Text("共 {0} 处差异", LocalizationKeys.ModPacksDiffCount, DiffRows.Count);

    /// <summary>是否可以开始对比（两个不同实例且不忙）。</summary>
    public bool CanCompare => !IsBusy
        && InstanceNames.Count >= 2
        && !string.IsNullOrWhiteSpace(LeftInstance)
        && !string.IsNullOrWhiteSpace(RightInstance)
        && !string.Equals(LeftInstance, RightInstance, StringComparison.OrdinalIgnoreCase);

    /// <summary>对比空状态标题。</summary>
    public string DiffEmptyStateTitle => Text("还没有对比结果", LocalizationKeys.ModPacksDiffEmptyTitle);

    /// <summary>对比空状态提示。</summary>
    public string DiffEmptyStateHint => Text("选两个实例后点「开始对比」，只读取。", LocalizationKeys.ModPacksDiffEmptyHint);

    // ---- 公共状态 ----------------------------------------------------------

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _errorDetail;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    /// <summary>是否有错误条。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误详情。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>是否空闲。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>是否有进度文案。</summary>
    public bool HasProgress => ProgressText.Length > 0;

    // ---- 生命周期 ----------------------------------------------------------

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (_subscribed)
        {
            return;
        }

        _instances.InstancesChanged += OnInstancesChanged;
        _instances.CurrentChanged += OnCurrentChanged;
        _subscribed = true;
        ApplyInstances();
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (!_subscribed)
        {
            return;
        }

        _instances.InstancesChanged -= OnInstancesChanged;
        _instances.CurrentChanged -= OnCurrentChanged;
        _subscribed = false;
    }

    // ---- 命令 --------------------------------------------------------------

    /// <summary>导出当前实例的已装模组列表。</summary>
    [RelayCommand]
    private Task ExportAsync() => RunAsync(
        async () =>
        {
            if (!_modPacks.HasActiveInstance)
            {
                SetError(Text("当前没有选中的游戏实例，请先选择一个实例。", LocalizationKeys.ModPacksErrorNoInstance), null);
                return;
            }

            if (ExportForm.IsEmpty)
            {
                ExportForm.ValidationText = Text("请填写导出文件的目标路径。", LocalizationKeys.ModPacksErrorExportPathRequired);
                SetError(Text("请填写导出文件的目标路径。", LocalizationKeys.ModPacksErrorExportPathRequired), null);
                return;
            }

            ExportResultText = string.Empty;
            ProgressPercent = 0;
            ProgressText = Text("正在导出模组列表…", LocalizationKeys.ModPacksBusyExporting);
            var result = await _modPacks.ExportAsync(ExportForm.ToRequest(), _exportProgress);
            if (result.IsSuccess && result.Value is { } exported)
            {
                ProgressPercent = 100;
                ProgressText = Text("导出完成。", LocalizationKeys.ModPacksStatusExportDone);
                ExportResultText = DescribeExport(exported);
                SetStatus(Text("已导出 {0} 个模组。", LocalizationKeys.ModPacksStatusExported, exported.ModuleCount));
                return;
            }

            ProgressText = string.Empty;
            SetError(ModPacksErrorText.Compose(result.Message, Text("导出模组列表失败。", LocalizationKeys.ModPacksErrorExportFailed), result.Retryable), result.Detail);
        },
        ExportContext,
        Text("正在导出模组列表…", LocalizationKeys.ModPacksBusyExporting));

    /// <summary>预览要导入的 .ckan 文件（只读取，不安装）。</summary>
    [RelayCommand]
    private Task PreviewImportAsync() => RunAsync(
        async () =>
        {
            var paths = ParsePaths(ImportPathText);
            if (paths.Count == 0)
            {
                SetError(Text("请填写至少一个 .ckan 文件路径（每行一个）。", LocalizationKeys.ModPacksErrorNoPaths), null);
                return;
            }

            _preview = null;
            PreviewRows = Array.Empty<ImportEntryRowViewModel>();
            PreviewSummaryText = string.Empty;
            PreviewWarningText = string.Empty;
            ImportResultText = string.Empty;
            var result = await _modPacks.PreviewImportAsync(paths);
            if (!result.IsSuccess || result.Value is not { } preview)
            {
                SetError(ModPacksErrorText.Compose(result.Message, Text("导入预览失败。", LocalizationKeys.ModPacksErrorPreviewFailed), result.Retryable), result.Detail);
                return;
            }

            ApplyPreview(preview);
            SetStatus(preview.Entries.Count == 0 ? Text("预览完成：没有可导入的条目。", LocalizationKeys.ModPacksStatusPreviewEmpty) : Text("预览完成：共 {0} 条。", LocalizationKeys.ModPacksStatusPreviewCount, preview.Entries.Count));
        },
        PreviewContext,
        Text("正在解析 .ckan 文件…", LocalizationKeys.ModPacksBusyParsing));

    /// <summary>按勾选执行导入（复用安装流水线）。</summary>
    [RelayCommand]
    private Task ImportAsync() => RunAsync(
        async () =>
        {
            if (_preview is not { } preview)
            {
                SetError(Text("请先预览要导入的 .ckan 文件。", LocalizationKeys.ModPacksErrorPreviewRequired), null);
                return;
            }

            var selected = PreviewRows
                .Where(row => row.CanSelect && row.IsSelected)
                .Select(row => row.Identifier)
                .ToArray();
            if (selected.Length == 0)
            {
                SetError(Text("请至少勾选一条可导入的条目。", LocalizationKeys.ModPacksErrorNoSelection), null);
                return;
            }

            ImportResultText = string.Empty;
            ProgressPercent = 0;
            ProgressText = Text("正在导入模组…", LocalizationKeys.ModPacksBusyImporting);
            var result = await _modPacks.ImportAsync(preview, selected, _importProgress);
            if (!result.IsSuccess || result.Value is not { } report)
            {
                ProgressText = string.Empty;
                SetError(ModPacksErrorText.Compose(result.Message, Text("导入失败。", LocalizationKeys.ModPacksErrorImportFailed), result.Retryable), result.Detail);
                return;
            }

            ProgressPercent = 100;
            ProgressText = Text("导入结束。", LocalizationKeys.ModPacksStatusImportDone);
            ImportResultText = DescribeImport(report);
            SetStatus(report.IsFullSuccess
                ? Text("导入完成：{0} 个模组已安装。", LocalizationKeys.ModPacksStatusImported, report.Installed.Count)
                : Text("导入结束：成功 {0} 个，失败 {1} 个。", LocalizationKeys.ModPacksStatusImportedPartial, report.Installed.Count, report.Failed.Count));
        },
        ImportContext,
        Text("正在导入模组…", LocalizationKeys.ModPacksBusyImporting));

    /// <summary>全选可导入的条目。</summary>
    [RelayCommand]
    private void SelectAllEntries()
    {
        foreach (var row in PreviewRows)
        {
            if (row.CanSelect)
            {
                row.IsSelected = true;
            }
        }

        NotifyPreviewState();
    }

    /// <summary>清空勾选。</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in PreviewRows)
        {
            row.IsSelected = false;
        }

        NotifyPreviewState();
    }

    /// <summary>对比左右两个实例的模组差异。</summary>
    [RelayCommand]
    private Task CompareAsync() => RunAsync(
        async () =>
        {
            if (string.IsNullOrWhiteSpace(LeftInstance) || string.IsNullOrWhiteSpace(RightInstance))
            {
                SetError(Text("请选择要对比的两个实例。", LocalizationKeys.ModPacksErrorCompareNeedTwo), null);
                return;
            }

            if (string.Equals(LeftInstance, RightInstance, StringComparison.OrdinalIgnoreCase))
            {
                SetError(Text("请选择两个不同的实例进行对比。", LocalizationKeys.ModPacksErrorCompareSame), null);
                return;
            }

            DiffRows = Array.Empty<DiffRowViewModel>();
            DiffSummaryText = string.Empty;
            var result = await _modPacks.CompareAsync(LeftInstance!, RightInstance!);
            if (!result.IsSuccess || result.Value is not { } comparison)
            {
                SetError(ModPacksErrorText.Compose(result.Message, Text("实例对比失败。", LocalizationKeys.ModPacksErrorCompareFailed), result.Retryable), result.Detail);
                return;
            }

            ApplyComparison(comparison);
            SetStatus(comparison.IsIdentical
                ? Text("对比完成：两个实例的模组列表一致。", LocalizationKeys.ModPacksStatusCompareIdentical)
                : Text("对比完成：共 {0} 处差异。", LocalizationKeys.ModPacksStatusCompareDiff, comparison.Items.Count));
        },
        CompareContext,
        Text("正在对比两个实例…", LocalizationKeys.ModPacksBusyComparing));

    /// <summary>重新读取实例列表（切实例后刷新左右候选）。</summary>
    [RelayCommand]
    private Task RefreshInstancesAsync() => RunAsync(
        async () =>
        {
            var result = await _instances.RefreshAsync();
            ApplyInstances();
            if (!result.IsSuccess)
            {
                SetError(ModPacksErrorText.Compose(result.Message, Text("读取实例列表失败。", LocalizationKeys.ModPacksErrorInstancesFailed), result.Retryable), result.Detail);
                return;
            }

            SetStatus(Text("已读取 {0} 个实例。", LocalizationKeys.ModPacksStatusInstances, InstanceNames.Count));
        },
        InstancesContext,
        Text("正在读取实例列表…", LocalizationKeys.ModPacksBusyInstances));

    /// <summary>关闭页面级错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    // ---- 内部实现 ----------------------------------------------------------

    /// <summary>把路径输入框拆成一行一个路径（同时接受分号分隔与首尾引号）。</summary>
    private static IReadOnlyList<string> ParsePaths(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var separators = new[] { '\r', '\n', ';' };
        return text
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim().Trim('"'))
            .Where(part => part.Length > 0)
            .ToArray();
    }

    private static string DescribeExport(ModPackExportResult exported)
    {
        var parts = new List<string>
        {
            "已写入 " + exported.Path,
            "包含 " + exported.ModuleCount + " 个模组"
        };
        if (!string.IsNullOrWhiteSpace(exported.ArchivePath))
        {
            parts.Add("归档 " + exported.ArchivePath + "（" + exported.BundledCount + " 个下载包，" + FormatBytes(exported.BundleBytes) + "）");
        }

        if (exported.Warnings.Count > 0)
        {
            parts.Add("警告：" + string.Join("；", exported.Warnings));
        }

        return string.Join("｜", parts);
    }

    private static string DescribeImport(ModPackImportReport report)
    {
        var parts = new List<string>
        {
            "成功 " + report.Installed.Count + " 个",
            "失败 " + report.Failed.Count + " 个"
        };
        if (report.Failed.Count > 0)
        {
            parts.Add("失败清单：" + string.Join("、", report.Failed));
        }

        if (report.Warnings.Count > 0)
        {
            parts.Add("警告：" + string.Join("；", report.Warnings));
        }

        return string.Join("｜", parts);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " 字节";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024d).ToString("0.0") + " KB";
        }

        return (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
    }

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
            SetError(ModPacksErrorText.FromException(ex, "导入导出操作失败。"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyPreview(ModPackPreview preview)
    {
        _preview = preview;
        PreviewRows = preview.Entries.Select(entry => new ImportEntryRowViewModel(entry)).ToArray();
        PreviewSummaryText = BuildPreviewSummary(preview);
        PreviewWarningText = preview.Warnings.Count == 0
            ? string.Empty
            : "警告：" + string.Join("；", preview.Warnings);
    }

    private static string BuildPreviewSummary(ModPackPreview preview)
    {
        var name = string.IsNullOrWhiteSpace(preview.Name) ? "元数据包" : preview.Name;
        var groups = preview.Entries
            .GroupBy(entry => entry.State)
            .OrderBy(group => (int)group.Key)
            .Select(group => ModPackStateText.Label(group.Key) + " " + group.Count())
            .ToArray();
        var body = groups.Length == 0 ? "没有可处理的条目" : string.Join("；", groups);
        return name + "｜共 " + preview.Entries.Count + " 条｜" + body;
    }

    private void ApplyComparison(InstanceComparison comparison)
    {
        DiffRows = comparison.Items.Select(DiffRowViewModel.From).ToArray();
        if (comparison.IsIdentical)
        {
            DiffSummaryText = "「" + comparison.LeftName + "」与「" + comparison.RightName + "」的模组列表完全一致。";
            return;
        }

        var groups = comparison.Items
            .GroupBy(item => item.Kind)
            .OrderBy(group => (int)group.Key)
            .Select(group => ModPackStateText.Label(group.Key) + " " + group.Count())
            .ToArray();
        DiffSummaryText = "「" + comparison.LeftName + "」↔「" + comparison.RightName + "」：" + string.Join("；", groups);
    }

    private void ApplyInstances()
    {
        var names = _instances.Instances
            .Select(info => info.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        InstanceNames = names;
        if (LeftInstance is null || !names.Contains(LeftInstance, StringComparer.Ordinal))
        {
            LeftInstance = names.FirstOrDefault();
        }

        // 右侧默认挑一个与左侧不同的实例；只有实例唯一时才两侧相同（此时对比按钮会禁用）。
        var rightUsable = RightInstance is not null
            && names.Contains(RightInstance, StringComparer.Ordinal)
            && !string.Equals(RightInstance, LeftInstance, StringComparison.OrdinalIgnoreCase);
        if (!rightUsable)
        {
            RightInstance = names.FirstOrDefault(name => !string.Equals(name, LeftInstance, StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault();
        }

        OnPropertyChanged(nameof(InstanceTitleText));
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanCompare));
    }

    private void OnInstancesChanged() => ApplyInstances();

    private void OnCurrentChanged(InstanceInfo? info)
    {
        OnPropertyChanged(nameof(InstanceTitleText));
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(CanExport));
    }

    private void OnExportProgress(OperationProgress progress) => ApplyProgress(progress, "正在导出模组列表…");

    private void OnImportProgress(OperationProgress progress) => ApplyProgress(progress, "正在导入模组…");

    private void ApplyProgress(OperationProgress progress, string fallback)
    {
        if (progress is null)
        {
            return;
        }

        ProgressPercent = Math.Clamp(progress.Percent, 0, 100);
        ProgressText = string.IsNullOrWhiteSpace(progress.Message)
            ? (string.IsNullOrWhiteSpace(progress.Title) ? fallback : progress.Title)
            : progress.Message!;
        if (progress.State == OperationState.Failed)
        {
            SetError(ProgressText, null);
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ImportEntryRowViewModel.IsSelected))
        {
            NotifyPreviewState();
        }
    }

    private void SetStatus(string message) => StatusText = message;

    private void SetError(string message, string? detail)
    {
        ErrorText = message;
        ErrorDetail = detail;
        StatusText = message;
    }

    partial void OnPreviewRowsChanged(IReadOnlyList<ImportEntryRowViewModel> value)
    {
        foreach (var row in _watchedRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _watchedRows = value ?? Array.Empty<ImportEntryRowViewModel>();
        foreach (var row in _watchedRows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        NotifyPreviewState();
    }

    partial void OnSelectedPreviewRowChanged(ImportEntryRowViewModel? value) => NotifyPreviewState();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanCompare));
        NotifyPreviewState();
        NotifyDiffState();
    }

    partial void OnPreviewSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasPreviewSummary));

    partial void OnPreviewWarningTextChanged(string value) => OnPropertyChanged(nameof(HasPreviewWarning));

    partial void OnDiffRowsChanged(IReadOnlyList<DiffRowViewModel> value) => NotifyDiffState();

    partial void OnDiffSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasDiffSummary));

    partial void OnExportResultTextChanged(string value) => OnPropertyChanged(nameof(HasExportResult));

    partial void OnImportResultTextChanged(string value) => OnPropertyChanged(nameof(HasImportResult));

    partial void OnProgressTextChanged(string value) => OnPropertyChanged(nameof(HasProgress));

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    partial void OnLeftInstanceChanged(string? value) => OnPropertyChanged(nameof(CanCompare));

    partial void OnRightInstanceChanged(string? value) => OnPropertyChanged(nameof(CanCompare));

    private void NotifyPreviewState()
    {
        OnPropertyChanged(nameof(HasPreviewRows));
        OnPropertyChanged(nameof(ShowPreviewEmptyState));
        OnPropertyChanged(nameof(SelectableCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(PreviewCountText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanImport));
    }

    private void NotifyDiffState()
    {
        OnPropertyChanged(nameof(HasDiffRows));
        OnPropertyChanged(nameof(ShowDiffEmptyState));
        OnPropertyChanged(nameof(DiffCountText));
    }

    // ---- 无参构造用的空实现（与 ReposPageViewModel / InstancesPageViewModel 同构） ----

    private sealed class EmptyModPackService : IModPackService
    {
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
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未接入实例服务。"));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未接入实例服务。"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未接入实例服务。"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Fail("尚未接入实例服务。"));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未接入实例服务。"));
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

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        private readonly Action<T> _callback = callback ?? throw new ArgumentNullException(nameof(callback));

        public void Report(T value) => _callback(value);
    }
}
