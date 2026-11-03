using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Views;
using CkanUI.ModText;
using CkanUI.Services.Abstractions;
using CkanUI.TestSupport;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// i18n 页的密度契约：4807 条词条必须一行一条（而不是每条三行大卡），
/// 否则一屏只能看到十几条，用户没法在几万条译文里扫读（m00626 的反馈）。
/// </summary>
[TestFixture]
public sealed class ModTextPageDensityTests
{
    private sealed class FakeErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported { add { } remove { } }
        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();
        public void Report(Exception exception, string context, string? userMessage = null) { }
        public void Report(string context, string userMessage, string? detail = null) { }
    }

    private sealed class FakeModTextService : IModTextService
    {
        public List<ModTextEntryItem> Items { get; } = new();
        public int TotalEntries => Items.Count;

        public int Revision => 0;
        public string CurrentLanguage => "zh-cn";
        public string BaseDirectory => Path.GetTempPath();
        public IModTextProvider Provider => null!;
        public event Action? PackChanged { add { } remove { } }

        public Task ReloadPacksAsync(string? language = null, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> GetAvailableLanguages() => new[] { "zh-cn" };
        public IReadOnlyList<ModPackFileInfo> GetPackFiles(string? language = null) =>
            new[] { new ModPackFileInfo("packs/zh-cn/CKANMeta.jsonl", Path.Combine(Path.GetTempPath(), "CKANMeta.jsonl"), 1024, Items.Count, DateTime.UtcNow) };
        public IReadOnlyList<ModTextEntryItem> GetAllEntries(string? filter = null) => Items;
        public Task PutOverrideAsync(string identifier, string field, string sourceText, string translation, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteOverrideAsync(string identifier, string field, string sourceText, CancellationToken ct = default) => Task.CompletedTask;
        public Task ImportJsonlAsync(string targetFileName, string content, string? language = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<OperationResult<string>> ExportJsonlAsync(string relativePath, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string>.Ok(string.Empty));
        public Task<OperationResult<int>> SyncFromUniversalI18nAsync(string? sourceDir = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<int>.Ok(0));
    }

    private static ModTextPageView CreatePage(out ModTextPageViewModel vm, out Window window)
    {
        var service = new FakeModTextService();
        for (int i = 0; i < 200; i++)
        {
            service.Items.Add(new ModTextEntryItem(
                (ulong)i,
                "ckan:TestMod" + i + ":abstract",
                "A rather long abstract text for module " + i + " that would wrap onto three lines in the old card layout",
                "第 " + i + " 号模组的摘要译文，长度足够触发省略号而不是换行",
                "TestMod" + i,
                "CKANMeta",
                "machine",
                false));
        }

        vm = new ModTextPageViewModel(service, new FakeErrorReporter(), new FakeDispatcher());
        vm.LoadAsync().GetAwaiter().GetResult();

        // 视图里有 PathIcon，必须先建好无头平台（否则 StreamGeometry 找不到渲染接口）；
        // 但必须放在 VM 加载之后：EnsureInitialized 会装 SynchronizationContext，
        // 之前调会让 LoadAsync().GetResult() 在主线程上自锁。
        HeadlessAppHost.EnsureInitialized();
        var page = new ModTextPageView { DataContext = vm };
        window = HeadlessAppHost.CreateTestWindow(page, 1280, 800);
        HeadlessAppHost.PumpJobs();
        return page;
    }

    [Test]
    public void EntryRows_AreSingleLineTableRows()
    {
        CreatePage(out _, out var window);
        var list = window.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(x => x.Name == "ModTextEntryList");
        Assert.That(list, Is.Not.Null, "条目列表必须存在");
        Assert.That(list!.ItemCount, Is.GreaterThan(0));

        ListBoxItem? container = null;
        for (int i = 0; i < 20 && container is null; i++)
        {
            HeadlessAppHost.PumpJobs();
            container = list.ContainerFromIndex(0) as ListBoxItem;
        }

        Assert.That(container, Is.Not.Null, "首行容器必须已实现");
        Assert.That(container!.Bounds.Height, Is.LessThan(36), "条目必须是单行表行（旧卡式布局约 80px）");
    }

    [Test]
    public void RendersSnapshotForReview()
    {
        CreatePage(out _, out var window);
        HeadlessAppHost.PumpJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "无头 Skia 必须能抓帧");

        var target = Environment.GetEnvironmentVariable("CKAN_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        Directory.CreateDirectory(target);
        using var stream = File.Create(Path.Combine(target, "modtext-page.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }
}
