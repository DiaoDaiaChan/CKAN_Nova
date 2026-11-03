using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Assets;

/// <summary>资产页共用的宿主能力：文件/目录选择、系统文件管理器、文本输入。</summary>
/// <remarks>与插件页、实例页同一套路：Avalonia 只出现在 App 层，VM 侧只认纯接口。</remarks>
public sealed class AvaloniaAssetFilePicker : IAssetFilePicker
{
    private readonly Control _owner;

    public AvaloniaAssetFilePicker(Control owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public async Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, CancellationToken ct = default)
    {
        // 视图可能还没挂到可视化树上，因此每次现取 TopLevel。
        var storage = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            return null;
        }

        var fileType = new FilePickerFileType(title)
        {
            Patterns = extensions.Select(ext => "*" + ext).ToArray(),
        };

        var files = await storage
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType> { fileType },
            })
            .ConfigureAwait(true);

        ct.ThrowIfCancellationRequested();
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title, string? startPath = null, CancellationToken ct = default)
    {
        var storage = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (storage is null || !storage.CanPickFolder)
        {
            return null;
        }

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            try
            {
                start = await storage.TryGetFolderFromPathAsync(startPath).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                start = null;
            }
        }

        var folders = await storage
            .OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = start,
            })
            .ConfigureAwait(true);

        ct.ThrowIfCancellationRequested();
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}

/// <summary>资产页的「在文件夹中显示」实现（与存档页 <c>ShellSavesLauncher</c> 同一套做法）。</summary>
public sealed class ShellAssetsLauncher : IAssetsShellLauncher
{
    /// <inheritdoc />
    public OperationResult Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult.Fail("路径为空，无法打开。");
        }

        try
        {
            if (Directory.Exists(path))
            {
                var started = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                started?.Dispose();
                return OperationResult.Ok();
            }

            if (!File.Exists(path))
            {
                return OperationResult.Fail("路径不存在：" + path, path, retryable: false);
            }

            // 文件走资源管理器的「选中」动作，省得用户自己在一堆截图里找。
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            info.ArgumentList.Add("/select," + path);
            var process = Process.Start(info);
            process?.Dispose();
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return OperationResult.Fail("打开目录失败：系统没有可用的文件管理器。", ex.Message, retryable: false);
        }
    }
}
