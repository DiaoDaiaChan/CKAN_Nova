using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Assets;

/// <summary>资产页共用的文件选择通道（由视图注入，VM 不依赖具体的 Avalonia 选择器）。</summary>
/// <remarks>与 <c>IPluginFilePicker</c> / <c>IInstanceFolderPicker</c> 同一套路：宿主的 StorageProvider 在无头测试里跑不了。</remarks>
public interface IAssetFilePicker
{
    /// <summary>选一个待导入的文件。</summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="extensions">允许的扩展名（含点，例如 <c>.craft</c>）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>文件绝对路径；用户取消返回 null。</returns>
    Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, CancellationToken cancellationToken = default);

    /// <summary>选一个目录（加载图目录清单用）。</summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="startPath">起始目录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>目录绝对路径；用户取消返回 null。</returns>
    Task<string?> PickFolderAsync(string title, string? startPath = null, CancellationToken cancellationToken = default);
}

/// <summary>资产页「在系统文件管理器里打开」通道。</summary>
public interface IAssetsShellLauncher
{
    /// <summary>用系统默认方式打开路径。</summary>
    /// <param name="path">目录或文件绝对路径。</param>
    OperationResult Reveal(string path);
}
