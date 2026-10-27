using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 外壳动作：打开实例目录 / 启动游戏（03 篇 §5「打开目录」，18 篇启动流程）。
/// </summary>
/// <remarks>
/// 启动游戏涉及资源释放（18 篇 E6 精简模式），完整状态机属于 <c>GameLaunchViewModel</c> 的范畴；
/// M1 这里只保证"命令存在、提示到位、失败可读"，具体实现由 App 层提供并在接入 E6 后替换。
/// </remarks>
public interface IInstanceShellLauncher
{
    /// <summary>在文件管理器里打开目录。</summary>
    Task<OperationResult> OpenFolderAsync(string path);

    /// <summary>启动该目录里的 KSP；未找到可执行文件时返回可读失败。</summary>
    Task<OperationResult> LaunchGameAsync(string gameDir);
}
