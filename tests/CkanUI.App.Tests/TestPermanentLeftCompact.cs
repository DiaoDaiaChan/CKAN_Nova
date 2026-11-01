using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class TestPermanentLeftCompact
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void TestPermanentLeftCompactCollapseAnimation()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();
            var nav = window.FindControl<FANavigationView>("NavView")!;
            var splitView = nav.GetVisualDescendants().OfType<SplitView>().First();
            var paneRoot = splitView.GetVisualDescendants().OfType<Panel>().First(p => p.Name == "PART_PaneRoot");

            nav.PaneDisplayMode = FANavigationViewPaneDisplayMode.LeftCompact;
            nav.IsPaneOpen = true;
            HeadlessAppHost.PumpJobs();

            Console.WriteLine($"[Expanded] nav.IsPaneOpen={nav.IsPaneOpen}, sv.IsPaneOpen={splitView.IsPaneOpen}, paneRoot.Width={paneRoot.Width}");

            nav.IsPaneOpen = false;
            Console.WriteLine($"[Collapse Start] nav.IsPaneOpen={nav.IsPaneOpen}, sv.IsPaneOpen={splitView.IsPaneOpen}, paneRoot.Width={paneRoot.Width}");

            HeadlessAppHost.PumpJobs();
            Console.WriteLine($"[After 1 Pump] nav.IsPaneOpen={nav.IsPaneOpen}, sv.IsPaneOpen={splitView.IsPaneOpen}, paneRoot.Width={paneRoot.Width}, bounds={paneRoot.Bounds.Width}");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
