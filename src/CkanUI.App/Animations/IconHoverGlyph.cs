using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using CkanUI.App.Converters;

namespace CkanUI.App.Animations;

/// <summary>按字形本身认动效形态：齿轮转、服务器闪灯、文件夹被推一把。</summary>
/// <remarks>
/// <para>宿主只说"这是什么控件"，说不清"画的是什么"：同一个工具条按钮里，齿轮该转起来，
/// 文件夹该被推开，警告三角该脆一下。这一层补的就是字形语义，让不同图标不再共用一套摇摆。</para>
/// <para>字形身份不靠 Data 的文本——Avalonia 的 <see cref="StreamGeometry"/> 既不保留路径字符串，
/// 也不按值比较（同一条路径解析两次 <c>Equals</c> 是 false）。这里改为把填充区域在自身包围盒上
/// 取 12×12 网格采样成 144 位指纹，和导航字形表里的已知图形比对；采样每颗图标只做一次并按几何对象缓存，
/// 认不出就返回 <see cref="IconHoverShape.Auto"/>，交回宿主判定。</para>
/// </remarks>
internal static class IconHoverGlyph
{
    /// <summary>指纹网格边长：12×12=144 位，足够分开这套 Fluent 字形，又不至于每次悬停都算一遍。</summary>
    private const int GridSize = 12;

    /// <summary>字形语义 → 形态。命名跟导航字形的 Glyph 走，页面里的同一图形也能对上。</summary>
    private static readonly Dictionary<string, IconHoverShape> ShapesByGlyph = new(StringComparer.OrdinalIgnoreCase)
    {
        // 转起来才像在工作：齿轮、同步、时钟、地球自转。
        ["Settings"] = IconHoverShape.Spin,
        ["Sync"] = IconHoverShape.Spin,
        ["Clock"] = IconHoverShape.Spin,
        ["Globe"] = IconHoverShape.Spin,

        // 指示灯语义：机架、图表这类"在跳数字"的东西用双拍心跳。
        ["Storage"] = IconHoverShape.Pulse,
        ["Chart"] = IconHoverShape.Pulse,

        // 可被推开／翻转：文件夹、交换、链接、下载。
        ["Folder"] = IconHoverShape.Nudge,
        ["FolderOpen"] = IconHoverShape.Nudge,
        ["Exchange"] = IconHoverShape.Nudge,
        ["Link"] = IconHoverShape.Nudge,

        // 挂着翻看：文档、推子、历史。
        ["Document"] = IconHoverShape.Sway,
        ["Sliders"] = IconHoverShape.Sway,
        ["History"] = IconHoverShape.Sway,

        // 轻飘的说明性图标离地呼吸。
        ["Info"] = IconHoverShape.Float,
        ["Translate"] = IconHoverShape.Float,
        ["Home"] = IconHoverShape.Float,

        // 出了事才挤一下：缺陷、编辑、归档。
        ["Bug"] = IconHoverShape.Snap,
        ["Edit"] = IconHoverShape.Snap,
        ["Archive"] = IconHoverShape.Snap,
    };

    /// <summary>几何对象 → 形态，按引用缓存：同一颗图标重复悬停不重算指纹。</summary>
    private static readonly Dictionary<Geometry, IconHoverShape> Cache = new(ReferenceEqualityComparer.Instance);

    private static readonly object Gate = new();

    private static Dictionary<(ulong Low, ulong High), IconHoverShape>? _bySignature;

    /// <summary>认这颗图标的字形语义；认不出返回 <see cref="IconHoverShape.Auto"/>。</summary>
    /// <param name="visual">图标可视对象。</param>
    internal static IconHoverShape Resolve(Visual visual)
    {
        if (visual is not PathIcon { Data: { } data })
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
