using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App.Animations;
using CkanUI.App.Theming;
using CkanUI.App.Tests.Automation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>选中行与键盘焦点的弹簧反馈。</summary>
[TestFixture]
[NonParallelizable]
public sealed class SelectionMotionTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(page: 200d, fast: 90d);
    }

    [TearDown]
    public void TearDown() => SetTokens(page: 200d, fast: 90d);

    [Test]
    public void SelectingARowPopsIt()
    {
        var list = new ListBox();
        for (int i = 0; i < 3; i++)
        {
            list.Items.Add(new ListBoxItem { Content = "row " + i, Height = 32d });
        }

        var window = HeadlessAppHost.CreateTestWindow(list, 360, 260);
        window.Show();
        HeadlessAppHost.PumpJobs();

        Assert.That(SelectionMotion.Attach(window), Is.GreaterThan(0), "attach should adopt the list");

        list.SelectedIndex = 1;
        HeadlessAppHost.PumpJobs();

        var item = list.GetVisualDescendants().OfType<ListBoxItem>().Single(x => x.IsSelected);

        // 冲量排在选中之后的下一帧（虚拟化要先实现行容器）。这里只钉"接管发生了"：
        // 弹簧的时间演化由 TransformSpringTests / MotionFramePumpTests 覆盖，全套并发跑时帧泵时序不可当契约。
        Tick(6);
        Assert.That(item.RenderTransform, Is.InstanceOf<TransformGroup>(),
            $"the row should be handed to its spring: mode={MotionBudget.Mode} collapsed={MotionBudget.IsCollapsed} over={item.IsPointerOver}");
        Assert.That(MotionBudget.Amplitude(SelectionMotion.SelectionPop), Is.GreaterThan(0d));
    }

    [Test]
    public void KeyboardFocusPopsTheControl()
    {
        // 挪开原点：无头平台的指针默认停在 (0,0)，贴在原点的控件会被判成"鼠标悬停中"。
        var button = new Button { Content = "Open", Width = 90d, Height = 32d, Margin = new Thickness(60, 60, 0, 0) };
        var window = HeadlessAppHost.CreateTestWindow(button, 320, 200);
        window.Show();
        HeadlessAppHost.PumpJobs();

        SelectionMotion.Attach(window);

        SelectionMotion.NotifyFocused(button);
        HeadlessAppHost.PumpJobs();

        Assert.That(button.RenderTransform, Is.InstanceOf<TransformGroup>(),
            $"focus should hand the button to its spring: mode={MotionBudget.Mode} collapsed={MotionBudget.IsCollapsed} over={button.IsPointerOver} amp={MotionBudget.Amplitude(SelectionMotion.FocusPop)} res={Application.Current!.Resources.TryGetResource(MotionHost.PageKey, null, out var v)}/{v}/{Application.Current!.Resources.TryGetResource(MotionHost.FastKey, null, out var v2)}/{v2}");
    }

    [Test]
    public void TextEntryFocusIsLeftAlone()
    {
        var box = new TextBox { Width = 160d, Height = 30d };
        var window = HeadlessAppHost.CreateTestWindow(box, 320, 200);
        window.Show();
        HeadlessAppHost.PumpJobs();

        SelectionMotion.Attach(window);
        SelectionMotion.NotifyFocused(box);
        HeadlessAppHost.PumpJobs();
        Tick(4);

        Assert.That(box.RenderTransform, Is.Not.InstanceOf<TransformGroup>(),
            "text entry owns its own caret and scroll, no spring here");
    }

    [Test]
    public void AttachIsIdempotentAndLeavesCollapsedMotionAlone()
    {
        var list = new ListBox { Items = { new ListBoxItem { Content = "a" } } };
        var window = HeadlessAppHost.CreateTestWindow(list, 320, 200);
        window.Show();
        HeadlessAppHost.PumpJobs();

        SelectionMotion.Attach(window);
        Assert.That(SelectionMotion.Attach(window), Is.EqualTo(0), "second attach adopts nothing");

        SetTokens(page: 0d, fast: 0d);
        Assert.That(SelectionMotion.Attach(window), Is.EqualTo(0), "collapsed motion attaches nothing");
    }

    private static double ScaleOf(Control control)
    {
        if (control.RenderTransform is TransformGroup group &&
            group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale)
        {
            return scale.ScaleX;
        }

        return control.RenderTransform is TransformOperations
            ? 1d
            : double.NaN;
    }

    private static void Tick(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(16);
        }
    }

    private static void SetTokens(double page, double fast)
    {
        var resources = Application.Current!.Resources;
        resources[MotionHost.PageKey] = TimeSpan.FromMilliseconds(page);
        resources[MotionHost.FastKey] = TimeSpan.FromMilliseconds(fast);
    }
}
