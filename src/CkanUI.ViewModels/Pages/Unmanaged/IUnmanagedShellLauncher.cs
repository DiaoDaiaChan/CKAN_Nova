using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Unmanaged;

/// <summary>
/// 「在文件夹中显示」的宿主委托（与 <c>IInstanceShellLauncher</c> 同一风格）：ViewModels 不碰进程与文件管理器，
/// 由 App 的视图代码隐藏实现并注入。
/// </summary>
public interface IUnmanagedShellLauncher
{
    /// <summary>在系统文件管理器中定位并选中该绝对路径。</summary>
    Task<OperationResult> ShowInFolderAsync(string absolutePath);
}
