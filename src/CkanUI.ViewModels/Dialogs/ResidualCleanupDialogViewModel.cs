using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.ResidualCleanup;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 卸载残留目录清理对话框（12 篇 F-43 / 06 篇 UI-22）：列出残留目录、预览内容，
/// 让用户勾选着删，或者干脆全部保留。
/// <para>
/// 三个出口：<see cref="OpenDirectoryCommand"/>（打开目录看一眼）、
/// <see cref="DeleteSelectedCommand"/>（删除勾选，成功才关窗、<see cref="Result"/> 为
/// <see cref="ResidualCleanupChoice.Delete"/>）、
/// <see cref="DialogViewModelBase.SecondaryCommand"/>（全部保留，<see cref="Result"/> 为
/// <see cref="ResidualCleanupChoice.Keep"/>）。
/// </para>
/// </summary>
public sealed partial class ResidualCleanupDialogViewModel : DialogViewModelBase
{
    private const string GameDataFolder = "GameData";

    private readonly IResidualCleanupService _service;
    private readonly InstanceInfo _instance;
    private readonly IReadOnlyCollection<string>? _reportedByUninstall;
    private readonly IErrorReporter? _errors;

    /// <summary>创建对话框视图模型。</summary>
    /// <param name="service">残留清理服务。</param>
    /// <param name="instance">目标实例（服务是单例，GameDir 必须由调用方带进来）。</param>
    /// <param name="reportedByUninstall">卸载时后端报上来的残留目录（相对 GameData）；可为 <c>null</c>。</param>
    /// <param name="errors">错误上报器；可为 <c>null</c>（测试里不关心上报）。</param>
    public ResidualCleanupDialogViewModel(
        IResidualCleanupService service,
        InstanceInfo instance,
        IReadOnlyCollection<string>? reportedByUninstall = null,
        IErrorReporter? errors = null)
        : base("清理卸载残留目录")
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(instance);

        _service = service;
        _instance = instance;
        _reportedByUninstall = reportedByUninstall;
        _errors = errors;

