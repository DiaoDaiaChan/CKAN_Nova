namespace CkanUI.Services.Abstractions;

/// <summary>
/// 页面愿意在「预热」时先把缩略图烘进缓存（宿主在你还没点进来之前就把这一页实体化）。
/// </summary>
/// <remarks>
/// <para>为什么要有这个接口：预热是宿主外壳的能力（MainWindow 的页面预热队列），
/// 而旗帜 / 飞船这类页面在绑定时同步解码缩略图——等用户点进来才解就是几百毫秒的卡顿。
/// 页面搬进插件程序集以后，宿主不能再对具体页面类型做 <c>is</c> 判断，只能问接口。</para>
/// <para>实现方自己决定宽度与行集合来源（宽度是页面自己的排版决定，宿主猜不出来）；
/// 行集合通常异步填，实现要处理"现在还没有行"的情况——宿主只调用一次，不会替你看集合变化。</para>
/// </remarks>
public interface IPageThumbnailWarmup
{
    /// <summary>把本页缩略图预热进缓存。可在任意线程调用，必须自己保证不阻塞 UI 超过一帧。</summary>
    void WarmUpThumbnails();
}
