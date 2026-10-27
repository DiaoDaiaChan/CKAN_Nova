using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Unmanaged;

namespace CkanUI.App.Views;

/// <summary>
/// 未管理文件页视图（规格 F-42 / 03 篇 §2 V-07）：只做宿主能力接线与双击定位。
/// </summary>
/// <remarks>业务全部在 <see cref="UnmanagedPageViewModel"/> 里；这里注入依赖平台 API 的「在文件夹中显示」实现
/// （04 篇 §1 架构铁律：<c>Avalonia.*</c> 与进程 API 只允许出现在 App 层）。</remarks>
public partial class UnmanagedPageView : UserControl
{
    private UnmanagedPageViewModel? _viewModel;

    /// <summary>构造视图并接线（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public UnmanagedPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as UnmanagedPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        // 用 ??= 让宿主（测试夹具 / 未来 DI 注册）已经塞好的实现优先。
        _viewModel.ShellLauncher ??= new ShellUnmanagedLauncher();
    }

    /// <summary>双击树节点 = 在文件夹中显示（与原 GUI 的 UnmanagedFiles 双击行为一致）。</summary>
    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel?.ShowInFolderCommand.CanExecute(null) == true)
        {
            _viewModel.ShowInFolderCommand.Execute(null);
        }
    }
}

/// <summary>「在文件夹中显示」的宿主实现：调系统外壳定位并选中目标。</summary>
internal sealed class ShellUnmanagedLauncher : IUnmanagedShellLauncher
{
    /// <inheritdoc />
    public Task<OperationResult> ShowInFolderAsync(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return Task.FromResult(OperationResult.Fail("路径为空，无法在文件夹中显示。"));
        }

        var exists = File.Exists(absolutePath) || Directory.Exists(absolutePath);
        if (!exists)
        {
            return Task.FromResult(OperationResult.Fail($"路径不存在：{absolutePath}（可能已被删除，请刷新后重试）"));
        }

        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{absolutePath}\"") { UseShellExecute = true }
                : new ProcessStartInfo(Path.GetDirectoryName(absolutePath) ?? absolutePath) { UseShellExecute = true };
            using var process = Process.Start(startInfo);
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("打开文件管理器失败。", ex.ToString(), retryable: true));
        }
    }
}
