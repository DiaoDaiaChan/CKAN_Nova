using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 外壳动作：打开实例目录 / 按启动命令启动游戏（03 篇 §5「打开目录」，18 篇启动流程，F-05/F-06）。
/// </summary>
/// <remarks>
/// <para>启动游戏涉及资源释放（18 篇 E6 精简模式），完整状态机属于 <c>GameLaunchViewModel</c> 的范畴；
/// 这里只负责「拿一条命令行 → 交给系统外壳执行 → 把失败翻成可读结果」，进程创建在 App 层实现。</para>
/// <para>命令行由 VM 侧解析（<see cref="LaunchCommandLine"/>）：可执行文件在前、参数在后，
/// Steam 链接（<c>steam://rungameid/…</c>）整条交给外壳，因此实现方不要自己再拼接 <c>-single-instance</c> 之类的参数。</para>
/// </remarks>
public interface IInstanceShellLauncher
{
    /// <summary>在文件管理器里打开目录。</summary>
    Task<OperationResult> OpenFolderAsync(string path);

    /// <summary>
    /// 按给定启动命令启动游戏；命令为空、路径不存在或进程启动失败时返回可读失败。
    /// </summary>
    /// <param name="gameDir">实例的游戏目录（作为工作目录，对齐 Core <c>PlayGame</c>）。</param>
    /// <param name="command">完整启动命令行（如 <c>KSP_x64.exe -single-instance</c> 或 <c>steam://rungameid/220200</c>）。</param>
    Task<OperationResult> LaunchGameAsync(string gameDir, string command);
}
