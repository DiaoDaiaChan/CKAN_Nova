using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CkanUI.Ui.Icons;
using NUnit.Framework;

namespace CkanUI.Ui.Tests.Icons;

/// <summary>
/// 字形库的行为锁：页内图标猜错比空着更糟，所以「名字拼错」必须解析成 <c>null</c> 而不是随便给个形状。
/// </summary>
[TestFixture]
public sealed class GlyphCatalogTests
{
    /// <summary>两个插件视图里实际绑定的字形名（改了视图就得改这张表，否则图标悄悄消失）。</summary>
    private static readonly string[] GlyphsInUse =
    [
        // 性能页：五个指标 + 耗时分解 + 曲线卡。
        "Speed", "Timer", "Percent", "Pulse", "Chart", "Cpu", "Program", "Memory", "Gpu", "Curve",
        // RP-1 页：八块统计卡 + 曲线指标切换。
        "Contract", "Milestone", "Crew", "Money", "Science", "Confidence", "Reputation",
        // KSP 配置页：六个分类页签。
        "Layers", "Monitor", "Volume", "Gamepad", "Wrench", "More",
        // 通用。
        "Calendar", "Clock", "Warning", "Check", "Bug", "Folder", "File",
    ];

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // 路径解析要的是「渲染平台起来了」（AvaloniaLocator 里有几何实现），不需要窗口亲和性；
        // 全量套件里无头壳常常由别的测试程序集在另一个线程建好，这条线上一概用解析探针判断可用性。
        HeadlessHost.EnsureInitialized();
        try
        {
            Geometry.Parse(MinimalPath);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"本线程解析不了路径数据（{ex.GetType().Name}: {ex.Message}）；单跑 CkanUI.Ui.Tests 可验证本夹具。");
        }
    }

    private const string MinimalPath = "M0 0L1 1";

    [Test]
    public void EveryCatalogName_ResolvesToGeometry()
    {
        Assert.That(GlyphCatalog.Count, Is.GreaterThan(0));

        var broken = new List<string>();
        foreach (var name in GlyphCatalog.Names)
        {
            Geometry? geometry;
            try
            {
                geometry = GlyphCatalog.Resolve(name);
            }
            catch (Exception ex)
            {
                broken.Add($"{name}: 解析抛异常 {ex.GetType().Name} {ex.Message}");
                continue;
            }

            if (geometry is null)
            {
                broken.Add($"{name}: 解析成 null");
                continue;
            }

            var bounds = geometry.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                broken.Add($"{name}: 包围盒为空（{bounds.Width}×{bounds.Height}），画出来看不见");
            }
        }

        Assert.That(broken, Is.Empty, "有字形解析不出可绘制路径：" + string.Join("；", broken));
    }

    [Test]
    public void GlyphsUsedByViews_Exist()
    {
        var missing = GlyphsInUse.Where(name => GlyphCatalog.Resolve(name) is null).ToArray();

        Assert.That(missing, Is.Empty, "视图里绑定的字形名在字形库里不存在（图标会静默消失）：" + string.Join(", ", missing));
    }

    [Test]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.That(GlyphCatalog.Resolve("speed"), Is.Not.Null);
        Assert.That(GlyphCatalog.Resolve("  SPEED  "), Is.Not.Null, "名字前后空白也要能认出来（绑定值来自字符串常量）");
    }

    [Test]
    public void Resolve_UnknownName_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GlyphCatalog.Resolve("DefinitelyNotAGlyph"), Is.Null, "未知字形不画，不退回占位符");
            Assert.That(GlyphCatalog.Resolve(string.Empty), Is.Null);
            Assert.That(GlyphCatalog.Resolve(null), Is.Null);
            Assert.That(GlyphCatalog.Resolve("   "), Is.Null);
        });
    }

    [Test]
    public void Resolve_CachesGeometryInstances()
    {
        var first = GlyphCatalog.Resolve("Speed");
        var second = GlyphCatalog.Resolve("Speed");

        Assert.That(ReferenceEquals(first, second), Is.True, "字形每次解析都重新 Par（解析）一次，会把渲染路径变成 GC 源");
    }

    [Test]
    public void Names_HaveNoDuplicatesAndMatchCount()
    {
        var names = GlyphCatalog.Names.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(names.Length));
            Assert.That(names.Length, Is.EqualTo(GlyphCatalog.Count));
        });
    }

    [Test]
    public void Geometry_StaysInsideTheTwentyFourUnitBox()
    {
        // GlyphIcon 以 24×24 视图框缩放；路径越界会被裁掉一角。
        var offenders = new List<string>();
        foreach (var name in GlyphCatalog.Names)
        {
            var bounds = GlyphCatalog.Resolve(name)!.Bounds;
            if (bounds.X < -0.5 || bounds.Y < -0.5 || bounds.Right > 24.5 || bounds.Bottom > 24.5)
            {
                offenders.Add($"{name}: {bounds.X:0.#},{bounds.Y:0.#} {bounds.Width:0.#}×{bounds.Height:0.#}");
            }
        }

        Assert.That(offenders, Is.Empty, "这些字形的路径超出了 24×24 视图框：" + string.Join("；", offenders));
    }
}
