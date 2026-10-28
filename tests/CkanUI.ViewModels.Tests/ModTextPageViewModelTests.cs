using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.ModText;
using CkanUI.Services.Abstractions;
using CkanUI.TestSupport;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class ModTextPageViewModelTests
{
    private sealed class ModTextFakeErrorReporter : IErrorReporter
    {
        public List<string> Contexts { get; } = new();
        public event Action<ErrorEntry>? Reported { add { } remove { } }
        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();
        public void Report(Exception exception, string context, string? userMessage = null) => Contexts.Add(context);
        public void Report(string context, string userMessage, string? detail = null) => Contexts.Add(context);
    }

    private sealed class FakeModTextService : IModTextService
    {
        public List<ModTextEntryItem> Entries { get; } = new();
        public List<ModPackFileInfo> Files { get; } = new();
        public List<string> Languages { get; } = new() { "zh-cn" };

        public int TotalEntries => Entries.Count;
        public string CurrentLanguage { get; set; } = "zh-cn";
        public string BaseDirectory => "fake_dir";
        public IModTextProvider Provider => null!;

        public event Action? PackChanged;

        public Task ReloadPacksAsync(string? language = null, CancellationToken ct = default)
        {
            if (language != null) CurrentLanguage = language;
            PackChanged?.Invoke();
            return Task.CompletedTask;
        }

        public IReadOnlyList<string> GetAvailableLanguages() => Languages;

        public IReadOnlyList<ModPackFileInfo> GetPackFiles(string? language = null) => Files;

        public IReadOnlyList<ModTextEntryItem> GetAllEntries(string? filter = null)
        {
            if (string.IsNullOrWhiteSpace(filter)) return Entries;
            return Entries.FindAll(e => (e.Source?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                         (e.Translation?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                         (e.Context?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        public Task PutOverrideAsync(string identifier, string field, string sourceText, string translation, CancellationToken ct = default)
        {
            Entries.Add(new ModTextEntryItem(12345, null, sourceText, translation, identifier, "CKANMeta", "human", false));
            PackChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task ImportJsonlAsync(string targetFileName, string content, string? language = null, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> ExportJsonlAsync(string relativePath, CancellationToken ct = default)
        {
            return Task.FromResult(string.Empty);
        }

        public Task DeleteOverrideAsync(string identifier, string field, string sourceText, CancellationToken ct = default)
        {
            Entries.RemoveAll(e => e.Context == identifier && e.Source == sourceText);
            PackChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task<int> SyncFromUniversalI18nAsync(string? sourceDir = null, CancellationToken ct = default)
        {
            PackChanged?.Invoke();
            return Task.FromResult(3);
        }
    }

    [Test]
    public async Task Activate_LoadsEntriesAndStats()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, null, "An autopilot mod", "自动驾驶模组", "MechJeb2", "CKANMeta", "human", false));
        service.Entries.Add(new ModTextEntryItem(2, null, "Telemetry info", "遥测信息", "KER", "CKANMeta", "human", false));
        service.Files.Add(new ModPackFileInfo("packs/zh-cn/CKANMeta.jsonl", "C:/fake/CKANMeta.jsonl", 1024, 2, DateTime.UtcNow));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.Entries.Count, Is.EqualTo(2));
        Assert.That(vm.SummaryText, Does.Contain("2"));
    }

    [Test]
    public async Task TrimProjection_丢掉词条且能重新解析()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, null, "An autopilot mod", "自动驾驶模组", "MechJeb2", "CKANMeta", "human", false));
        service.Entries.Add(new ModTextEntryItem(2, null, "Flight engineer", "飞行工程师", "KER", "CKANMeta", "human", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();
        vm.SearchQuery = "MechJeb";

        var released = vm.TrimProjection();

        // _allEntries 与过滤后的 Entries 都放掉，计数是两份之和（18 篇 §3 的 L1 档）。
        Assert.That(released, Is.GreaterThanOrEqualTo(2));
        Assert.That(vm.Entries, Is.Empty);
        Assert.That(vm.SummaryText, Is.Empty);
        Assert.That(vm.SearchQuery, Is.EqualTo("MechJeb"), "搜索词是用户输入，释放不该动它");
        Assert.That(vm.PageName, Is.EqualTo("模组文本"));

        await vm.LoadAsync();
        Assert.That(vm.Entries, Is.Not.Empty);
    }

    [Test]
    public void TrimProjection_没有词条时_释放零条()
    {
        var service = new FakeModTextService();
        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());

        Assert.That(vm.TrimProjection(), Is.EqualTo(0));
    }

    [Test]
    public async Task SearchFilter_FiltersByContextAndText()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, null, "An autopilot mod", "自动驾驶模组", "MechJeb2", "CKANMeta", "human", false));
        service.Entries.Add(new ModTextEntryItem(2, null, "Flight engineer", "飞行工程师", "KER", "CKANMeta", "human", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        vm.SearchQuery = "MechJeb";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("MechJeb2"));

        vm.SearchQuery = "工程师";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("KER"));

        vm.SearchQuery = "";
        Assert.That(vm.Entries.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task SaveOverrideCommand_AddsOverrideAndRefreshes()
    {
        var service = new FakeModTextService();
        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        vm.EditIdentifier = "TestMod";
        vm.EditField = "abstract";
        vm.EditSource = "Original source";
        vm.EditTranslation = "自定义翻译";

        Assert.That(vm.CanSaveOverride, Is.True);
        await vm.SaveOverrideCommand.ExecuteAsync(null);

        Assert.That(service.Entries.Count, Is.EqualTo(1));
        Assert.That(service.Entries[0].Context, Is.EqualTo("TestMod"));
        Assert.That(service.Entries[0].Translation, Is.EqualTo("自定义翻译"));
    }

    [Test]
    public async Task DeleteOverrideCommand_RemovesOverrideAndRefreshes()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, null, "Original source", "旧翻译", "TestMod", "CKANMeta", "human", false));
        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.Entries.Count, Is.EqualTo(1));

        vm.EditIdentifier = "TestMod";
        vm.EditField = "abstract";
        vm.EditSource = "Original source";

        Assert.That(vm.CanDeleteOverride, Is.True);
        await vm.DeleteOverrideCommand.ExecuteAsync(null);

        Assert.That(service.Entries.Count, Is.EqualTo(0));
        Assert.That(vm.StatusMessage, Does.Contain("删除覆盖项并生效"));
    }

    [Test]
    public async Task SyncFromUniversalI18nCommand_ExecutesSyncAndUpdatesStatus()
    {
        var service = new FakeModTextService();
        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        await vm.SyncFromUniversalI18nCommand.ExecuteAsync(null);

        Assert.That(vm.StatusMessage, Does.Contain("同步成功"));
        Assert.That(vm.StatusMessage, Does.Contain("3 个词库文件"));
    }
}
