using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Content;

/// <summary>
/// 内容页宿主委托：复制路径与打开所在目录。由 App 层在视图中实现并注入。
/// </summary>
public interface IContentShellLauncher
{
    /// <summary>复制文本到系统剪贴板。</summary>
    Task<OperationResult> CopyTextAsync(string text);

    /// <summary>在系统文件管理器中定位并打开文件或目录。</summary>
    Task<OperationResult> OpenFolderAsync(string absolutePath);
}
