namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 启动游戏的状态机（18 篇 §4，E6）。
/// </summary>
/// <remarks>
/// <para>阶段线性推进：<see cref="Idle"/> → <see cref="Preparing"/>（先还注册表锁）→
/// <see cref="Starting"/>（拉起进程）→ <see cref="Running"/>（游戏运行，可能已进入精简模式）→
/// <see cref="Restoring"/>（退出或手动恢复数据）→ 回到 <see cref="Idle"/>。</para>
/// <para>它是<b>页面的</b>状态而不是进程的：实例本就互斥，同一时刻只有一个阶段；失败路径一律回落到
/// <see cref="Idle"/>，不把界面卡在中间态。</para>
/// </remarks>
public enum GameLaunchPhase
{
    /// <summary>空闲：没有启动中的游戏。</summary>
    Idle = 0,

    /// <summary>准备中：先卸载 Core 数据（放掉注册表锁），再拉进程。</summary>
    Preparing = 1,

    /// <summary>启动中：外壳启动器正在创建进程。</summary>
    Starting = 2,

    /// <summary>运行中：进程已拉起，可能已进入精简模式。</summary>
    Running = 3,

    /// <summary>恢复中：游戏退出或用户手动触发，正在把注册表与仓库快照读回来。</summary>
    Restoring = 4,
}
