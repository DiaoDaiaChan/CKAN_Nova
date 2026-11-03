using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Media;

using CkanUI.App.Animations;
using CkanUI.App.Converters;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 图标按字形认形态：齿轮转、服务器闪、文件夹被推、文档摆、警告挤，
/// 以及"字形语义压过宿主角色"的优先级。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class IconHoverGlyphTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void EverySemanticGlyph_GetsItsOwnMotion()
    {
        var expected = new Dictionary<string, IconHoverShape>(StringComparer.OrdinalIgnoreCase)
        {
            ["Settings"] = IconHoverShape.Spin,
            ["Storage"] = IconHoverShape.Pulse,
            ["Folder"] = IconHoverShape.Nudge,
            ["Document"] = IconHoverShape.Sway,
            ["Bug"] = IconHoverShape.Snap,
        };

        var resolved = new List<IconHoverShape>();
        foreach (var (glyph, shape) in expected)
        {
            var icon = GlyphIcon(glyph);
            var window = Show(icon);

            try
            {
                HeadlessAppHost.PumpJobs();
                var actual = IconHoverMotion.ResolveShape(icon);
                Assert.That(actual, Is.EqualTo(shape), $"字形 {glyph} 应该有自己的形态");
                resolved.Add(actual);
            }
            finally
            {
                window.Close();
                HeadlessAppHost.PumpJobs();
            }
        }

        Assert.That(resolved.Distinct().Count(), Is.EqualTo(expected.Count), "不同语义的图标不能共用一个动作");
    }

    [Test]
    public void InlinePageGlyph_MatchesByNamePathNotBinding()
    {
        // 与 HomePageView.axaml 里 "打开游戏文件夹" 按钮的 HomeIconFolder 同一段路径：
        // 页面图标不走字形绑定，也要靠几何指纹认出来。
        var icon = new PathIcon
        {
            Data = Geometry.Parse("M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"),
            Width = 16,
            Height = 16,
        };
        var window = Show(icon);

        try
        {
            HeadlessAppHost.PumpJobs();
            Assert.That(IconHoverMotion.ResolveShape(icon), Is.EqualTo(IconHoverShape.Nudge), "页面内联的文件夹图标也要被推开");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void GlyphSemantic_BeatsHostRole()
    {
        var tile = new Border { Width = 40, Height = 40 };
        tile.Classes.Add(IconHoverRole.IconTileClass);
        var icon = GlyphIcon("Settings");
        tile.Child = icon;
        var window = Show(tile);

        try
        {
            HeadlessAppHost.PumpJobs();

            Assert.That(IconHoverMotion.ResolveShape(icon), Is.EqualTo(IconHoverShape.Spin), "齿轮放在底板里也得转，不能被底板的呼吸吃掉");
            Assert.That(IconHoverShape.Float, Is.EqualTo(IconHoverRole.Resolve(icon)), "底板角色本身仍是呼吸，证明是字形赢了");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void UnknownGlyph_StillFallsBackToHostRole()
    {
        var tile = new Border { Width = 40, Height = 40 };
        tile.Classes.Add(IconHoverRole.IconTileClass);
        var icon = new PathIcon { Data = Geometry.Parse("M2 2 L14 2 L14 14 L2 14 Z"), Width = 16, Height = 16 };
        tile.Child = icon;
        var window = Show(tile);

        try
        {
            HeadlessAppHost.PumpJobs();

            Assert.That(IconHoverMotion.ResolveShape(icon), Is.EqualTo(IconHoverShape.Float), "认不出的字形回到宿主判定");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static PathIcon GlyphIcon(string glyph)
    {
        var geometry = NavGlyphToGeometryConverter.Instance.Convert(glyph, typeof(Geometry), null, CultureInfo.InvariantCulture) as Geometry;
        Assert.That(geometry, Is.Not.Null, $"字形表里应该有 {glyph}");
        return new PathIcon { Data = geometry, Width = 16, Height = 16 };
    }

    private static Window Show(Control content) => HeadlessAppHost.CreateTestWindow(content, 200, 120);
}
