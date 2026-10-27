using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CkanUI.Services.Abstractions.Dtos;
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

    /// <summary>接受完整 UpdateCheckResult 结果记录的构造函数。</summary>
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
}
