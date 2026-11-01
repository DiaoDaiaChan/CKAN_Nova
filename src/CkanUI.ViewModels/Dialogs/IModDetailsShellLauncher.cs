using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 模组详情面板需要的外壳动作（打开 SpaceDock 网页等）。
/// 与 <c>ISavesShellLauncher</c> / <c>ISettingsShellLauncher</c> 同一套先例：接口在 ViewModels，
/// 实现在 App 的视图里（<c>Process.Start</c> + <c>UseShellExecute</c>），由视图在 DataContext 变化时注入，
/// VM 因此不引用 Avalonia / 平台 API。
/// </summary>
public interface IModDetailsShellLauncher
{
    /// <summary>在系统默认浏览器中打开指定 URL。</summary>
    /// <param name="url">目标网页 URL。</param>
    Task<OperationResult> OpenBrowserAsync(string url);
}
