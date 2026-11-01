namespace CkanUI.App.Animations;

/// <summary>换页入场的起点姿态：位移、缩放、不透明度。</summary>
/// <param name="OffsetX">水平起点位移（像素）；正值表示页签向右切，内容从右侧补进来。</param>
/// <param name="OffsetY">垂直起点位移（像素，向下为正）。</param>
/// <param name="StartScale">起点缩放。</param>
/// <param name="StartOpacity">起点不透明度。</param>
public readonly record struct PageRevealPose(double OffsetX, double OffsetY, double StartScale, double StartOpacity);

/// <summary>
/// 换页入场姿态：把「页签切到哪边」翻译成起点位移的方向与主轴。
/// </summary>
/// <remarks>
/// <para>只做上浮的入场在宽屏上读不出"换页方向"，观感像内容自己闪了一下。这里按页签索引差分出两条姿态：
/// 同组页签横切时以水平位移为主（28 px），一级导航换页时以垂直上浮为主（22 px）。
/// 位移主轴跟着控件方向走，用户才读得出"往右切了一个页签 / 往下换了一层"。</para>
/// <para>横切时垂直分量只剩 10 px、上浮时没有水平分量：两个方向同时给满会像"斜着甩进来"，反而糊。
/// 页面宿主不裁剪，水平 28 px 以内低不透明度的边缘不会压到侧栏。</para>
/// </remarks>
public static class PageRevealSpec
{
    /// <summary>页签横切时的水平起点（完整档，像素）。</summary>
    public const double DirectionalHorizontalPixels = 28d;

    /// <summary>页签横切时保留的垂直分量（完整档，像素）。</summary>
    public const double DirectionalVerticalPixels = 10d;

    /// <summary>一级导航换页（无方向）时的上浮起点（完整档，像素）。</summary>
    public const double LiftOnlyPixels = 22d;

    /// <summary>起点缩放（完整档）。</summary>
    public const double StartScale = 0.986d;

    /// <summary>页签横切时的起点不透明度（完整档）。</summary>
    public const double DirectionalStartOpacity = 0.30d;

    /// <summary>一级导航换页时的起点不透明度（完整档）。</summary>
    public const double LiftOnlyStartOpacity = 0.34d;

    /// <summary>按页签切换方向（1 向右 / -1 向左 / 0 同组首次或一级导航）给出起点姿态。</summary>
    public static PageRevealPose For(int direction)
    {
        if (MotionBudget.IsCollapsed)
        {
            return new PageRevealPose(0d, 0d, 1d, 1d);
        }

        var sign = direction > 0 ? 1d : direction < 0 ? -1d : 0d;
        var horizontal = sign == 0d ? 0d : MotionBudget.Amplitude(DirectionalHorizontalPixels);
        var vertical = MotionBudget.Amplitude(sign == 0d ? LiftOnlyPixels : DirectionalVerticalPixels);
        var opacity = MotionBudget.Scaled(sign == 0d ? LiftOnlyStartOpacity : DirectionalStartOpacity, collapse: 1d);

        return new PageRevealPose(
            OffsetX: sign * horizontal,
            OffsetY: vertical,
            StartScale: MotionBudget.Scaled(StartScale),
            StartOpacity: opacity);
    }
}
