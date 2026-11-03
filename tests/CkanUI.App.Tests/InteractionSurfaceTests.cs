using Avalonia.Controls;
using Avalonia.Controls.Primitives;

using CkanUI.App.Controls;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 响应表面分类器的矩阵约束：光环、按下收缩、悬停抬升、指针倾斜都读这一份判定，
/// 任何一类表面被判成两种结果，用户看到的就是"有的控件有反应、有的没有"。
/// </summary>
[TestFixture]
internal sealed class InteractionSurfaceTests
{
    [Test]
    public void KindOf_ClassifiesEveryResponsiveFamily()
    {
        Assert.That(InteractionSurface.KindOf(null), Is.EqualTo(InteractionSurfaceKind.None));
        Assert.That(InteractionSurface.KindOf(new StackPanel()), Is.EqualTo(InteractionSurfaceKind.None), "普通容器不响应");
        Assert.That(InteractionSurface.KindOf(new TextBlock()), Is.EqualTo(InteractionSurfaceKind.None), "文本不响应");
        Assert.That(InteractionSurface.KindOf(new Button()), Is.EqualTo(InteractionSurfaceKind.Control));
        Assert.That(InteractionSurface.KindOf(new ToggleButton()), Is.EqualTo(InteractionSurfaceKind.Control));
        Assert.That(InteractionSurface.KindOf(new CheckBox()), Is.EqualTo(InteractionSurfaceKind.Control), "CheckBox 派生自 ToggleButton");
        Assert.That(InteractionSurface.KindOf(new ListBoxItem()), Is.EqualTo(InteractionSurfaceKind.Row));
        Assert.That(InteractionSurface.KindOf(new TreeViewItem()), Is.EqualTo(InteractionSurfaceKind.Row));
        Assert.That(InteractionSurface.KindOf(new DataGridRow()), Is.EqualTo(InteractionSurfaceKind.Row));
        Assert.That(InteractionSurface.KindOf(new MenuItem()), Is.EqualTo(InteractionSurfaceKind.MenuOrTab));
        Assert.That(InteractionSurface.KindOf(new TabItem()), Is.EqualTo(InteractionSurfaceKind.MenuOrTab));
    }

    [Test]
    public void IsCard_CoversEveryCardContainerClassButNotInnerPanels()
    {
        foreach (var className in InteractionSurface.CardClasses)
        {
            Assert.That(InteractionSurface.KindOf(new Border { Classes = { className } }),
                Is.EqualTo(InteractionSurfaceKind.Card), $"{className} 是卡片容器");
        }

        Assert.That(InteractionSurface.KindOf(new Border { Classes = { "CkanSubtleCard" } }),
            Is.EqualTo(InteractionSurfaceKind.None), "卡片内小块由外层卡片负责反应");
        Assert.That(InteractionSurface.KindOf(new Border { Classes = { "CkanStatCard" } }),
            Is.EqualTo(InteractionSurfaceKind.None), "指标块不单独作为表面");

        var panel = new Border { Classes = { "CkanCard" } };
        Assert.That(InteractionSurface.IsCard(panel), Is.True);
        Assert.That(InteractionSurface.IsCard(new Button { Classes = { "CkanCard" } }), Is.False, "卡片类名只对 Border 生效");
    }

    [Test]
    public void FeedbackMatrix_KeepsAuraDipAndLiftInAgreement()
    {
        // 只给光环、不给收缩的（行 / 菜单 / 页签）与既给光环又收缩的（按钮 / 卡片）必须能区分；
        // 给收缩的必须给光环 —— 否则会出现"按下去会缩，但悬停没反应"的割裂。
        foreach (var kind in new[]
                 {
                     InteractionSurfaceKind.None,
                     InteractionSurfaceKind.Control,
                     InteractionSurfaceKind.Card,
                     InteractionSurfaceKind.Row,
                     InteractionSurfaceKind.MenuOrTab,
                 })
        {
            if (InteractionSurface.DipsOnPress(kind))
            {
                Assert.That(InteractionSurface.ShowsAura(kind), Is.True, $"{kind} 能收缩就必须给光环");
            }

            Assert.That(InteractionSurface.DipInset(kind) > 0, Is.True, $"{kind} 的收缩量必须为正");
        }

        Assert.That(InteractionSurface.DipsOnPress(InteractionSurfaceKind.None), Is.False);
        Assert.That(InteractionSurface.DipsOnPress(InteractionSurfaceKind.Row), Is.False, "整行按下不缩，否则像布局抖动");
        Assert.That(InteractionSurface.DipsOnPress(InteractionSurfaceKind.MenuOrTab), Is.False, "页签缩一下会把相邻项错开");
        Assert.That(InteractionSurface.DipInset(InteractionSurfaceKind.Card), Is.EqualTo(InteractionSurface.CardDipInsetPixels));
        Assert.That(InteractionSurface.DipInset(InteractionSurfaceKind.Control), Is.EqualTo(InteractionSurface.ControlDipInsetPixels));
        Assert.That(InteractionSurface.CardClasses, Does.Contain("CkanGuideCard"), "引导卡片曾因不在白名单里而只剩光标变化");
    }

    [Test]
    public void Find_FromInnerContent_WalksUpToTheOutermostSurface()
    {
        var label = new TextBlock { Text = "尚未加载模组索引" };
        var card = new Border
        {
            Classes = { "CkanEmptyStateCard" },
            Child = new StackPanel { Children = { label } },
        };
        var panel = new StackPanel { Children = { card } };
        var window = HeadlessAppHost.CreateTestWindow(panel, 320, 240);

        try
        {
            HeadlessAppHost.PumpJobs();

            Assert.That(InteractionSurface.Find(label, window), Is.SameAs(card), "点到卡内文字也要命中整卡");
            Assert.That(InteractionSurface.Find(card, window), Is.SameAs(card));
            Assert.That(InteractionSurface.Find(panel, window), Is.Null, "容器本身没有表面");
        }
        finally
        {
            window.Close();
        }
    }
}
