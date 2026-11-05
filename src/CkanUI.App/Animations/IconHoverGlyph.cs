using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

using CkanUI.App.Converters;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;

namespace CkanUI.App.Animations;

/// <summary>按字形本身认动效形态：齿轮自转、数据机架寻道、文件夹翻开、箭头穿梭。</summary>
/// <remarks>
/// <para>宿主只说"这是什么控件"，说不清"画的是什么"：同一个工具条按钮里，齿轮该转起来，
/// 文件夹该被掀开，警告三角该脆一下。这一层补的就是字形语义，让不同图标不再共用一套公式化倾斜与旋转。</para>
/// <para>字形身份优先取导航项绑定的语义（如 <see cref="NavItem.Glyph"/>），若未指定则通过
/// 把填充区域在自身包围盒上取 12×12 网格采样成 144 位指纹，和导航字形表里的已知图形比对；采样每颗图标只做一次并按几何对象缓存，
/// 认不出就返回 <see cref="IconHoverShape.Auto"/>，交回宿主判定。</para>
/// </remarks>
internal static class IconHoverGlyph
{
    /// <summary>指纹网格边长：12×12=144 位，足够分开这套 Fluent 字形，又不至于每次悬停都算一遍。</summary>
    private const int GridSize = 12;

    /// <summary>字形语义 → 形态。命名跟导航字形的 Glyph 走，页面里的同一图形也能对上。</summary>
    private static readonly Dictionary<string, IconHoverShape> ShapesByGlyph = new(StringComparer.OrdinalIgnoreCase)
    {
        // 机械与循环自转：仅保留真正具备旋转物理语义的齿轮与同步箭头
        ["Settings"] = IconHoverShape.Spin,
        ["Sync"] = IconHoverShape.Spin,

        // 数据与机架：三条杠服务器机架抽屉滑出寻道（彻底告别旋转）
        ["Storage"] = IconHoverShape.Rack,

        // 实例与文件夹：掀开翻阅展开（彻底告别公式化倾斜）
        ["Folder"] = IconHoverShape.Unfold,
        ["FolderOpen"] = IconHoverShape.Unfold,

        // 整合包与交换：左右双向箭头对流穿梭（彻底告别旋转）
        ["Exchange"] = IconHoverShape.Shuttle,

        // 模组汉化：语种字符雀跃轻跳对流（告别倾斜）
        ["Translate"] = IconHoverShape.Bounce,

        // 端庄悬浮呼吸：房屋落地页、太空星球、说明信息、文档纸张
        ["Home"] = IconHoverShape.Float,
        ["Globe"] = IconHoverShape.Float,
        ["Info"] = IconHoverShape.Float,
        ["Document"] = IconHoverShape.Float,
        ["Sliders"] = IconHoverShape.Float,

        // 积木与指标：模组矩阵微弹、图表心跳脉冲、去重链环扣合
        ["Apps"] = IconHoverShape.Pulse,
        ["Chart"] = IconHoverShape.Pulse,
        ["Performance"] = IconHoverShape.Pulse,
        ["Link"] = IconHoverShape.Pulse,
        ["Clock"] = IconHoverShape.Pulse,

        // 稳健与扣锁：存档沉降压印、拼图卡位咬合、日志排障锁定
        ["Archive"] = IconHoverShape.Snap,
        ["Puzzle"] = IconHoverShape.Snap,
        ["Bug"] = IconHoverShape.Snap,
        ["Edit"] = IconHoverShape.Snap,

        // 历史钟摆微探
        ["History"] = IconHoverShape.Sway,

        // AI 助手：星芒脉冲呼吸（走亮度节奏不做位移，免得跟输入焦点抢视觉）
        ["Sparkle"] = IconHoverShape.Pulse,
    };

    /// <summary>通过字形名称直接读取形态。</summary>
    public static IconHoverShape GetShapeForGlyph(string? glyph)
    {
        if (string.IsNullOrWhiteSpace(glyph))
        {
            return IconHoverShape.Auto;
        }

        return ShapesByGlyph.TryGetValue(glyph, out var shape) ? shape : IconHoverShape.Auto;
    }

