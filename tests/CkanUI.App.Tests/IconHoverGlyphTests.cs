using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;

using CkanUI.App.Animations;
using CkanUI.App.Converters;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;

using FluentAvalonia.UI.Controls;

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
            ["Storage"] = IconHoverShape.Rack,
            ["Folder"] = IconHoverShape.Unfold,
            ["Exchange"] = IconHoverShape.Shuttle,
            ["Translate"] = IconHoverShape.Bounce,
            ["Document"] = IconHoverShape.Float,
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
            Assert.That(IconHoverMotion.ResolveShape(icon), Is.EqualTo(IconHoverShape.Unfold), "页面内联的文件夹图标也要掀开翻阅");
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

    [Test]
    public void StorageIcon_RackMotion_DoesNotSpin_SlidesOut()
    {
        var icon = GlyphIcon("Storage");
        var window = Show(icon);

        try
        {
            HeadlessAppHost.PumpJobs();
            var shape = IconHoverMotion.ResolveShape(icon);
            Assert.That(shape, Is.EqualTo(IconHoverShape.Rack), "数据机架图标必须解析为 Rack 形态");

            var track = IconHoverTrack.For(icon);
            track.Enter(shape);

            // 推进多帧模拟进场与寻道
            for (var i = 0; i < 20; i++)
            {
                track.Advance(1d / 60d);
            }

            Assert.That(track.Angle, Is.EqualTo(0d), "代表数据的三条杠图标绝对不能转起来（角度保持0）");
            Assert.That(track.OffsetX, Is.GreaterThan(1.0), "代表数据的三条杠图标应有水平抽屉滑出寻道位移");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void FolderIcon_UnfoldMotion_DoesNotTilt_ExpandsVertically()
    {
        var icon = GlyphIcon("Folder");
        var window = Show(icon);

        try
        {
            HeadlessAppHost.PumpJobs();
            var shape = IconHoverMotion.ResolveShape(icon);
            Assert.That(shape, Is.EqualTo(IconHoverShape.Unfold), "文件夹图标必须解析为 Unfold 形态");

            var track = IconHoverTrack.For(icon);
            track.Enter(shape);

            for (var i = 0; i < 20; i++)
            {
                track.Advance(1d / 60d);
            }

            Assert.That(track.Angle, Is.EqualTo(0d), "代表实例的文件夹图标绝对不能公式化倾斜（角度保持0）");
            Assert.That(track.Scale, Is.GreaterThan(1.03), "代表实例的文件夹图标应纵向掀开展开");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AllFourSemanticIcons_UnclippedAndStayWithinBounds()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        try
        {
            window.Show();
            HeadlessAppHost.PumpJobs();

            var nav = window.FindControl<FANavigationView>("NavView");
            Assert.That(nav, Is.Not.Null);

            var transNav = new NavItem("modtext", "模组汉化", "Translate");
            var transItem = new FANavigationViewItem { Content = transNav.Title, Tag = transNav };
            var transGeom = NavGlyphToGeometryConverter.Instance.Convert("Translate", typeof(Geometry), null, CultureInfo.InvariantCulture) as Geometry;
            transItem.IconSource = new FAPathIconSource { Data = transGeom };
            IconHoverMotion.SetShape(transItem, IconHoverShape.Bounce);
            nav!.MenuItems.Add(transItem);
            HeadlessAppHost.PumpJobs();

            var targets = new[]
            {
                ("Folder", IconHoverShape.Unfold),
                ("Exchange", IconHoverShape.Shuttle),
                ("Storage", IconHoverShape.Rack),
                ("Translate", IconHoverShape.Bounce),
            };

            foreach (var (glyph, shape) in targets)
            {
                var item = nav.MenuItems.Concat(nav.FooterMenuItems)
                    .OfType<FANavigationViewItem>()
                    .FirstOrDefault(i => i.Tag is NavItem n && n.Glyph == glyph);
                Assert.That(item, Is.Not.Null, $"导航栏中必须存在 {glyph} 对应的项");
                if (item is null) continue;

                var icon = item.GetVisualDescendants()
                    .OfType<Control>()
                    .First(c => c is FAPathIcon || c.GetType().Name.EndsWith("Icon", StringComparison.Ordinal));
                Assert.That(icon, Is.Not.Null, $"{glyph} 导航项内必须生成图标控件");

                var viewbox = item.GetVisualDescendants().OfType<Viewbox>().FirstOrDefault();

                var track = IconHoverTrack.For(icon);
                track.Enter(shape);

                Assert.That(icon.ClipToBounds, Is.False, $"{glyph} 图标控件本身绝不可裁剪（ClipToBounds 必须为 False）");
                if (viewbox != null)
                {
                    Assert.That(viewbox.ClipToBounds, Is.False, $"{glyph} 图标外层 Viewbox 容器绝不可截断（ClipToBounds 必须为 False）");
                }

                for (var f = 0; f < 25; f++)
                {
                    track.Advance(1d / 60d);
                }
                HeadlessAppHost.PumpJobs();

                try
                {
                    var frame = window.CaptureRenderedFrame();
                    if (frame != null)
                    {
                        var outDir = @"C:\Users\43701\.gemini\antigravity\brain\21632ece-ea84-455b-b489-6a9667115d29";
                        var outPath = System.IO.Path.Combine(outDir, $"nav_{glyph}_optimized.png");
#pragma warning disable CS0618
                        frame.Save(outPath);
#pragma warning restore CS0618
                    }
                }
                catch
                {
                }

                // 验证进场位姿保持在优雅的微交互安全范围内，绝不上溢或左右撞墙
                Assert.That(Math.Abs(track.OffsetX), Is.LessThanOrEqualTo(2.0), $"{glyph} 进场的水平位移必须收敛在 2px 以内的安全画框");
                Assert.That(Math.Abs(track.OffsetY), Is.LessThanOrEqualTo(2.0), $"{glyph} 进场的竖直位移必须收敛在 2px 以内的安全画框");
                Assert.That(track.Scale, Is.LessThanOrEqualTo(1.10), $"{glyph} 进场的形变缩放必须保持在 1.10 以内，绝不膨胀出画框");

                // 继续推进到持续循环阶段（60帧 = 1.0秒）
                for (var f = 0; f < 35; f++)
                {
                    track.Advance(1d / 60d);
                }
                HeadlessAppHost.PumpJobs();

                // 验证持续阶段同样严格收敛在安全包络内，且非旋转类绝不转圈
                Assert.That(Math.Abs(track.OffsetX), Is.LessThanOrEqualTo(2.0), $"{glyph} 循环中的水平位移必须收敛在 2px 以内");
                Assert.That(Math.Abs(track.OffsetY), Is.LessThanOrEqualTo(2.0), $"{glyph} 循环中的竖直位移必须收敛在 2px 以内");
                Assert.That(track.Scale, Is.LessThanOrEqualTo(1.10), $"{glyph} 循环中的缩放必须保持在 1.10 以内");
                if (shape != IconHoverShape.Spin)
                {
                    Assert.That(Math.Abs(track.Angle), Is.LessThanOrEqualTo(5.0), $"{glyph} 绝不能大角度倾斜或自转");
                }

                track.Exit();
                for (var f = 0; f < 30; f++)
                {
                    track.Advance(1d / 60d);
                }
                HeadlessAppHost.PumpJobs();
            }
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void FAPathIcon_InNavigationItem_ResolvesSemanticShape()
    {
        var nav = new NavItem("instances", "实例", "Folder");
        var item = new FANavigationViewItem
        {
            Content = nav.Title,
            Tag = nav,
        };
        var geom = NavGlyphToGeometryConverter.Instance.Convert("Folder", typeof(Geometry), null, CultureInfo.InvariantCulture) as Geometry;
        item.IconSource = new FAPathIconSource { Data = geom };

        var window = Show(item);
        try
        {
            HeadlessAppHost.PumpJobs();
            var icon = item.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(c => c is FAPathIcon || c.GetType().Name.EndsWith("Icon", StringComparison.Ordinal));
            Assert.That(icon, Is.Not.Null, "导航项内应生成图标控件");

            var resolved = IconHoverMotion.ResolveShape(icon!);
            Assert.That(resolved, Is.EqualTo(IconHoverShape.Unfold), "侧栏导航项内的文件夹图标应解析为 Unfold，而不是公式化 Sway");
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
