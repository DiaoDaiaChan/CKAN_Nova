using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Dialogs.ResidualCleanup;

/// <summary>
/// 「打开目录」的宿主委托（与 <c>IUnmanagedShellLauncher</c> 同一套先例）：
/// ViewModels 不碰进程与文件管理器，由 App 的视图代码隐藏实现并注入。
/// </summary>
public interface IResidualCleanupShellLauncher
{
    /// <summary>用系统文件管理器打开该绝对路径。</summary>
    Task<OperationResult> OpenFolderAsync(string absolutePath);
}
