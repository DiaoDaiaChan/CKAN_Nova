namespace CkanUI.App.Animations;

/// <summary>图标悬浮后的形态：进场怎么起、悬停循环怎么动、离开怎么落位，整档一起决定。</summary>
/// <remarks>
/// <para>形态不是"配几种角度"，而是各挑自己的通道与支点：齿轮真的转，空态大图标真的离地呼吸，
/// 工具条按钮真的被推一把。全部只动 RenderTransform，不碰布局。</para>
/// <para>认形态的优先级：显式 <see cref="IconHoverMotion.ShapeProperty"/> &gt; 字形语义（<see cref="IconHoverGlyph"/>）&gt;
/// 宿主判定（<see cref="IconHoverRole"/>）&gt; <see cref="Waggle"/>。页面不用逐图标标注：
/// 同一颗齿轮放到哪都转，同一块底板里的不同图形也各动各的。</para>
/// </remarks>
public enum IconHoverShape
{
    /// <summary>没指定：交给 <see cref="IconHoverRole"/> 按宿主判定。</summary>
    Auto = 0,

    /// <summary>轻微摇摆：通用图标的默认形态。</summary>
    Waggle = 1,

    /// <summary>持续旋转：齿轮、时钟、同步这类"转起来才像在工作"的图标。</summary>
    Spin = 2,

    /// <summary>钟摆：绕上沿摆动，像挂在钉子上的小牌子——侧栏与导航项用它。</summary>
    Sway = 3,

    /// <summary>上浮呼吸：离地一点再轻轻起伏，用于空态 / 引导卡片 / 指标卡的大图标。</summary>
    Float = 4,

    /// <summary>顺向轻推：沿水平轴一顶再回弹，提示"这块能点"——工具条上的纯图标按钮。</summary>
    Nudge = 5,

    /// <summary>双拍心跳：只缩放不旋转，用于徽标、模组矩阵这类"在提醒你"的图标。</summary>
    Pulse = 6,

    /// <summary>瞬时挤压：进场挤一下立刻回弹，之后不再循环——窗口控件、拼图咬合、存档宝箱沉降。</summary>
    Snap = 7,

    /// <summary>掀开翻阅：支点置底，纵向向上展开拉伸与翻动呼吸，专为文件夹（实例）定制，零倾斜。</summary>
    Unfold = 8,

    /// <summary>机架抽屉：水平滑出如硬盘仓抽出，叠加快节奏读写微脉冲，专为数据（三条杠）定制，零旋转。</summary>
    Rack = 9,

    /// <summary>双向穿梭：沿水平轴左右往返对流穿梭，专为双向箭头（整合包/交换）定制，零旋转。</summary>
    Shuttle = 10,

    /// <summary>雀跃轻跳：向上轻盈小跳伴随对流微弹，专为语种翻译（文A）定制，零死板倾斜。</summary>
    Bounce = 11,
}
