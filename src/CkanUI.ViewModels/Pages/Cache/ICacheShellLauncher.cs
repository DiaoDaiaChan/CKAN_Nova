using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Cache;

/// <summary>
/// 缓存页需要的外壳动作（F-50 / 03 篇 §7 / UI-19「打开缓存目录」）。
/// </summary>
/// <remarks>
/// 与 <c>ISettingsShellLauncher</c> 同一套先例：接口在 ViewModels，实现在 App 的视图里
/// （<c>CachePageView</c> 用 <c>ProcessStartInfo</c> + <c>UseShellExecute</c>），
/// 由视图在 DataContext 变化时注入页面 VM 的属性 —— VM 因此不引用 Avalonia。
/// </remarks>
public interface ICacheShellLauncher
{
    /// <summary>在文件管理器里打开目录。</summary>
    /// <param name="path">目录绝对路径。</param>
    Task<OperationResult> OpenFolderAsync(string path);
}
