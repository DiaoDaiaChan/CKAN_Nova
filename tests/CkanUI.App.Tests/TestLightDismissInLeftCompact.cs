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
public sealed class TestLightDismissInLeftCompact
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void TestClickOutsideWhenPanePinned()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();
            var nav = window.FindControl<FANavigationView>("NavView")!;
            var splitView = nav.GetVisualDescendants().OfType<SplitView>().First();
            var lightDismiss = splitView.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().FirstOrDefault(r => r.Name == "LightDismissLayer");

            Console.WriteLine($"nav.PaneDisplayMode: {nav.PaneDisplayMode}");
            Console.WriteLine($"splitView.DisplayMode: {splitView.DisplayMode}");
            Console.WriteLine($"LightDismissLayer IsVisible: {lightDismiss?.IsVisible}, IsHitTestVisible: {lightDismiss?.IsHitTestVisible}");

            // Expand
            vm.ToggleNavCollapsedCommand.Execute(null);
            HeadlessAppHost.PumpJobs();

            Console.WriteLine($"After Expand - nav.PaneDisplayMode: {nav.PaneDisplayMode}");
            Console.WriteLine($"After Expand - nav.IsPaneOpen: {nav.IsPaneOpen}");
            Console.WriteLine($"After Expand - nav.Classes: {string.Join(", ", nav.Classes)}");
            Console.WriteLine($"After Expand - LightDismissLayer IsVisible: {lightDismiss?.IsVisible}, IsHitTestVisible: {lightDismiss?.IsHitTestVisible}");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