    /// <summary>几何对象 → 形态，按引用缓存：同一颗图标重复悬停不重算指纹。</summary>
    private static readonly Dictionary<Geometry, IconHoverShape> Cache = new(ReferenceEqualityComparer.Instance);

    private static readonly object Gate = new();

    private static Dictionary<(ulong Low, ulong High), IconHoverShape>? _bySignature;

    /// <summary>认这颗图标的字形语义；认不出返回 <see cref="IconHoverShape.Auto"/>。</summary>
    /// <param name="visual">图标可视对象。</param>
    internal static IconHoverShape Resolve(Visual visual)
    {
        if (FindNavGlyph(visual) is { } glyphName && ShapesByGlyph.TryGetValue(glyphName, out var shapeByNav))
        {
            return shapeByNav;
        }

        var data = GetData(visual);
        if (data is null)
        {
            return IconHoverShape.Auto;
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(data, out var cached))
            {
                return cached;
            }
        }

        var shape = Match(data);

        lock (Gate)
        {
            Cache[data] = shape;
        }

        return shape;
    }

    private static Geometry? GetData(Visual visual) => visual switch
    {
        PathIcon pathIcon => pathIcon.Data,
        FluentAvalonia.UI.Controls.FAPathIcon faPathIcon => faPathIcon.Data,
        _ => null,
    };

    private static string? FindNavGlyph(Visual visual)
    {
        Visual? current = visual;
        var depth = 0;
        while (current is not null && depth < 16)
        {
            if (current is Control { Tag: NavItem nav } && !string.IsNullOrWhiteSpace(nav.Glyph))
            {
                return nav.Glyph;
            }
            if (current is Control { DataContext: NavItem navDc } && !string.IsNullOrWhiteSpace(navDc.Glyph))
            {
                return navDc.Glyph;
            }
            current = current.GetVisualParent();
            depth++;
        }
        return null;
    }

    private static IconHoverShape Match(Geometry data)
    {
        var signature = SignatureOf(data);
        if (signature == default)
        {
            return IconHoverShape.Auto;
        }

        var table = Table();
        return table.TryGetValue(signature, out var shape) ? shape : IconHoverShape.Auto;
    }

    /// <summary>把导航字形表编成"指纹 → 形态"，只建一次；渲染平台缺席时（设计器）退回空表。</summary>
    private static Dictionary<(ulong Low, ulong High), IconHoverShape> Table()
    {
        lock (Gate)
        {
            if (_bySignature is not null)
            {
                return _bySignature;
            }

            var table = new Dictionary<(ulong Low, ulong High), IconHoverShape>();
            foreach (var (glyph, path) in NavGlyphToGeometryConverter.GlyphPaths)
            {
                if (!ShapesByGlyph.TryGetValue(glyph, out var shape))
                {
                    continue;
                }

                try
                {
                    var signature = SignatureOf(Geometry.Parse(path));
                    if (signature != default)
                    {
                        table.TryAdd(signature, shape);
                    }
                }
                catch (Exception)
                {
                    // 没有渲染平台（设计器 / 单测裸跑）时解析不出几何：认不出就交给宿主判定，不抛给悬停链路。
                }
            }

            _bySignature = table;
            return table;
        }
    }

    /// <summary>包围盒内 12×12 网格采样填充区域，压成两个 64 位字。</summary>
    private static (ulong Low, ulong High) SignatureOf(Geometry geometry)
    {
        var bounds = geometry.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return default;
        }

        var low = 0UL;
        var high = 0UL;
        var index = 0;

        for (var row = 0; row < GridSize; row++)
        {
            var y = bounds.Y + (row + 0.5) * bounds.Height / GridSize;
            for (var column = 0; column < GridSize; column++)
            {
                var x = bounds.X + (column + 0.5) * bounds.Width / GridSize;
                if (geometry.FillContains(new Point(x, y)))
                {
                    if (index < 64)
                    {
                        low |= 1UL << index;
                    }
                    else
                    {
                        high |= 1UL << (index - 64);
                    }
                }

                index++;
            }
        }

        return (low, high);
    }
}
