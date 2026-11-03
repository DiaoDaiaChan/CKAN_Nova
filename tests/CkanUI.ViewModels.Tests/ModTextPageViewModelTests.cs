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

        public int Revision => 0;
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

        public OperationResult<string> ExportResult { get; set; } = OperationResult<string>.Ok(string.Empty);

        public Task<OperationResult<string>> ExportJsonlAsync(string relativePath, CancellationToken ct = default)
        {
            return Task.FromResult(ExportResult);
        }

        public Task DeleteOverrideAsync(string identifier, string field, string sourceText, CancellationToken ct = default)
        {
            Entries.RemoveAll(e => e.Context == identifier && e.Source == sourceText);
            PackChanged?.Invoke();
            return Task.CompletedTask;
        }

        public OperationResult<int> SyncResult { get; set; } = OperationResult<int>.Ok(3);

        public Task<OperationResult<int>> SyncFromUniversalI18nAsync(string? sourceDir = null, CancellationToken ct = default)
        {
            // 只有成功才触发重载通知，与真实服务一致（失败时不会重新扫盘）。
            if (SyncResult.IsSuccess) PackChanged?.Invoke();
            return Task.FromResult(SyncResult);
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
    public async Task ScopeFilter_SeparatesCkanMetadataFromInGamePacks()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A mod", "一个模组", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, null, "Blue Paint", "蓝色涂装", null, "Waterfall", null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.ScopeFilterOptions.Count, Is.EqualTo(3), "范围下拉：全部 / CKAN 元数据 / 模组翻译");
        Assert.That(vm.Entries.Count, Is.EqualTo(2), "默认「全部范围」两套语料都在");

        vm.SelectedScopeFilter = vm.ScopeFilterOptions[1];
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("Alpha"));
        Assert.That(vm.ModGroups.Count, Is.EqualTo(2), "首项「全部模组」+ 只剩元数据的那一组");
        Assert.That(vm.ModGroups[1].Key, Is.EqualTo("Alpha"));

        vm.SelectedScopeFilter = vm.ScopeFilterOptions[2];
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Mod, Is.EqualTo("Waterfall"));
        Assert.That(vm.ModGroups[1].Key, Is.EqualTo("Waterfall"));
        Assert.That(vm.SummaryText, Does.Contain("1"));

        vm.SelectedScopeFilter = vm.ScopeFilterOptions[0];
        Assert.That(vm.Entries.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ScopeSwitch_ResetsGroupSelectionSoListIsNotEmpty()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A mod", "一个模组", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, null, "Blue Paint", "蓝色涂装", null, "Waterfall", null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        vm.SelectedGroup = vm.ModGroups.First(g => g.Key == "Alpha");
        Assert.That(vm.Entries.Count, Is.EqualTo(1));

        // 切到模组翻译：Alpha 这一组在同范围里不存在，分组选择必须归零，否则列表筛成空。
        vm.SelectedScopeFilter = vm.ScopeFilterOptions[2];
        Assert.That(vm.SelectedGroupKey, Is.Empty);
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
    }

    [Test]
    public void ToggleOverridePanelCommand_FlipsVisibility()
    {
        var vm = new ModTextPageViewModel(new FakeModTextService(), new ModTextFakeErrorReporter(), new FakeDispatcher());
        Assert.That(vm.IsOverridePanelVisible, Is.True);
        vm.ToggleOverridePanelCommand.Execute(null);
        Assert.That(vm.IsOverridePanelVisible, Is.False);
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

    [Test]
    public async Task SyncFromUniversalI18nCommand_Success_SetsStatusBarText()
    {
        var service = new FakeModTextService { SyncResult = OperationResult<int>.Ok(5) };
        var errors = new ModTextFakeErrorReporter();
        var vm = new ModTextPageViewModel(service, errors, new FakeDispatcher());
        await vm.LoadAsync();

        await vm.SyncFromUniversalI18nCommand.ExecuteAsync(null);

        Assert.That(vm.StatusMessage, Is.EqualTo("同步成功！已同步 5 个词库文件并实时重载。"));
        Assert.That(errors.Contexts, Is.Empty);
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public async Task FilterOptions_ExposeAllFirstAndUnlabeledLast()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Beta:abstract", "B", "乙", "Beta", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(3, null, "C", "丙", "Gamma", null, null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.FieldFilterOptions[0], Is.EqualTo("全部字段"));
        Assert.That(vm.FieldFilterOptions, Does.Contain("abstract"));
        Assert.That(vm.FieldFilterOptions[^1], Is.EqualTo("无字段"));
        Assert.That(vm.HasActiveFilters, Is.False);
        // 模组维度只有左栏分组一条路径（没有第二套「按包名筛选」的下拉）
        Assert.That(vm.ModGroups[0].IsAll, Is.True);
        Assert.That(vm.ModGroups.Count, Is.EqualTo(4), "全部 + Alpha + Beta + 未标注");
    }

    [Test]
    public async Task FieldFilter_ParsesFieldFromStableKey()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Alpha:description", "B", "乙", "Alpha", "CKANMeta", "machine", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        vm.SelectedFieldFilter = "description";

        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Source, Is.EqualTo("B"));
    }

    [Test]
    public async Task KindFilter_SeparatesTranslatedKeptAndManual()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, null, "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, null, "B", "B", "Beta", "CKANMeta", "machine", true));
        service.Entries.Add(new ModTextEntryItem(3, null, "C", "丙", "Gamma", "CKANMeta", "human", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();
        Assert.That(vm.KindFilterOptions, Is.EqualTo(new[] { "全部", "已翻译", "保留原文", "人工覆盖" }));

        vm.SelectedKindFilter = "保留原文";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].SourceBadge, Is.EqualTo("保留/不可译"));

        vm.SelectedKindFilter = "人工覆盖";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("Gamma"));

        vm.SelectedKindFilter = "已翻译";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Source, Is.EqualTo("A"), "已翻译不含保留原文与人工覆盖");
    }

    [Test]
    public async Task SearchCoversModAndFieldInAdditionToText()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:description", "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Beta:abstract", "B", "乙", "Beta", "OtherPack", "machine", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        vm.SearchQuery = "OtherPack";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("Beta"));

        vm.SearchQuery = "description";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));
        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("Alpha"));
    }

    [Test]
    public async Task ClearFiltersCommand_ResetsEveryDimension()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Beta:description", "B", "乙", "Beta", "OtherPack", "human", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        vm.SearchQuery = "乙";
        vm.SelectedGroup = vm.ModGroups.First(g => g.Key == "Beta");
        vm.SelectedFieldFilter = "description";
        vm.SelectedKindFilter = "人工覆盖";
        Assert.That(vm.Entries.Count, Is.EqualTo(1));

        vm.ClearFiltersCommand.Execute(null);

        Assert.That(vm.SearchQuery, Is.Empty);
        Assert.That(vm.SelectedGroupKey, Is.Empty);
        Assert.That(vm.SelectedFieldFilter, Is.EqualTo("全部字段"));
        Assert.That(vm.SelectedKindFilter, Is.EqualTo("全部"));
        Assert.That(vm.HasActiveFilters, Is.False);
        Assert.That(vm.Entries.Count, Is.EqualTo(2));
        Assert.That(vm.SummaryText, Is.EqualTo("已显示 2 / 2 条"), "清零后页脚不留「已筛选」后缀");
    }

    [Test]
    public async Task SyncFromUniversalI18nCommand_Failure_ReportsToErrorOutlet()
    {
        const string failMessage = "未找到 UniversalI18n 仓库或 packs 目录。请确认路径是否存在。";
        var service = new FakeModTextService
        {
            SyncResult = OperationResult<int>.Fail(failMessage, "no-such-dir"),
        };
        var errors = new ModTextFakeErrorReporter();
        var vm = new ModTextPageViewModel(service, errors, new FakeDispatcher());
        await vm.LoadAsync();
        errors.Contexts.Clear();

        await vm.SyncFromUniversalI18nCommand.ExecuteAsync(null);

        // 失败必须走既有错误出口（错误中心），并在状态栏给出中文原因，而不是抛到 UI 线程。
        Assert.That(errors.Contexts, Does.Contain("ModTextPage.SyncUniversalI18n"));
        Assert.That(vm.StatusMessage, Does.Contain("同步失败"));
        Assert.That(vm.StatusMessage, Does.Contain(failMessage));
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public async Task StableKey_WinsOverNormalizedForIdentifierAndField()
    {
        var service = new FakeModTextService();
        // 运行时包里的 n 是归一化原文，只有 k 带着 ckan:<identifier>:<field>。
        service.Entries.Add(new ModTextEntryItem(1, "a copy of the redone restock legs", "A copy of the redone Restock legs", "重制版着陆腿", null, "CKANMeta", null, false, "ckan:RestockRigidLegs:abstract"));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("RestockRigidLegs"));
        Assert.That(vm.Entries[0].Field, Is.EqualTo("abstract"));
        Assert.That(vm.ModGroups.Count, Is.EqualTo(2), "全部 + 真实模组一组");
        Assert.That(vm.ModGroups[1].Key, Is.EqualTo("RestockRigidLegs"));
    }

    [Test]
    public async Task InGamePackWithoutStableKeyFallsBackToModInFirstColumn()
    {
        var service = new FakeModTextService();
        // 游戏内语言包（modui / 部件描述）没有 k，首列得退回 mod 名，否则表格看着像空的。
        service.Entries.Add(new ModTextEntryItem(1, "a simple test single multi-emitter system", "A simple test single multi-emitter system", "简单的多发射器测试系统", null, "Waterfall", null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.Entries[0].Identifier, Is.Empty);
        Assert.That(vm.Entries[0].DisplayKey, Is.EqualTo("Waterfall"));
        Assert.That(vm.ModGroups[1].Key, Is.EqualTo("Waterfall"));
    }

    [Test]
    public async Task GroupFilterQuery_NarrowsRailAndFallsBackWhenSelectionDisappears()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", null, false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Beta:abstract", "B", "乙", "Beta", "CKANMeta", null, false));
        service.Entries.Add(new ModTextEntryItem(3, "ckan:Gamma:abstract", "C", "丙", "Gamma", "CKANMeta", null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();
        Assert.That(vm.ModGroups.Count, Is.EqualTo(4), "全部 + 3 个模组");

        vm.GroupFilterQuery = "be";
        Assert.That(vm.ModGroups.Count, Is.EqualTo(2), "「全部模组」常驻 + 命中 Beta");
        Assert.That(vm.ModGroups[0].IsAll, Is.True);

        vm.SelectedGroup = vm.ModGroups.First(g => g.Key == "Beta");
        Assert.That(vm.Entries.Count, Is.EqualTo(1));

        // 选中的组被过滤掉时必须回落「全部」，否则界面显示的组与筛选条件各说各话。
        vm.GroupFilterQuery = "gam";
        Assert.That(vm.SelectedGroupKey, Is.Empty);
        Assert.That(vm.Entries.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task ModGroups_GroupByIdentifierWithCountsAndAllFirst()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Alpha:description", "B", "乙", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(3, "ckan:Beta:abstract", "C", "丙", "Beta", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(4, null, "D", "丁", null, "LegacyPack", null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.ModGroups[0].IsAll, Is.True);
        Assert.That(vm.ModGroups[0].DisplayName, Is.EqualTo("全部模组"));
        Assert.That(vm.ModGroups[0].Count, Is.EqualTo(4));
        Assert.That(vm.ModGroups[0].CountText, Is.EqualTo("4 条"));
        Assert.That(vm.ModGroups[1].Key, Is.EqualTo("Alpha"), "条目多的分组排前面");
        Assert.That(vm.ModGroups[1].Count, Is.EqualTo(2));
        Assert.That(vm.ModGroups.Any(g => g.Key == "LegacyPack"), Is.True, "没有 identifier 的老数据退回语言包 mod 名");
        Assert.That(vm.SelectedGroup, Is.SameAs(vm.ModGroups[0]), "默认选「全部」");
    }

    [Test]
    public async Task SelectedGroup_FiltersEntriesAndFlipsActiveFilterFlag()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Alpha:description", "B", "乙", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(3, "ckan:Beta:abstract", "C", "丙", "Beta", "CKANMeta", "machine", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();
        Assert.That(vm.HasActiveFilters, Is.False);

        vm.SelectedGroup = vm.ModGroups.First(g => g.Key == "Alpha");

        Assert.That(vm.Entries.Count, Is.EqualTo(2), "点分组只看这个模组的词条");
        Assert.That(vm.SelectedGroupKey, Is.EqualTo("Alpha"));
        Assert.That(vm.HasActiveFilters, Is.True);
        Assert.That(vm.SummaryText, Does.Contain("已筛选"));
    }

    [Test]
    public async Task ClearFiltersCommand_ResetsGroupSelection()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, "ckan:Beta:abstract", "B", "乙", "Beta", "CKANMeta", "machine", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();
        vm.SelectedGroup = vm.ModGroups.First(g => g.Key == "Beta");
        Assert.That(vm.Entries.Count, Is.EqualTo(1));

        vm.ClearFiltersCommand.Execute(null);

        Assert.That(vm.SelectedGroupKey, Is.Empty);
        Assert.That(vm.SelectedGroup, Is.Not.Null);
        Assert.That(vm.SelectedGroup!.IsAll, Is.True);
        Assert.That(vm.Entries.Count, Is.EqualTo(2));
        Assert.That(vm.HasActiveFilters, Is.False);
    }

    [Test]
    public async Task TrimProjection_DropsModGroupsWithEntries()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A", "甲", "Alpha", "CKANMeta", "machine", false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();
        Assert.That(vm.ModGroups, Is.Not.Empty);

        vm.TrimProjection();

        Assert.That(vm.ModGroups, Is.Empty, "分组是词条的投影，释放时必须一起丢掉");
        Assert.That(vm.SelectedGroup, Is.Null);
    }

    [Test]
    public async Task SelectScopeCommand_DrivesTheThreeScopeButtons()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, "ckan:Alpha:abstract", "A mod", "一个模组", "Alpha", "CKANMeta", "machine", false));
        service.Entries.Add(new ModTextEntryItem(2, null, "Blue Paint", "蓝色涂装", null, "Waterfall", null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.IsScopeAll, Is.True, "默认落在全部范围");
        Assert.That(vm.IsScopeMetadata, Is.False);
        Assert.That(vm.IsScopeMods, Is.False);

        vm.SelectScopeCommand.Execute(vm.ScopeFilterOptions[1]);

        Assert.That(vm.IsScopeMetadata, Is.True, "点「CKAN 元数据」按钮后只有它是选中态");
        Assert.That(vm.IsScopeAll, Is.False);
        Assert.That(vm.Entries, Has.Count.EqualTo(1));
        Assert.That(vm.Entries[0].Identifier, Is.EqualTo("Alpha"));

        vm.SelectScopeCommand.Execute(vm.ScopeFilterOptions[2]);

        Assert.That(vm.IsScopeMods, Is.True);
        Assert.That(vm.Entries, Has.Count.EqualTo(1));
        Assert.That(vm.Entries[0].Mod, Is.EqualTo("Waterfall"));

        vm.SelectScopeCommand.Execute(string.Empty);

        Assert.That(vm.IsScopeMods, Is.True, "空参数不改变当前范围");
    }

    [Test]
    public async Task SourceBadgeColumn_OnlyShowsWhenARowHasNonDefaultSource()
    {
        var service = new FakeModTextService();
        service.Entries.Add(new ModTextEntryItem(1, null, "Blue Paint", "蓝色涂装", null, "Waterfall", null, false));
        service.Entries.Add(new ModTextEntryItem(2, null, "White Paint", "白色涂装", null, "Waterfall", null, false));

        var vm = new ModTextPageViewModel(service, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm.LoadAsync();

        Assert.That(vm.Entries.All(entry => !entry.ShowSourceBadge), Is.True, "默认词典条目不该挂徽标");
        Assert.That(vm.ShowsSourceBadgeColumn, Is.False, "整批都是默认来源时类型列连表头一起收起");

        // 重载走 Revision 短路（包没变就不重建词条），所以新增条目要换一个 VM 重新载入
        var svc2 = new FakeModTextService();
        svc2.Entries.Add(new ModTextEntryItem(1, null, "Blue Paint", "蓝色涂装", null, "Waterfall", null, false));
        svc2.Entries.Add(new ModTextEntryItem(3, null, "Red Paint", "红色涂装", null, "Waterfall", "machine", false));
        var vm2 = new ModTextPageViewModel(svc2, new ModTextFakeErrorReporter(), new FakeDispatcher());
        await vm2.LoadAsync();

        Assert.That(vm2.ShowsSourceBadgeColumn, Is.True, "出现机翻条目后类型列要回来");
        Assert.That(vm2.Entries.Single(entry => entry.Source == "Red Paint").ShowSourceBadge, Is.True);
        Assert.That(vm2.Entries.Single(entry => entry.Source == "Blue Paint").ShowSourceBadge, Is.False);
    }
}
