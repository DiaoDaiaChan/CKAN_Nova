using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Dialogs.ResidualCleanup;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 卸载残留目录清理对话框视图（12 篇 F-43 / 06 篇 UI-22）：只做宿主能力接线与首次扫描。
/// </summary>
/// <remarks>业务全部在 <see cref="ResidualCleanupDialogViewModel"/> 里；这里注入依赖进程 API 的
/// 「打开目录」实现（04 篇 §1 架构铁律：<c>Avalonia.*</c> 与进程 API 只允许出现在 App 层）。</remarks>
public partial class ResidualCleanupDialogView : DialogWindow
{
    private bool _loaded;

    /// <summary>构造视图并加载 XAML（<c>IDialogPresenter</c> 用无参构造反射创建）。</summary>
    public ResidualCleanupDialogView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not ResidualCleanupDialogViewModel viewModel)
        {
            return;
        }

        // 用 ??= 让宿主（测试夹具 / 未来 DI 注册）已经塞好的实现优先。
        viewModel.ShellLauncher ??= new ShellResidualCleanupLauncher();

        if (_loaded)
        {
            return;
        }

        _loaded = true;

        // 首次拿到 VM 就扫一遍；LoadAsync 内部已兜住异常，不会抛到这里。
        _ = viewModel.LoadCommand.ExecuteAsync(null);
    }
}

/// <summary>「打开目录」的宿主实现：调系统外壳打开该目录本身。</summary>
internal sealed class ShellResidualCleanupLauncher : IResidualCleanupShellLauncher
{
    /// <inheritdoc />
    public Task<OperationResult> OpenFolderAsync(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return Task.FromResult(OperationResult.Fail("路径为空，无法打开目录。"));
        }

        if (!Directory.Exists(absolutePath))
        {
            return Task.FromResult(
                OperationResult.Fail($"目录不存在：{absolutePath}（可能已被删除，请刷新后重试）"));
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(absolutePath) { UseShellExecute = true });
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("打开文件管理器失败。", ex.ToString(), retryable: true));
        }
    }
}
