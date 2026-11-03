using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Views;
using CkanUI.ModText;
using CkanUI.Services.Abstractions;
using CkanUI.TestSupport;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class ZzModTextDragProbe2
{
    private sealed class FakeErr : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported { add { } remove { } }
        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();
        public void Report(Exception e, string c, string? m = null) { }
        public void Report(string c, string m, string? d = null) { }
    }

    private sealed class FakeSvc : IModTextService
    {
        public List<ModTextEntryItem> Items { get; } = new();
        public int TotalEntries => Items.Count;
        public int Revision => 0;
        public string CurrentLanguage => "zh-cn";
        public string BaseDirectory => Path.GetTempPath();
        public IModTextProvider Provider => null!;
        public event Action? PackChanged { add { } remove { } }
        public Task ReloadPacksAsync(string? l = null, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> GetAvailableLanguages() => new[] { "zh-cn" };
        public IReadOnlyList<ModPackFileInfo> GetPackFiles(string? l = null) => Array.Empty<ModPackFileInfo>();
        public IReadOnlyList<ModTextEntryItem> GetAllEntries(string? f = null) => Items;
        public Task PutOverrideAsync(string a, string b, string c, string d, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteOverrideAsync(string a, string b, string c, CancellationToken ct = default) => Task.CompletedTask;
        public Task ImportJsonlAsync(string a, string b, string? c = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<OperationResult<string>> ExportJsonlAsync(string p, CancellationToken ct = default) => Task.FromResult(OperationResult<string>.Ok(string.Empty));
        public Task<OperationResult<int>> SyncFromUniversalI18nAsync(string? d = null, CancellationToken ct = default) => Task.FromResult(OperationResult<int>.Ok(0));
    }

    private static void Pump(Window w, int n = 4)
    {
        for (int i = 0; i < n; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); HeadlessAppHost.PumpJobs(); }
    }

    [Test]
    public void ThumbDragFast_KeepsRows()
    {
        var svc = new FakeSvc();
        for (int i = 0; i < 5000; i++)
            svc.Items.Add(new ModTextEntryItem((ulong)i, "ckan:Mod" + i + ":abstract", "source " + i, "译文 " + i, "Mod" + i, "CKANMeta", "machine", false));
        var vm = new ModTextPageViewModel(svc, new FakeErr(), new FakeDispatcher());
        vm.LoadAsync().GetAwaiter().GetResult();
        HeadlessAppHost.EnsureInitialized();
        var page = new ModTextPageView { DataContext = vm };
        var window = HeadlessAppHost.CreateTestWindow(page, 1280, 800);
        Pump(window, 20);

        var list = window.GetVisualDescendants().OfType<ListBox>().First(x => x.Name == "ModTextEntryList");
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().First();
        var pt = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        Console.WriteLine($"bar bounds={bar.Bounds} thumb={thumb.Bounds} at {pt}");

        window.MouseMove(pt, RawInputModifiers.None);
        window.MouseDown(pt, MouseButton.Left, RawInputModifiers.None);
        Pump(window, 2);
        for (int i = 1; i <= 6; i++)
        {
            window.MouseMove(new Point(pt.X, pt.Y + 60 * i), RawInputModifiers.None);
            Pump(window, 1);
        }
        window.MouseUp(new Point(pt.X, pt.Y + 360), MouseButton.Left, RawInputModifiers.None);
        Pump(window, 10);

        Console.WriteLine($"after drag: offset={scroll.Offset.Y:0} extent={scroll.Extent.Height:0} viewport={scroll.Viewport.Height:0} realized={list.GetRealizedContainers().Count()} items={list.ItemCount}");
    }
}
