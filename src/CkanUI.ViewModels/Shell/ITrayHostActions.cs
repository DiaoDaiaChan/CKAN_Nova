using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Shell;

/// <summary>
/// 托盘菜单需要的宿主动作（12 篇 F-51 / UI-26）。接口留在视图模型层，实现放在 App 层
/// （<c>TrayHostActions</c>），与 <c>ISettingsShellLauncher</c> 同一套先例：
/// 视图模型只认这个窄接口，不引用 Avalonia，也不引用 CKAN。
/// </summary>
/// <remarks>
/// 七个动作都是 <see cref="Task"/>：菜单回调可能来自系统托盘的托管线程，
/// 实现方负责自行切回 UI 线程（见 <c>IDispatcher</c>）。
/// 返回 <see cref="OperationResult"/> 的三个目录动作用户可读失败原因，
/// 供托盘菜单把中文错误写进 <see cref="TrayMenuViewModel.StatusText"/>。
/// </remarks>
public interface ITrayHostActions
{
    /// <summary>显示主窗口：还原最小化状态并抢焦点。</summary>
    Task ShowWindow();

    /// <summary>隐藏主窗口（只留托盘图标，进程继续运行）。</summary>
    Task HideWindow();

    /// <summary>在文件管理器里打开日志目录（真源来自服务层，不由 App 拼路径）。</summary>
    Task<OperationResult> OpenLogFolder();

    /// <summary>在文件管理器里打开下载缓存目录（真源来自缓存服务）。</summary>
    Task<OperationResult> OpenCacheFolder();

    /// <summary>在文件管理器里打开当前实例目录。</summary>
    Task<OperationResult> OpenInstanceFolder();

    /// <summary>联网刷新仓库索引；失败时保留旧快照并带回可重试错误。</summary>
    Task<OperationResult> RefreshRepositories();

    /// <summary>退出应用（托盘常驻场景下这是唯一的正常退出入口）。</summary>
    Task ExitAsync();
}
