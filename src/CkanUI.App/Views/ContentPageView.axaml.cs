using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Content;

namespace CkanUI.App.Views;

/// <summary>内容页视图；状态与行为由 ViewModel 提供，注入桌面系统剪贴板与文件管理器能力。</summary>
public partial class ContentPageView : UserControl
{
    private ContentPageViewModel? _viewModel;

    public ContentPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as ContentPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.ShellLauncher ??= new ContentShellLauncher(this);
    }

    private sealed class ContentShellLauncher : IContentShellLauncher
    {
        private readonly WeakReference<Control> _owner;

        public ContentShellLauncher(Control owner)
        {
            _owner = new WeakReference<Control>(owner);
        }

        public async Task<OperationResult> CopyTextAsync(string text)
        {
            try
            {
                if (_owner.TryGetTarget(out var control))
                {
                    var topLevel = TopLevel.GetTopLevel(control);
                    if (topLevel?.Clipboard is { } clipboard)
                    {
                        await clipboard.SetTextAsync(text).ConfigureAwait(false);
                        return OperationResult.Ok();
                    }
                }
                return OperationResult.Fail("未获取到系统剪贴板。");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("写入剪贴板失败。", ex.Message);
            }
        }

        public Task<OperationResult> OpenFolderAsync(string absolutePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(absolutePath) || !Directory.Exists(absolutePath))
                {
                    return Task.FromResult(OperationResult.Fail("指定的目录不存在。"));
                }

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = absolutePath,
                    UseShellExecute = true,
                    Verb = "open"
                });

                return Task.FromResult(OperationResult.Ok());
            }
            catch (Exception ex)
            {
                return Task.FromResult(OperationResult.Fail("无法打开文件管理器。", ex.Message));
            }
        }
    }
}
