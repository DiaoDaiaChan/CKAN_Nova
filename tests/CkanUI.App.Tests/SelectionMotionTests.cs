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
        Assert.That(item.RenderTransform, Is.InstanceOf<TransformGroup>());

        Tick(4);
        var scale = ScaleOf(item);
        Assert.That(scale, Is.GreaterThan(1.0), $"selected row should pop, scale was {scale}");
    }

    [Test]
    public void KeyboardFocusPopsTheControl()
    {
        var button = new Button { Content = "Open", Width = 90d, Height = 32d };
        var window = HeadlessAppHost.CreateTestWindow(button, 320, 200);
        window.Show();
        HeadlessAppHost.PumpJobs();

        SelectionMotion.Attach(window);

        SelectionMotion.NotifyFocused(button);
        HeadlessAppHost.PumpJobs();

        Assert.That(button.RenderTransform, Is.InstanceOf<TransformGroup>(), "focus should hand the button to its spring");
        Tick(4);
        Assert.That(ScaleOf(button), Is.GreaterThan(1.0));
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
