using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

namespace CkanUI.App.Animations;

/// <summary>按图标"放在哪、干什么用"判形态：同一个字形落在不同宿主上，动起来不该一模一样。</summary>
/// <remarks>
/// <para>只看控件类型与约定类名，不看字形的路径数据——路径说明画的是什么，不说明它此刻扮演什么角色。
/// 拿路径做分类会把箭头和齿轮判成同级，改一个图标就得改一遍映射表。</para>
/// <para>就近原则：从图标往上找第一个认得的宿主，先命中先算。显式
/// <see cref="IconHoverMotion.ShapeProperty"/> 压过这里（导航栏的齿轮就是这么指定的）。</para>
/// </remarks>
public static class IconHoverRole
{
    /// <summary>图标底板块的类名（空态、引导卡片、指标卡共用）。</summary>
    public const string IconTileClass = "CkanIconTile";

    /// <summary>纯图标按钮的类名（30×30 无字按钮）。</summary>
    public const string IconButtonClass = "CkanIcon";

    /// <summary>窗口标题栏按钮的类名。</summary>
    public const string CaptionButtonClass = "CkanCaption";

    /// <summary>自定义图标宿主的类名。</summary>
    public const string IconHostClass = "CkanIconHost";

    /// <summary>往上找宿主的层数上限：视觉树异常深时也不至于把一次悬停拖成一次遍历。</summary>
    private const int MaxDepth = 24;

    /// <summary>按宿主判定形态；认不出时给通用摇摆。</summary>
    /// <param name="icon">图标控件。</param>
    public static IconHoverShape Resolve(Visual icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        var current = icon.GetVisualParent();
        var depth = 0;
        while (current is not null && depth < MaxDepth)
        {
            if (current is Control control)
            {
                var shape = ShapeOfHost(control);
                if (shape != IconHoverShape.Auto)
                {
                    return shape;
                }
            }

            current = current.GetVisualParent();
            depth++;
        }

        return IconHoverShape.Waggle;
    }

    /// <summary>单个宿主的形态；认不出返回 <see cref="IconHoverShape.Auto"/>。</summary>
    private static IconHoverShape ShapeOfHost(Control control) => control switch
    {
        Border tile when tile.Classes.Contains(IconTileClass) => IconHoverShape.Float,
        Border host when host.Classes.Contains(IconHostClass) => IconHoverShape.Pulse,
        Button caption when caption.Classes.Contains(CaptionButtonClass) => IconHoverShape.Snap,
        Button action when action.Classes.Contains(IconButtonClass) => IconHoverShape.Nudge,
        ToggleButton => IconHoverShape.Snap,
        FANavigationViewItem => IconHoverShape.Sway,
        _ => IconHoverShape.Auto,
    };
}
