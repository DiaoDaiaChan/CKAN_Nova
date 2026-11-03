using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace CkanUI.App.Controls;

/// <summary>
/// 指针响应表面的种类。光环、按下收缩、悬停抬升、指针倾斜都按这一份判定走，
/// 不再各模块各写一套类型/类名白名单——否则同样的卡片在一处算"面"、另一处不算，
/// 用户看到的就是"有的控件有反应、有的没有"。
/// </summary>
public enum InteractionSurfaceKind
{
    /// <summary>不响应。</summary>
    None,

    /// <summary>按钮族（含 <see cref="ToggleButton"/>、<see cref="CheckBox"/>、<see cref="RadioButton"/>）。</summary>
    Control,

    /// <summary>卡片容器（整块面，按下按 6 px 收）。</summary>
    Card,

    /// <summary>列表/表格/树的行。</summary>
    Row,

    /// <summary>菜单项与页签：有光环，但不缩不抬（缩一下会把相邻项错开）。</summary>
    MenuOrTab,
}

/// <summary>
/// 响应表面判定与反馈矩阵的唯一来源。
/// </summary>
/// <remarks>
/// <para>矩阵：</para>
/// <list type="table">
/// <item><description>按钮族：光环 + 按下 12 px + 悬停 2 px（图标按钮交给 <c>IconHoverMotion</c>，本类不抬）。</description></item>
/// <item><description>卡片容器：光环 + 按下 6 px + 悬停 2.75 px（与按钮同一套手感，只是大面积按比例收得少）。</description></item>
/// <item><description>行：光环 + 悬停 1.35 px，按下不缩（整行缩会像布局抖动）。</description></item>
/// <item><description>菜单项/页签：只给光环。</description></item>
/// </list>
/// <para>卡片识别只认容器托盘；<c>CkanSubtleCard</c> / <c>CkanStatCard</c> 这类卡片内部小块不算面——
/// 它们落在卡片里时由外层卡片负责反应，自己再缩一层会把内层小面板"缩出去"。</para>
/// </remarks>
public static class InteractionSurface
{
    /// <summary>卡片容器的按下单边收缩量（像素）。</summary>
    public const double CardDipInsetPixels = 6d;

    /// <summary>普通控件的按下单边收缩量（像素）。</summary>
    public const double ControlDipInsetPixels = 12d;

    /// <summary>卡片容器的类名。带 <c>CkanCardSelected</c> 的是选中态卡片，同样是一块可点的面。</summary>
    public static readonly string[] CardClasses =
    [
        "CkanCard",
        "CkanCardInteractive",
        "CkanCardSelected",
        "CkanEmptyStateCard",
        "CkanGuideCard",
    ];

    /// <summary>这个元素是不是卡片容器。</summary>
    /// <param name="visual">候选元素。</param>
    /// <returns>是卡片容器则为 <c>true</c>。</returns>
    public static bool IsCard(Visual? visual)
    {
        if (visual is not Border border)
        {
            return false;
        }

        for (var i = 0; i < CardClasses.Length; i++)
        {
            if (border.Classes.Contains(CardClasses[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把单个元素分类；不认识的一律 <see cref="InteractionSurfaceKind.None"/>。</summary>
    /// <param name="visual">候选元素。</param>
    /// <returns>表面种类。</returns>
    public static InteractionSurfaceKind KindOf(Visual? visual)
    {
        if (visual is null)
        {
            return InteractionSurfaceKind.None;
        }

        if (IsCard(visual))
        {
            return InteractionSurfaceKind.Card;
        }

        if (visual is MenuItem or TabItem)
        {
            return InteractionSurfaceKind.MenuOrTab;
        }

        if (visual is TreeViewItem or ListBoxItem or DataGridRow)
        {
            return InteractionSurfaceKind.Row;
        }

        // ToggleButton 覆盖 CheckBox / RadioButton；Button 覆盖 RepeatButton 与各类派生按钮。
        if (visual is Button or ToggleButton)
        {
            return InteractionSurfaceKind.Control;
        }

        return InteractionSurfaceKind.None;
    }

    /// <summary>这类表面要不要给悬停光环。</summary>
    /// <param name="kind">表面种类。</param>
    /// <returns>不是 <see cref="InteractionSurfaceKind.None"/> 就是。</returns>
    public static bool ShowsAura(InteractionSurfaceKind kind) => kind != InteractionSurfaceKind.None;

    /// <summary>这类表面按下要不要收。</summary>
    /// <param name="kind">表面种类。</param>
    /// <returns>按钮族与卡片为 <c>true</c>。</returns>
    public static bool DipsOnPress(InteractionSurfaceKind kind)
        => kind is InteractionSurfaceKind.Control or InteractionSurfaceKind.Card;

    /// <summary>按下收缩的单边像素数。</summary>
    /// <param name="kind">表面种类。</param>
    /// <returns>卡片 6 px，其余 12 px。</returns>
    public static double DipInset(InteractionSurfaceKind kind)
        => kind == InteractionSurfaceKind.Card ? CardDipInsetPixels : ControlDipInsetPixels;

    /// <summary>从命中的视觉元素往上找最近的响应表面。</summary>
    /// <param name="source">指针事件的 Source。</param>
    /// <param name="root">向上查找的边界（通常是窗口）。</param>
    /// <returns>响应元素；没有则返回 <see langword="null"/>。</returns>
    public static Control? Find(Visual? source, Visual? root)
    {
        for (var visual = source; visual is not null && visual != root; visual = visual.GetVisualParent())
        {
            if (KindOf(visual) != InteractionSurfaceKind.None)
            {
                return visual as Control;
            }
        }

        return null;
    }
}
