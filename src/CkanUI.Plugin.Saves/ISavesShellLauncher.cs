using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Saves;

/// <summary>
/// 存档页需要的外壳动作（03 篇 §14.2「在资源管理器中显示」）。
/// </summary>
/// <remarks>
/// 与 <c>IInstanceShellLauncher</c> / <c>ISettingsShellLauncher</c> 同一套先例：接口在 ViewModels，
/// 实现在 App 的视图里（<c>Process.Start</c> + <c>UseShellExecute</c>），由视图在 DataContext 变化时注入，
/// VM 因此不引用 Avalonia / 平台 API。
/// </remarks>
public interface ISavesShellLauncher
{
    /// <summary>在文件管理器里打开目录（通常传存档所在目录或 saves/ 目录）。</summary>
    /// <param name="path">目录绝对路径。</param>
    Task<OperationResult> OpenFolderAsync(string path);
}
