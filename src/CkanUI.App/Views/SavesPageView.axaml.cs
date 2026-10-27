using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Saves;

namespace CkanUI.App.Views;

/// <summary>
/// 存档工作台视图（13 篇 §7 / 03 篇 §14 / 06 篇 E1）：只做绑定与平台能力接线。
/// </summary>
/// <remarks>
/// 业务全部在 <see cref="SavesPageViewModel"/> 里（04 篇 §1）；本文件只负责把
/// 「在文件夹中显示」这类要碰平台 API 的宿主能力注入 VM（VM 侧只认
/// <see cref="ISavesShellLauncher"/>，因此不引用 Avalonia）。
/// </remarks>
public partial class SavesPageView : UserControl
{
    private SavesPageViewModel? _viewModel;

    /// <summary>构造视图（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public SavesPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as SavesPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        // ??= 让宿主（测试夹具 / 未来的 DI 注册）已经塞好的实现优先。
        _viewModel.ShellLauncher ??= new ShellSavesLauncher();
    }
}

/// <summary>
/// 外壳动作实现：在资源管理器中打开目录（与 <c>ShellInstanceLauncher</c> 同一套做法）。
/// </summary>
internal sealed class ShellSavesLauncher : ISavesShellLauncher
{
    /// <inheritdoc />
    public Task<OperationResult> OpenFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(OperationResult.Fail("目录为空，无法打开。"));
        }

        if (!Directory.Exists(path))
        {
            return Task.FromResult(OperationResult.Fail($"目录不存在：{path}", path, retryable: false));
        }

        try
        {
            var started = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            started?.Dispose();
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return Task.FromResult(OperationResult.Fail("打开目录失败：系统没有可用的文件管理器。", ex.Message, retryable: false));
        }
    }
}
