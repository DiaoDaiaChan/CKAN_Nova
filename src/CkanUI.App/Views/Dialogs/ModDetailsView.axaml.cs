using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 详情面板视图（03 篇 §3.1 / 06 篇 UI-03）：嵌在模组页右侧，不是窗口，因此是 <see cref="UserControl"/>。
/// 数据上下文为 <c>ModDetailsViewModel</c>，空状态与选中状态互斥显示。
/// </summary>
public sealed partial class ModDetailsView : UserControl
{
    /// <summary>初始化视图。</summary>
    public ModDetailsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ModDetailsViewModel vm && vm.ShellLauncher is null)
        {
            vm.ShellLauncher = new ModDetailsShellLauncher(this);
        }
    }

    private sealed class ModDetailsShellLauncher(Control owner) : IModDetailsShellLauncher
    {
        public async Task<OperationResult> OpenBrowserAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return OperationResult.Fail("URL 为空");
            }

            var topLevel = TopLevel.GetTopLevel(owner);
            if (topLevel?.Launcher is { } launcher && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var success = await launcher.LaunchUriAsync(uri).ConfigureAwait(true);
                return success ? OperationResult.Ok() : OperationResult.Fail("启动浏览器失败");
            }

            try
            {
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }
    }
}