        PrimaryButtonText = "删除勾选";
        SecondaryButtonText = "全部保留";
        SummaryText = "正在扫描残留目录…";
    }

    /// <summary>候选残留目录。</summary>
    public ObservableCollection<ResidualDirectoryRowViewModel> Rows { get; } = new();

    /// <summary>用户选择的出口；默认 <see cref="ResidualCleanupChoice.Keep"/>（什么都不删）。</summary>
    public ResidualCleanupChoice Result { get; private set; } = ResidualCleanupChoice.Keep;

    /// <summary>「打开目录」的宿主委托，由 App 的视图注入；为 <c>null</c> 时该出口给出中文提示。</summary>
    public IResidualCleanupShellLauncher? ShellLauncher { get; set; }

    /// <summary>该实例的 GameData 绝对路径（用于拼行的绝对路径）。</summary>
    public string GameDataPath => Path.Combine(_instance.GameDir, GameDataFolder);

    /// <summary>是否有可展示的行。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>扫描结束且一个都没发现。</summary>
    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    /// <summary>已勾选且可删除的目录数。</summary>
    public int SelectedCount => Rows.Count(row => row.IsSelected && row.IsDeletable);

    /// <summary>「删除勾选」是否可用。</summary>
    public bool CanDelete => SelectedCount > 0 && !IsLoading;

    /// <summary>勾选状态的说明文案。</summary>
    public string SelectionText => SelectedCount == 0
        ? "尚未勾选任何目录。"
        : $"已勾选 {SelectedCount} 个目录。";

    /// <summary>列表上方的汇总文案。</summary>
    [ObservableProperty]
    private string? _summaryText;

    /// <summary>底部状态文案（成功类反馈）。</summary>
    [ObservableProperty]
    private string? _statusText;

    /// <summary>是否正在忙（扫描/删除）。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>错误的补充说明（异常消息等技术细节）。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>当前选中的行（「打开目录」在没有 CommandParameter 时用它）。</summary>
    [ObservableProperty]
    private ResidualDirectoryRowViewModel? _selectedRow;

    /// <summary>是否有补充说明。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>主按钮由 <see cref="DeleteSelectedCommand"/> 承担（要 await 服务），基类钩子不关窗。</summary>
    protected override bool CloseAfterPrimary => false;

    /// <summary>扫描并列出候选残留目录。</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        ClearError();
        await LoadCoreAsync();
    }

    /// <summary>全选（只勾可删除的行）。</summary>
    [RelayCommand]
    public void SelectAll()
    {
        foreach (var row in Rows.Where(row => row.IsDeletable))
        {
            row.IsSelected = true;
        }

        RefreshSummary();
    }

    /// <summary>取消全选。</summary>
    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }

        RefreshSummary();
    }

    /// <summary>用系统文件管理器打开该残留目录。</summary>
    /// <param name="row">目标行；为 <c>null</c> 时用 <see cref="SelectedRow"/>。</param>
    [RelayCommand]
    public async Task OpenDirectoryAsync(ResidualDirectoryRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            SetError("请先选中一个残留目录。");
            return;
        }

        if (ShellLauncher is not { } launcher)
        {
            SetError("当前宿主没有提供「打开目录」的能力。");
            return;
        }

        ClearError();
        try
        {
            var result = await launcher.OpenFolderAsync(row.AbsolutePath);
            if (!result.IsSuccess)
            {
                SetError(result.Message ?? "打开目录失败。", result.Detail, result.Retryable);
                return;
            }

            StatusText = "已打开：" + row.AbsolutePath;
        }
        catch (Exception ex)
        {
            Report(ex, "打开残留目录");
            SetError("打开目录失败。", ex.Message, retryable: true);
        }
    }

    /// <summary>删除勾选的残留目录；成功才关窗。</summary>
    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        var selected = Rows
            .Where(row => row.IsSelected && row.IsDeletable)
            .Select(row => row.RelativePath)
            .ToArray();

        if (selected.Length == 0)
        {
            SetError("请先勾选要删除的残留目录。");
            return;
        }

        ClearError();
        IsLoading = true;
        try
        {
            var result = await _service.DeleteAsync(_instance, selected);
            if (!result.IsSuccess)
            {
                // 失败时先把列表刷新成磁盘上的真实状态，用户点重试才不会对着已删掉的目录再来一次。
                await ReloadAfterFailureAsync();
                SetError(result.Message ?? "删除残留目录失败。", result.Detail, result.Retryable);
                return;
            }

            Result = ResidualCleanupChoice.Delete;
            IsConfirmed = true;
            StatusText = DescribeDeleted(result.Value);
            Close();
        }
        catch (Exception ex)
        {
            Report(ex, "删除残留目录");
            SetError("删除残留目录时出错。", ex.Message, retryable: true);
        }
        finally
        {
            IsLoading = false;
            RefreshSummary();
        }
    }

    /// <summary>「全部保留」：不删任何东西，只关窗。</summary>
    protected override void OnSecondary()
    {
        Result = ResidualCleanupChoice.Keep;
        IsConfirmed = false;
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanDelete));
    }

    partial void OnErrorDetailChanged(string? value)
    {
        OnPropertyChanged(nameof(HasErrorDetail));
    }

    private async Task LoadCoreAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _service.ListAsync(_instance, _reportedByUninstall);
            if (!result.IsSuccess || result.Value is null)
            {
                ApplyRows(Array.Empty<ResidualDirectoryInfo>());
                SetError(result.Message ?? "列出残留目录失败。", result.Detail, result.Retryable);
                return;
            }

            ApplyRows(result.Value);
            StatusText = result.Value.Count == 0 ? "没有发现可清理的残留目录。" : null;
        }
        catch (Exception ex)
        {
            Report(ex, "列出残留目录");
            ApplyRows(Array.Empty<ResidualDirectoryInfo>());
            SetError("列出残留目录时出错。", ex.Message, retryable: true);
        }
        finally
        {
            IsLoading = false;
            RefreshSummary();
        }
    }

    private async Task ReloadAfterFailureAsync()
    {
        try
        {
            await LoadCoreAsync();
        }
        catch (Exception ex)
        {
            Report(ex, "刷新残留目录");
        }
    }

    private void ApplyRows(IReadOnlyList<ResidualDirectoryInfo> infos)
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Rows.Clear();
        foreach (var info in infos)
        {
            var row = new ResidualDirectoryRowViewModel(info)
            {
                AbsolutePath = Path.Combine(
                    GameDataPath,
                    info.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            };

            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        SelectedRow = Rows.Count > 0 ? Rows[0] : null;
        RefreshSummary();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResidualDirectoryRowViewModel.IsSelected))
        {
            RefreshSummary();
        }
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanDelete));

        if (Rows.Count == 0)
        {
            SummaryText = IsLoading ? "正在扫描残留目录…" : "没有发现可清理的残留目录。";
            return;
        }

        var files = Rows.Sum(row => (long)row.FileCount);
        var bytes = Rows.Sum(row => row.SizeBytes);
        var blocked = Rows.Count(row => !row.IsDeletable);
        SummaryText = blocked == 0
            ? $"共 {Rows.Count} 个残留目录，{files} 个文件，{ResidualDirectoryRowViewModel.FormatSize(bytes)}。"
            : $"共 {Rows.Count} 个残留目录（其中 {blocked} 个不能删除），{files} 个文件，"
              + $"{ResidualDirectoryRowViewModel.FormatSize(bytes)}。";
    }

    private static string DescribeDeleted(ResidualCleanupReport? report)
    {
        if (report is null)
        {
            return "已删除勾选的残留目录。";
        }

        var text = $"已删除 {report.DeletedCount} 个目录，释放 "
                   + $"{ResidualDirectoryRowViewModel.FormatSize(report.FreedBytes)}";
        return report.SkippedCount == 0
            ? text + "。"
            : text + $"，跳过 {report.SkippedCount} 个（受管内容或符号链接）。";
    }

    private void SetError(string message, string? detail = null, bool retryable = false)
    {
        ValidationError = retryable ? message + "（可以重试）" : message;
        ErrorDetail = detail;
    }

    private void ClearError()
    {
        ValidationError = null;
        ErrorDetail = null;
    }

    private void Report(Exception ex, string context) => _errors?.Report(ex, context);
}
