using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using NUnit.Framework;
using System;
using System.Linq;

namespace CkanUI.App.Tests;

[TestFixture]
public class ZzPaneGeomProbe
{
    static void Tick(int n = 30)
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

        void Dump(string tag)
        {
            var nav = window.FindControl<Control>("NavView")!;
            var page = window.FindControl<Control>("PageHost")!;
            var pt = page.TranslatePoint(new Point(0, 0), window);
            Console.WriteLine($"[{tag}] nav={nav.Bounds.Width:F1}x{nav.Bounds.Height:F1} classes={string.Join(",", nav.Classes)} pageX={pt?.X:F1} pageW={page.Bounds.Width:F1}");
            var sv = nav.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.GetType().Name == "SplitView");
            if (sv != null) Console.WriteLine($"    splitview classes={string.Join(",", sv.Classes)} bounds={sv.Bounds.Width:F1}");
            var paneRoot = nav.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PART_PaneRoot");
            if (paneRoot != null)
            {
                var pp = paneRoot.TranslatePoint(new Point(0, 0), window);
                Console.WriteLine($"    PaneRoot x={pp?.X:F1} w={paneRoot.Bounds.Width:F1} visible={paneRoot.IsVisible}");
            }
        }

        Dump("collapsed");
        shell.IsNavCollapsed = false;
        Tick(40);
        Dump("expanded");
        shell.IsNavCollapsed = true;
        Tick(40);
        Dump("collapsed-again");
    }
}
