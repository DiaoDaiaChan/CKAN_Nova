using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Update;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 新版本提示对话框 ViewModel（F-52 / UI-27）。
/// 遵循 DialogViewModelBase 契约，解耦外部链接跳转委托。
/// </summary>
public sealed partial class UpdateAvailableDialogViewModel : DialogViewModelBase
{
    private readonly Func<string, Task<bool>>? _openUrlHandler;

    public string CurrentVersion { get; }
    public string LatestVersion { get; }
    public string? ReleaseNotes { get; }
    public string? ReleaseUrl { get; }
    public bool IsDevBuild { get; }
    public string UpdatePromptText { get; }

    /// <summary>错误消息提示文案（对齐单测 ErrorMessage 契约与基类 ValidationError）。</summary>
    public string? ErrorMessage => ValidationError;

    public ICommand OpenReleaseUrlCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>「立即安装」命令（F-52 下载 → 校验 → 解包 → 备份 → 替换，见 <see cref="IUpdateInstaller"/>）。</summary>
    public ICommand InstallCommand { get; }

    /// <summary>「取消安装」命令：只请求取消当前安装，已经替换的文件由服务层回滚。</summary>
    public ICommand CancelInstallCommand { get; }

    private CancellationTokenSource? _installCts;
    private bool _cancelRequested;

    /// <summary>更新包安装器（App 层在弹窗后注入）；未注入时「安装」给出可读提示而不是崩溃。</summary>
    [ObservableProperty]
    private IUpdateInstaller? _installer;

    /// <summary>本次要安装的更新包（下载地址 + 可选 SHA256）；为空表示上游没给出可直接下载的资源。</summary>
    [ObservableProperty]
    private UpdateInstallPlan? _installPlan;

    /// <summary>是否正在安装（进度区显示、安装按钮禁用）。</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>安装进度百分比（0-100）。</summary>
    [ObservableProperty]
    private int _installPercent;

    /// <summary>安装阶段文案（服务层给的中文进度消息）。</summary>
    [ObservableProperty]
    private string? _installStatusText;

    /// <summary>安装失败原因（用户可读一行中文）。</summary>
    [ObservableProperty]
    private string? _installErrorText;

    /// <summary>安装失败的技术细节（可展开）。</summary>
    [ObservableProperty]
    private string? _installErrorDetail;

    /// <summary>是否已经装完、需要重启应用才生效。</summary>
    [ObservableProperty]
    private bool _isRestartRequired;

    /// <summary>能否安装：接了安装器且上游给了可直接下载的资源（否则只能走发布页手动下载）。</summary>
    public bool CanInstall => Installer is not null && InstallPlan is not null && !IsInstalling;

    /// <summary>能否取消安装。</summary>
    public bool CanCancelInstall => IsInstalling && !_cancelRequested;

    /// <summary>是否显示进度区。</summary>
    public bool ShowInstallProgress => IsInstalling;

    /// <summary>是否安装失败。</summary>
    public bool HasInstallError => !string.IsNullOrEmpty(InstallErrorText);

    /// <summary>是否装完待重启。</summary>
    public bool HasRestartHint => IsRestartRequired;

    /// <summary>安装可用性说明（为什么不能点「立即安装」）。</summary>
    public string InstallAvailabilityText
    {
        get
        {
            if (Installer is null)
            {
                return "自动安装未接线：可以打开发布页手动下载。";
            }

            if (InstallPlan is null)
            {
                return "上游没有提供可直接下载的更新包地址：请打开发布页手动下载。";
            }

            return InstallPlan.HasChecksum
                ? $"将从 {InstallPlan.FileName} 安装：先下载并校验 SHA256，替换前自动备份旧文件。"
                : $"将从 {InstallPlan.FileName} 安装：上游未提供 SHA256，只做大小校验；替换前自动备份旧文件。";
        }
    }

    /// <summary>
    /// 接受完整 <see cref="UpdateCheckResult"/> 结果记录的构造函数。
    /// 上游给了可直接下载的资产时顺手推出安装计划；没给就留空，「立即安装」自动禁用。
    /// </summary>
    public UpdateAvailableDialogViewModel(
        UpdateCheckResult updateResult,
        Func<string, Task<bool>>? browserLauncher = null)
        : this(
            updateResult.CurrentVersion ?? "v0.0.0",
            updateResult.LatestVersion ?? "v0.0.0",
            updateResult.ReleaseNotes,
            updateResult.ReleaseUrl,
            updateResult.IsDevBuild,
            browserLauncher)
    {
        if (updateResult.HasAsset && Uri.TryCreate(updateResult.AssetUrl, UriKind.Absolute, out var assetUri))
        {
            InstallPlan = new UpdateInstallPlan(
                assetUri,
                updateResult.AssetSha256,
                updateResult.AssetSize,
                updateResult.LatestVersion);
        }
    }

