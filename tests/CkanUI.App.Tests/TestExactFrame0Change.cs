using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class TestExactFrame0Change
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void TestWhatChangesOnFrame0()
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

            nav.IsPaneOpen = true;
            HeadlessAppHost.PumpJobs();

            paneRoot.PropertyChanged += (s, e) =>
            {
                if (e.Property == Layoutable.WidthProperty)
                {
                    Console.WriteLine($"Width changed: {e.OldValue} -> {e.NewValue}");
                    Console.WriteLine(Environment.StackTrace);
                }
            };

            Console.WriteLine("Setting nav.IsPaneOpen = false...");
            nav.IsPaneOpen = false;

            Console.WriteLine("Pumping jobs...");
            HeadlessAppHost.PumpJobs();
            Console.WriteLine("Done pumping jobs.");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
