using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CkanUI.App;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;
using System;
using System.Linq;

namespace CkanUI.App.Tests;

[TestFixture]
public class ZzPopoutProbe
{
    static void Tick(int n = 25)
    {
        for (int i = 0; i < n; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void Probe()
    {
        HeadlessAppHost.EnsureInitialized();
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Width = 1280; window.Height = 800;
        window.Show();
        Tick(40);

        Console.WriteLine($"hoverMode={shell.NavHoverMode} collapsed={shell.IsNavCollapsed}");
        var nav = window.FindControl<FANavigationView>("NavView")!;
        var items = nav.GetVisualDescendants().OfType<FANavigationViewItem>().ToList();
        Console.WriteLine($"items={items.Count}");
        foreach (var it in items.Take(3)) Console.WriteLine($"  item {it.Tag} bounds={it.Bounds}");

        var target = items.FirstOrDefault(i => i.Bounds.Height > 0);
        if (target is null) { Console.WriteLine("no item"); return; }
        var p = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        Console.WriteLine($"moving to {p}");
        window.MouseMove(p!.Value, RawInputModifiers.None);
        Tick(30);

        var card = window.FindControl<Control>("NavTabPopoutCard");
        Console.WriteLine($"after hover: card={(card == null ? "null" : card.IsVisible + "/op=" + card.Opacity)}");
        if (card != null)
        {
            var cp = card.TranslatePoint(new Point(0, 0), window);
            Console.WriteLine($"  card pos={cp} size={card.Bounds.Size}");
            var txt = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t));
            Console.WriteLine("  texts=" + string.Join(" | ", txt));
        }

        window.MouseMove(new Point(900, 400), RawInputModifiers.None);
        Tick(30);
        Console.WriteLine($"after leave: card={(card == null ? "null" : card.IsVisible + "/op=" + card.Opacity)}");

        shell.IsNavCollapsed = false;
        Tick(40);
        var p2 = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        window.MouseMove(p2!.Value, RawInputModifiers.None);
        Tick(30);
        Console.WriteLine($"expanded hover: card={(card == null ? "null" : card.IsVisible + "/op=" + card.Opacity)}");
    }
}
