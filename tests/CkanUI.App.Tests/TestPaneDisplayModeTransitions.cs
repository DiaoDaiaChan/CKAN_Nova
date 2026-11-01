using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class TestPaneDisplayModeTransitions
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void TestFineGrainedFrames()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };

        var style = new Style(x => x.OfType<FANavigationView>().Template().OfType<SplitView>().Class(":closed").Template().OfType<Panel>().Name("PART_PaneRoot"));
        style.Setters.Add(new Setter(Animatable.TransitionsProperty, new Transitions
        {
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0) }
        }));
        window.Styles.Add(style);

        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();
            var nav = window.FindControl<FANavigationView>("NavView")!;
            var splitView = nav.GetVisualDescendants().OfType<SplitView>().First();
            var paneRoot = splitView.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "PART_PaneRoot");

            nav.PaneDisplayMode = FANavigationViewPaneDisplayMode.LeftCompact;
            nav.IsPaneOpen = true;
            Thread.Sleep(300);
            HeadlessAppHost.PumpJobs();

            Console.WriteLine($"[Open] Width={paneRoot.Width}");

            nav.IsPaneOpen = false;

            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(25);
                HeadlessAppHost.PumpJobs();
                Console.WriteLine($"[T+{(i+1)*25}ms] Width={paneRoot.Width}");
            }
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
