namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 首次启动指引的宿主入口：VM 只发「请打开向导」的请求，真实窗口、服务装配与 owner 都在 App 层。
/// </summary>
/// <remarks>
/// 与 <c>IInstanceFolderPicker</c> 同一理由：向导需要 Avalonia 的 <c>ShowDialog</c> 与 DI 容器里的服务，
/// ViewModels 层两者都不能碰；用接口把这件事留成宿主能力，headless 测试塞替身即可。
/// </remarks>
public interface IFirstRunWizardLauncher
{
    /// <summary>打开首次启动指引；用户中途退出不算失败。</summary>
    Task LaunchAsync();
}
