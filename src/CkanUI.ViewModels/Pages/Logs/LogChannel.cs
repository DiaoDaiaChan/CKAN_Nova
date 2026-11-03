namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 日志页的两档视角：CKAN 自身的界面/内核日志，和当前实例的 KSP 游戏日志。
/// 为什么在视图模型层分档而不是只靠来源勾选：玩家排障的默认问题就是「这次是 CKAN 出错还是游戏出错」，
/// 这层选择该是一个开关，而不是教用户去过滤条里认哪几路来源属于游戏。
/// </summary>
public enum LogChannel
{
    /// <summary>CKAN 侧：log4net / IUser / 未捕获异常 / 子进程。</summary>
    Gui = 0,

    /// <summary>游戏侧：当前实例 <c>KSP.log</c>（含 Unity 玩家日志）。</summary>
    Game = 1,
}
