using System;
using System.Linq;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>卡片悬停抬升：真的走帧、反向时连续、三档幅度正确。</summary>
[TestFixture]
[NonParallelizable]
public sealed class HoverLiftMotionTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(200, 90);
    }

    [TearDown]
    public void TearDown() => SetTokens(200, 90);

    [Test]
    public void HoverLiftsTheCardAndLeavingPutsItBack()
    {
        var card = CreateCard();
        HeadlessAppHost.CreateTestWindow(card);

        HoverLiftMotion.Apply(card, hovered: true);
        Tick(6);
        Assert.That(TranslateY(card), Is.LessThan(-0.5), "悬停要真的抬起来");

        Tick(45);
        Assert.That(TranslateY(card), Is.EqualTo(-HoverLiftMotion.LiftPixels).Within(0.05));

        HoverLiftMotion.Apply(card, hovered: false);
        Tick(45);
        Assert.That(TranslateY(card), Is.EqualTo(0).Within(0.05));
    }

    [Test]
    public void LeavingMidFlightDoesNotTeleport()
    {
        var card = CreateCard();
        HeadlessAppHost.CreateTestWindow(card);

        HoverLiftMotion.Apply(card, hovered: true);
        Tick(6);
        var before = TranslateY(card);
        Assert.That(before, Is.LessThan(-0.5), "反向测试前必须已经在动");

        HoverLiftMotion.Apply(card, hovered: false);
        Tick(1);
        var after = TranslateY(card);

        Assert.That(Math.Abs(after - before), Is.LessThan(0.4), "反向要带速回位，不能瞬间归零");
        Assert.That(after, Is.LessThan(0), "还没回到原位");
    }

    [Test]
    public void ReducedMotionHalvesTheLift()
    {
        SetTokens(page: 0, fast: 90);

        var card = CreateCard();
        HeadlessAppHost.CreateTestWindow(card);

        HoverLiftMotion.Apply(card, hovered: true);
        Tick(45);

        Assert.That(TranslateY(card), Is.EqualTo(-HoverLiftMotion.LiftPixels * MotionBudget.ReducedAmplitudeScale).Within(0.05));
    }

    [Test]
    public void CollapsedMotionDoesNotTouchTheCard()
    {
        SetTokens(page: 0, fast: 0);

        var card = CreateCard();
        HeadlessAppHost.CreateTestWindow(card);
        var before = card.RenderTransform;

        HoverLiftMotion.Apply(card, hovered: true);
        Tick(2);

        Assert.That(card.RenderTransform, Is.SameAs(before));
    }

    [Test]
    public void AttachOnlyAdoptsInteractiveCardsAndRunsOnce()
    {
        var panel = new StackPanel
        {
            Children =
            {
                CreateCard(),
                new Border { Height = 20 },
            },
        };
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            Assert.That(HoverLiftMotion.Attach(window), Is.EqualTo(1));
            Assert.That(HoverLiftMotion.Attach(window), Is.EqualTo(0));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ListRowsLiftByHalfTheCardDistance()
    {
        var rows = new StackPanel
        {
            Children =
            {
                new ListBoxItem { Content = "第一行", Height = 32 },
            },
        };
        var window = HeadlessAppHost.CreateTestWindow(rows);

        try
        {
            Assert.That(HoverLiftMotion.Attach(window), Is.EqualTo(1), "列表行也要被接管");

            var row = rows.Children.OfType<ListBoxItem>().Single();
            HoverLiftMotion.Apply(row, hovered: true);
            Tick(45);
            Assert.That(TranslateY(row), Is.EqualTo(-HoverLiftMotion.RowLiftPixels).Within(0.05));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void GuideButtonsLiftLikeCards()
    {
        var button = new Button { Classes = { HoverLiftMotion.GuideButtonClass }, Content = "去设置" };
        HeadlessAppHost.CreateTestWindow(button);

        HoverLiftMotion.Apply(button, hovered: true);
        Tick(45);

        Assert.That(TranslateY(button), Is.EqualTo(-HoverLiftMotion.LiftPixels).Within(0.05));
    }

    [Test]
    public void GridRowsLiftByTheRowDistance()
    {
        var rows = new StackPanel
        {
            Children =
            {
                new DataGridRow { Height = 32 },
            },
        };
        var window = HeadlessAppHost.CreateTestWindow(rows);

        try
        {
            Assert.That(HoverLiftMotion.Attach(rows), Is.EqualTo(1));
            HoverLiftMotion.Apply((DataGridRow)rows.Children[0], hovered: true);
            Tick(45);

            Assert.That(TranslateY((DataGridRow)rows.Children[0]), Is.EqualTo(-HoverLiftMotion.RowLiftPixels).Within(0.05));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PlainButtonsLiftByTheButtonDistance()
    {
        var button = new Button { Content = "刷新", Height = 32 };
        HeadlessAppHost.CreateTestWindow(button);

        HoverLiftMotion.Apply(button, hovered: true);
        Tick(45);

        Assert.That(TranslateY(button), Is.EqualTo(-HoverLiftMotion.ButtonLiftPixels).Within(0.05));
    }

    [Test]
    public void ButtonsInsideALiftingCardAreLeftToTheCard()
    {
        var button = new Button { Content = "打开", Height = 28 };
        var card = new Border
        {
            Classes = { HoverLiftMotion.CardClass },
            Height = 90,
            Child = button,
        };
        var window = HeadlessAppHost.CreateTestWindow(card);

        try
        {
            Assert.That(HoverLiftMotion.Attach(window), Is.EqualTo(1), "只接管外层卡片，卡内按钮不重复抬");
            Assert.That(TranslateY(button), Is.EqualTo(0).Within(0.05));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void IconButtonsAreLeftToTheirIconMotion()
    {
        // Button.CkanIcon:pointerover 把 RenderTransform 设成 none，硬接管会和那条 setter 抢，
        // 结果是按钮不动、图标也不转——图标按钮的反馈交给 IconHoverMotion。
        var button = new Button { Classes = { HoverLiftMotion.IconButtonClass }, Content = "×", Width = 30, Height = 30 };
        var window = HeadlessAppHost.CreateTestWindow(button);

        try
        {
            Assert.That(HoverLiftMotion.Attach(window), Is.EqualTo(0), "图标按钮不进接管名单");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ShellAttachAdoptsChromeButtons()
    {
        // 标题栏/状态条按钮在 PageHost 外面，页面级挂载永远扫不到，得靠整窗挂载补上。
        var panel = new StackPanel
        {
            Children =
            {
                new Button { Name = "AboutCompactButton", Content = "?", Width = 30, Height = 30 },
            },
        };
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            Assert.That(HoverLiftMotion.AttachShell(window), Is.EqualTo(1), "整窗挂载要接管外壳按钮");
            Assert.That(HoverLiftMotion.AttachShell(window), Is.EqualTo(0), "重复挂载不叠加");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void GuideArrowSlidesWithTheCard()
    {
        var arrow = new PathIcon { Classes = { HoverLiftMotion.ArrowClass }, Width = 12, Height = 12 };
        var card = new Border
        {
            Classes = { HoverLiftMotion.GuideCardClass },
            Height = 90,
            Child = new StackPanel { Children = { new TextBlock { Text = "去设置" }, arrow } },
        };
        HeadlessAppHost.CreateTestWindow(card);

        HoverLiftMotion.Apply(card, hovered: true);
        Tick(45);

        Assert.That(TranslateX(arrow), Is.EqualTo(HoverLiftMotion.ArrowTravelPixels).Within(0.05), "箭头要跟卡片一起滑出去");

        HoverLiftMotion.Apply(card, hovered: false);
        Tick(45);
        Assert.That(TranslateX(arrow), Is.EqualTo(0).Within(0.05));
    }

    private static Border CreateCard() => new()
    {
        Classes = { HoverLiftMotion.CardClass },
        Height = 80,
        Background = Brushes.Transparent,
    };

    private static void SetTokens(int page, int fast)
    {
        var resources = Application.Current!.Resources;
        resources[MotionHost.PageKey] = TimeSpan.FromMilliseconds(page);
        resources[MotionHost.FastKey] = TimeSpan.FromMilliseconds(fast);
    }

    private static void Tick(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(16);
        }
    }

    private static double TranslateX(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.X
            : 0d;

    private static double TranslateY(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.Y
            : 0d;
}