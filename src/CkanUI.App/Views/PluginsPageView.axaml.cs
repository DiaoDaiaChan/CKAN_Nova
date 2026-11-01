using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Plugins;

namespace CkanUI.App.Views;

/// <summary>
/// 插件页视图（规格 F-17 / 06 篇 UI-33）：只做宿主能力接线。
/// </summary>
/// <remarks>业务全在 <see cref="PluginsPageViewModel"/>；这里注入依赖平台 API 的 dll 选择器
/// （04 篇 §1 架构铁律：<c>Avalonia.*</c> 只允许出现在 App 层）。</remarks>
public partial class PluginsPageView : UserControl
{
    private PluginsPageViewModel? _viewModel;

    /// <summary>构造视图并接线（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public PluginsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as PluginsPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        // 用 ??= 让宿主（测试夹具 / 未来 DI 注册）已经塞好的实现优先。
        _viewModel.FilePicker ??= new AvaloniaPluginFilePicker(this);
    }

    /// <summary>行内开关：关掉「已激活」栏的开关等于停用该插件（先选中行，再走既有命令）。</summary>
    private void OnActivePluginToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle
            || toggle.DataContext is not PluginRowViewModel row
            || _viewModel is not { } viewModel)
        {
            return;
        }

        // 列表填充时绑定出来的 true 不该触发动作，只有"被用户关掉"才走停用。
        if (toggle.IsChecked != false)
        {
            return;
        }

        viewModel.SelectedActive = row;
        if (viewModel.DeactivateCommand.CanExecute(null))
        {
            viewModel.DeactivateCommand.Execute(null);
        }
    }

    /// <summary>行内开关：打开「未激活」栏的开关等于激活该插件。</summary>
    private void OnDormantPluginToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle
            || toggle.DataContext is not PluginRowViewModel row
            || _viewModel is not { } viewModel)
        {
            return;
        }

        if (toggle.IsChecked != true)
        {
            return;
        }

        viewModel.SelectedDormant = row;
        if (viewModel.ActivateCommand.CanExecute(null))
        {
            viewModel.ActivateCommand.Execute(null);
        }
    }
}

/// <summary>插件 dll 选择器实现（Avalonia <c>StorageProvider</c>）：F-17「新增插件」。</summary>
internal sealed class AvaloniaPluginFilePicker : IPluginFilePicker
{
    private static readonly FilePickerFileType PluginFileType = new("插件程序集")
    {
        Patterns = new[] { "*.dll" },
        MimeTypes = new[] { "application/octet-stream", "application/x-msdownload" },
    };

    private readonly Control _owner;

    public AvaloniaPluginFilePicker(Control owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public async Task<string?> PickPluginFileAsync(CancellationToken ct = default)
    {
        // 视图可能还没挂到可视化树上（例如首次 Activate 前调用），因此每次现取 TopLevel。
        var storage = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            return null;
        }

        var files = await storage
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要添加的插件 dll",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType> { PluginFileType },
            })
            .ConfigureAwait(true);

        ct.ThrowIfCancellationRequested();
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
