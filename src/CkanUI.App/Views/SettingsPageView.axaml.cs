using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    public SettingsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        }
    }
}
