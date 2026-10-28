using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;

namespace CkanUI.App.Views;

/// <summary>设置页视图（03 篇 §7 / 06 篇 UI-07）。只做布局与绑定，逻辑全部在 SettingsPageViewModel。</summary>
/// <remarks>
/// 视图专属的两件事：把依赖平台 API 的「打开目录」能力注入 VM（04 篇 §1 架构铁律：
/// <c>Avalonia.*</c> 与 <c>Process</c> 只允许出现在 App 层，VM 侧只认 <see cref="ISettingsShellLauncher"/>），
/// 以及在 DataContext 变化时把自身作为实现挂上去（ViewLocator 走无参构造，没有别的注入点）。
/// </remarks>
public sealed partial class SettingsPageView : UserControl, ISettingsShellLauncher
{
    /// <summary>初始化视图（ViewLocator 通过无参构造创建）。</summary>
    private IPlatformSettings? _platformSettings;

    /// <summary>初始化视图（ViewLocator 通过无参构造创建）。</summary>
    public SettingsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookPlatformSettings();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnhookPlatformSettings();
    }

    /// <summary>系统配色/对比度变化时重算接管状态（用户可能在设置页开着的时候就去系统里切了高对比度）。</summary>
    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e) =>
        (DataContext as SettingsPageViewModel)?.RefreshAppearanceLock();

    private void HookPlatformSettings()
    {
        // Avalonia 12 的 TopLevel 不再暴露 PlatformSettings（只有 Application 有），故一律走应用级单例。
        var platformSettings = Application.Current?.PlatformSettings;
        if (platformSettings is null || ReferenceEquals(platformSettings, _platformSettings))
        {
            return;
        }

        UnhookPlatformSettings();
        _platformSettings = platformSettings;
        platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
    }

    private void UnhookPlatformSettings()
    {
        if (_platformSettings is null)
        {
            return;
        }

        _platformSettings.ColorValuesChanged -= OnPlatformColorValuesChanged;
        _platformSettings = null;
    }

    /// <inheritdoc />
    public Task<OperationResult> OpenFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(OperationResult.Fail("目录为空，无法打开。"));
        }

        if (!Directory.Exists(path))
        {
            return Task.FromResult(OperationResult.Fail($"目录不存在：{path}（不可重试）"));
        }

        try
        {
            // UseShellExecute 让资源管理器接管，本体不等待子进程退出。
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("打开目录失败", ex.Message));
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.ShellLauncher ??= this;

            // 强调色的对比度门禁要按「当前生效的」主题底色算：跟随系统时只有这里知道到底落在哪一档。
            viewModel.DarkThemeProbe ??= () => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

            // 系统高对比度压过用户选择（20 篇 P-02）：这里只读平台设置，真正的切换由 ThemeHost 负责。
            viewModel.HighContrastProbe ??= () => ThemeHost.IsSystemHighContrast(Application.Current);
            viewModel.RefreshAppearanceLock();
        }
    }
}
