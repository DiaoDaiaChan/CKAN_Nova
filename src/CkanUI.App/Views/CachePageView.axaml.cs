using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Cache;

namespace CkanUI.App.Views;

/// <summary>
/// 下载缓存管理页视图（规格 F-50 / F-40 / 06 篇 UI-19）：只做宿主能力接线。
/// </summary>
/// <remarks>业务全在 <see cref="CachePageViewModel"/>；这里注入依赖平台 API 的 ZIP 选择器
/// （04 篇 §1 架构铁律：<c>Avalonia.*</c> 只允许出现在 App 层）。</remarks>
public partial class CachePageView : UserControl
{
    private CachePageViewModel? _viewModel;

    /// <summary>构造视图并接线（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public CachePageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as CachePageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        // 用 ??= 让宿主（测试夹具 / 未来 DI 注册）已经塞好的实现优先。
        _viewModel.ZipPicker ??= new AvaloniaCacheZipPicker(this);
    }
}

/// <summary>ZIP 选择器实现（Avalonia <c>StorageProvider</c>）：F-40「本地 ZIP 灌缓存」。</summary>
internal sealed class AvaloniaCacheZipPicker : ICacheZipPicker
{
    private static readonly FilePickerFileType ZipFileType = new("ZIP 压缩包")
    {
        Patterns = new[] { "*.zip" },
        MimeTypes = new[] { "application/zip", "application/x-zip-compressed" },
    };

    private readonly Control _owner;

    public AvaloniaCacheZipPicker(Control owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public async Task<string?> PickZipFileAsync(CancellationToken ct = default)
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
                Title = "选择要灌入下载缓存的模块 ZIP",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType> { ZipFileType },
            })
            .ConfigureAwait(true);

        ct.ThrowIfCancellationRequested();
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
