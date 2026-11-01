using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class TestStyleOverrideForCloseTransition
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void TestWhatIsVisibleDuringCollapse()
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

            // Expand via command
            vm.ToggleNavCollapsedCommand.Execute(null);
            HeadlessAppHost.PumpJobs();

            var firstItem = nav.MenuItems.OfType<FANavigationViewItem>().First();
            var contentPresenter = firstItem.GetVisualDescendants().OfType<ContentPresenter>().First(cp => cp.Name == "ContentPresenter");
            var brandLabel = nav.GetVisualDescendants().OfType<TextBlock>().First(tb => tb.Classes.Contains("BrandLabel"));

            Console.WriteLine($"[Expanded] cp.Opacity={contentPresenter.Opacity}, brand.Opacity={brandLabel.Opacity}");

            // Collapse via command
            vm.ToggleNavCollapsedCommand.Execute(null);
            Console.WriteLine($"[Collapse 0ms] nav.Classes={string.Join(",", nav.Classes)}, cp.Opacity={contentPresenter.Opacity}, brand.Opacity={brandLabel.Opacity}");

            HeadlessAppHost.PumpJobs();
            Console.WriteLine($"[Collapse Mid] paneRoot.Width={paneRoot.Width}, cp.Opacity={contentPresenter.Opacity}, brand.Opacity={brandLabel.Opacity}");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