    /// <summary>解构入参构造函数。</summary>
    public UpdateAvailableDialogViewModel(
        string currentVersion,
        string latestVersion,
        string? releaseNotes,
        string? releaseUrl,
        bool isDevBuild = false,
        Func<string, Task<bool>>? openUrlHandler = null)
        : base(isDevBuild ? "发现开发版更新" : "发现新版本")
    {
        CurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "v0.0.0" : currentVersion;
        LatestVersion = string.IsNullOrWhiteSpace(latestVersion) ? "v0.0.0" : latestVersion;
        ReleaseNotes = releaseNotes;
        ReleaseUrl = releaseUrl;
        IsDevBuild = isDevBuild;
        _openUrlHandler = openUrlHandler;

        PrimaryButtonText = "关闭";
        SecondaryButtonText = string.Empty;

        UpdatePromptText = isDevBuild
            ? $"检测到新的开发构建版本 {LatestVersion}（当前版本：{CurrentVersion}）。"
            : $"CKAN 存在可用的新版本 {LatestVersion}（当前版本：{CurrentVersion}）。";

        OpenReleaseUrlCommand = new AsyncRelayCommand(OpenReleaseUrlAsync);
        CloseCommand = PrimaryCommand;
        InstallCommand = new AsyncRelayCommand(InstallAsync);
        CancelInstallCommand = new RelayCommand(CancelInstall);
    }

    private async Task OpenReleaseUrlAsync()
    {
        ValidationError = null;
        OnPropertyChanged(nameof(ErrorMessage));

        if (string.IsNullOrWhiteSpace(ReleaseUrl))
        {
            ValidationError = "没有可用的发布页面链接。";
            OnPropertyChanged(nameof(ErrorMessage));
            return;
        }

        if (_openUrlHandler == null)
        {
            ValidationError = $"无法自动打开浏览器，请手动访问发布页面：{ReleaseUrl}";
            OnPropertyChanged(nameof(ErrorMessage));
            return;
        }

        try
        {
            var success = await _openUrlHandler(ReleaseUrl).ConfigureAwait(false);
            if (!success)
            {
                ValidationError = $"无法打开默认浏览器，请手动访问：{ReleaseUrl}";
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }
        catch (Exception ex)
        {
            ValidationError = $"打开链接失败: {ex.Message}（{ReleaseUrl}）";
            OnPropertyChanged(nameof(ErrorMessage));
        }
    }

    private async Task InstallAsync()
    {
        if (IsInstalling)
        {
            return;
        }

        // 属性可能在别的线程被换掉，先抓成局部变量再判空，避免解引用时被改写。
        var installer = Installer;
        var plan = InstallPlan;

        if (installer is null)
        {
            SetInstallError("自动安装未接线：未提供更新安装器，请打开发布页手动下载。", ReleaseUrl);
            return;
        }

        if (plan is null)
        {
            SetInstallError("上游没有提供可直接下载的更新包地址，请打开发布页手动下载。", ReleaseUrl);
            return;
        }

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        _cancelRequested = false;
        ClearInstallError();
        IsRestartRequired = false;
        InstallPercent = 0;
        InstallStatusText = "正在准备安装…";
        IsInstalling = true;
        OnPropertyChanged(nameof(CanCancelInstall));

        try
        {
            var progress = new InlineProgress<UpdateInstallProgress>(OnInstallProgress);
            // 不 ConfigureAwait(false)：进度与结果都要回到 UI 线程更新可观察状态。
            var result = await installer.InstallAsync(plan, progress, _installCts.Token);

            if (!result.IsSuccess || result.Value is null)
            {
                InstallStatusText = _cancelRequested ? "已取消安装" : "安装失败";
                SetInstallError(result.Message ?? "更新安装失败。", result.Detail);
                return;
            }

            InstallPercent = 100;
            InstallStatusText = result.Value.Message;
            IsRestartRequired = result.Value.RestartRequired;
        }
        catch (OperationCanceledException)
        {
            InstallStatusText = "已取消安装";
            SetInstallError("已取消安装，程序文件没有被替换。", null);
        }
        catch (Exception ex)
        {
            InstallStatusText = "安装失败";
            SetInstallError($"安装更新失败：{ex.Message}", ex.ToString());
        }
        finally
        {
            IsInstalling = false;
            _cancelRequested = false;
            OnPropertyChanged(nameof(CanCancelInstall));
        }
    }

    private void CancelInstall()
    {
        if (!IsInstalling || _cancelRequested)
        {
            return;
        }

        _cancelRequested = true;
        InstallStatusText = "正在取消安装…";
        OnPropertyChanged(nameof(CanCancelInstall));

        try
        {
            _installCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 安装已结束并释放了取消源：忽略即可，结果由安装任务自己收尾。
        }
    }

    private void OnInstallProgress(UpdateInstallProgress progress)
    {
        if (progress is null)
        {
            return;
        }

        InstallPercent = progress.Percent;
        InstallStatusText = progress.Message;
    }

    private void SetInstallError(string message, string? detail)
    {
        InstallErrorText = message;
        InstallErrorDetail = detail;
        ValidationError = message;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void ClearInstallError()
    {
        InstallErrorText = null;
        InstallErrorDetail = null;
        ValidationError = null;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    partial void OnInstallerChanged(IUpdateInstaller? value) => OnPropertyChanged(nameof(CanInstall));

    partial void OnInstallPlanChanged(UpdateInstallPlan? value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallAvailabilityText));
    }

    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanCancelInstall));
        OnPropertyChanged(nameof(ShowInstallProgress));
    }

    partial void OnInstallErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasInstallError));

    partial void OnIsRestartRequiredChanged(bool value) => OnPropertyChanged(nameof(HasRestartHint));

    /// <summary>
    /// 同步回调的进度实现：<see cref="Progress{T}"/> 在没有同步上下文的 headless 环境里会把回调丢到线程池，
    /// 导致单测断言时机不确定，这里直呼直回。
    /// </summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report) => _report = report ?? throw new ArgumentNullException(nameof(report));

        public void Report(T value) => _report(value);
    }
}
