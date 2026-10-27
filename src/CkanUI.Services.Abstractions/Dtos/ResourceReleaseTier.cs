namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 启动后资源释放的档位（18 篇 §7）。档位决定最多执行到哪一层，
/// 层与层之间的执行时机由调用方（实例页的启动编排）按 18 篇 §4 的 T3/T4/T5 决定。
/// </summary>
public enum ResourceReleaseTier
{
    /// <summary>关闭：一列都不执行（用户显式关掉，或本次不是由本 UI 启动的游戏）。</summary>
    Off = 0,

    /// <summary>保守：只做视图层与应用缓存（L0+L1），无感，释放量最小。</summary>
    Conservative = 1,

    /// <summary>均衡（默认）：保守 + Core 数据层（L0–L2），释放量最大的可控部分，可逆。</summary>
    Balanced = 2,

    /// <summary>激进：均衡 + 托管堆与 OS 工作集（L0–L4），恢复期可能有 page fault（R-58）。</summary>
    Aggressive = 3,
}
