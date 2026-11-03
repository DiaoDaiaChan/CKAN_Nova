namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 页面行投影的裁剪契约（18 篇 §3 的 L1 档）。
/// </summary>
/// <remarks>
/// 为什么放在视图模型层：所谓「投影」是从服务快照算出来的行集合（模组行、内容树节点、词条行），
/// 唯一持有者就是页面视图模型本身；服务层看不到它们，App 层也不该伸手进 VM 的私有字段。
/// 这些集合的共同点是<b>可重建</b>——下次 <c>OnActivated</c> 会重新读快照或重载词库，
/// 所以释放时只丢数据、不动用户输入（勾选、搜索词、列宽）。
/// <b>但正在显示的页面必须原样返回 0</b>：丢完不会再有 `OnActivated`，
/// 用户看到的是一张空表加一句「载入完成」（启动游戏前的 L1 释放撞过这个坑）。
/// </remarks>
public interface IPageProjectionTrim
{
    /// <summary>页面名，仅用于日志与释放报告，不参与逻辑判断。</summary>
    string PageName { get; }

    /// <summary>丢掉本页的行投影，返回释放掉的条目数（<c>0</c> 表示本来就没有可丢的）。</summary>
    int TrimProjection();
}
