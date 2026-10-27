namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 「本 UI 启动的游戏退出了」的信号源（18 篇 §4/§9.2）。Core 的启动路径
/// （<c>GameInstance.PlayGame</c>）只给 <c>onExit</c> 回调，而 App 层的
/// <c>ShellInstanceLauncher</c> 用 <c>UseShellExecute=true</c> 拉起进程后立即释放句柄，
/// 拿不到 <c>Process.Exited</c>；因此这里约定一个一次性信号：谁先观测到退出就广播一次，
/// 实例页据此取消后续释放层并调用 <c>RestoreAsync</c>。
/// </summary>
public interface IInstanceLaunchExitSignal
{
    /// <summary>游戏退出。参数是启动时用的游戏目录，调用方据此忽略别的实例的退出。</summary>
    event Action<string>? GameExited;
}
