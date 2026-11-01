using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using CkanUI.App.Controls;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
[NonParallelizable]
public class MainWindowVlmMountingTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void MainWindow_MountsCursorPointerLayerOnVisualLayerManager()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            Assert.That(window.CursorPointer, Is.Not.Null, "CursorPointer property should be initialized");
            var cursor = window.CursorPointer!;
            Assert.That(cursor.GetVisualParent(), Is.InstanceOf<VisualLayerManager>(), "CursorPointerLayer should be child of VisualLayerManager");
            Assert.That(cursor.ZIndex, Is.EqualTo(int.MaxValue), "CursorPointerLayer should have int.MaxValue ZIndex");
            Assert.That(cursor.Bounds.Width, Is.EqualTo(window.Bounds.Width), "Bounds should match window width");
            Assert.That(cursor.Bounds.Height, Is.EqualTo(window.Bounds.Height), "Bounds should match window height");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void MainWindow_CursorRendersAboveOverlayLayers()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var vlm = window.GetVisualDescendants().OfType<VisualLayerManager>().First();
            var overlayLayer = OverlayLayer.GetOverlayLayer(vlm);
            Assert.That(overlayLayer, Is.Not.Null, "OverlayLayer should be created via VLM");

            HeadlessAppHost.PumpJobs();

            var cursor = window.CursorPointer!;
            Assert.That(cursor.ZIndex, Is.GreaterThan(overlayLayer!.ZIndex), "CursorPointerLayer ZIndex must be higher than OverlayLayer ZIndex!");
        }
        finally
        {
            window.Close();
        }
    }
}
