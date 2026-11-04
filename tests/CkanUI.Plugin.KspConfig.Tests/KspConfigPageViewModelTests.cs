using CkanUI.Plugin.KspConfig;
using CkanUI.Services.Abstractions.KspConfig;
using NUnit.Framework;

namespace CkanUI.Plugin.KspConfig.Tests;

[TestFixture]
public sealed class KspConfigPageViewModelTests
{
    private sealed class FakeKspConfigService : IKspConfigService
    {
        public KspConfigDocument Document { get; set; } = new()
        {
            FilePath = @"D:\KSP\settings.cfg",
            Language = "zh-cn",
            HasBackup = true,
            RawLines = ["SCREEN_RESOLUTION_WIDTH = 1920", "MASTER_VOLUME = 0.8"],
            LocalizationTable = new Dictionary<string, string>
            {
                ["#autoLOC_149894"] = "屏幕分辨率",
                ["#autoLOC_146154"] = "主音量"
            },
            Settings =
            [
                new KspSettingItem
                {
                    Key = "SCREEN_RESOLUTION_WIDTH",
                    Value = "1920",
                    OriginalValue = "1920",
                    DisplayName = "屏幕分辨率",
                    Category = KspSettingCategory.Graphics,
                    SettingType = KspSettingType.Integer,
                    MinValue = 800,
                    MaxValue = 3840,
                    Priority = 99
                },
                new KspSettingItem
                {
                    Key = "FULLSCREEN",
                    Value = "True",
                    OriginalValue = "True",
                    DisplayName = "全屏显示",
                    Category = KspSettingCategory.Graphics,
                    SettingType = KspSettingType.Boolean,
                    Priority = 100
                },
                new KspSettingItem
                {
                    Key = "MASTER_VOLUME",
                    Value = "0.8",
                    OriginalValue = "0.8",
                    DisplayName = "主音量",
                    Category = KspSettingCategory.Audio,
                    SettingType = KspSettingType.Float,
                    MinValue = 0,
                    MaxValue = 1,
                    Step = 0.05,
                    Priority = 90
                },
                new KspSettingItem
                {
                    Key = "UI_SIZE",
                    Value = "1.0",
                    OriginalValue = "1.0",
                    DisplayName = "操作界面尺寸",
                    Category = KspSettingCategory.Gameplay,
                    SettingType = KspSettingType.Float
                },
                new KspSettingItem
                {
                    Key = "ADVANCED_TWEAKABLES",
                    Value = "True",
                    OriginalValue = "True",
                    DisplayName = "高级部件调整",
                    Category = KspSettingCategory.Advanced,
                    SettingType = KspSettingType.Boolean
                }
            ]
        };

        public List<KspSettingItem> SavedSettings { get; } = [];
        public bool RestoredCalled { get; private set; }

        public Task<KspConfigDocument> LoadConfigAsync(string? instancePath = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Document);
        }

        public Task<bool> SaveConfigAsync(KspConfigDocument document, IEnumerable<KspSettingItem> modifiedSettings, CancellationToken cancellationToken = default)
        {
            SavedSettings.Clear();
            SavedSettings.AddRange(modifiedSettings);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyDictionary<string, string>> LoadOfficialLocalizationAsync(string? instancePath = null, string? language = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Document.LocalizationTable);
        }

        public Task<bool> RestoreBackupAsync(string? instancePath = null, CancellationToken cancellationToken = default)
        {
            RestoredCalled = true;
            return Task.FromResult(true);
        }

        public string? GetBackupPath(string? instancePath = null) => @"D:\KSP\settings.cfg.bak";
    }

    [Test]
    public async Task LoadConfigAsync_PopulatesItemsAndTabs()
    {
        var fakeService = new FakeKspConfigService();
        var vm = new KspConfigPageViewModel(fakeService);

        await vm.LoadConfigAsync();

        Assert.That(vm.FilteredItems.Count, Is.EqualTo(5));
        Assert.That(vm.FilePath, Is.EqualTo(@"D:\KSP\settings.cfg"));
        Assert.That(vm.Language, Is.EqualTo("zh-cn"));
        Assert.That(vm.HasBackup, Is.True);
        Assert.That(vm.HasUnsavedChanges, Is.False);
        Assert.That(vm.ModifiedCount, Is.EqualTo(0));

        // 验证分类 Tab 计数
        var allTab = vm.Tabs.First(t => t.Category == null);
        Assert.That(allTab.Count, Is.EqualTo(5));

        var graphicsTab = vm.Tabs.First(t => t.Category == KspSettingCategory.Graphics);
        Assert.That(graphicsTab.Count, Is.EqualTo(2));

        var audioTab = vm.Tabs.First(t => t.Category == KspSettingCategory.Audio);
        Assert.That(audioTab.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Filtering_ByTabAndSearch_WorksCorrectly()
    {
        var fakeService = new FakeKspConfigService();
        var vm = new KspConfigPageViewModel(fakeService);
        await vm.LoadConfigAsync();

        // 切换到音频 Tab
        vm.SelectedTab = vm.Tabs.First(t => t.Category == KspSettingCategory.Audio);
        Assert.That(vm.FilteredItems.Count, Is.EqualTo(1));
        Assert.That(vm.FilteredItems[0].Key, Is.EqualTo("MASTER_VOLUME"));

        // 切换回全部 Tab
        vm.SelectedTab = vm.Tabs.First(t => t.Category == null);
        Assert.That(vm.FilteredItems.Count, Is.EqualTo(5));

        // 搜索关键词
        vm.SearchText = "全屏";
        Assert.That(vm.FilteredItems.Count, Is.EqualTo(1));
        Assert.That(vm.FilteredItems[0].Key, Is.EqualTo("FULLSCREEN"));

        // 搜索 Key 英文名
        vm.SearchText = "SCREEN_RESOLUTION";
        Assert.That(vm.FilteredItems.Count, Is.EqualTo(1));
        Assert.That(vm.FilteredItems[0].Key, Is.EqualTo("SCREEN_RESOLUTION_WIDTH"));
    }

    [Test]
    public async Task ModifyingItem_TracksDirtyState_AndDiscardReverts()
    {
        var fakeService = new FakeKspConfigService();
        var vm = new KspConfigPageViewModel(fakeService);
        await vm.LoadConfigAsync();

        var widthItem = vm.FilteredItems.First(i => i.Key == "SCREEN_RESOLUTION_WIDTH");
        var fullscreenItem = vm.FilteredItems.First(i => i.Key == "FULLSCREEN");

        // 修改宽度
        widthItem.DoubleValue = 2560;
        Assert.That(widthItem.IsModified, Is.True);
        Assert.That(vm.HasUnsavedChanges, Is.True);
        Assert.That(vm.ModifiedCount, Is.EqualTo(1));

        // 修改全屏
        fullscreenItem.BooleanValue = false;
        Assert.That(fullscreenItem.IsModified, Is.True);
        Assert.That(vm.ModifiedCount, Is.EqualTo(2));

        // 撤回全屏的修改
        fullscreenItem.ResetCommand.Execute(null);
        Assert.That(fullscreenItem.IsModified, Is.False);
        Assert.That(fullscreenItem.BooleanValue, Is.True);
        Assert.That(vm.ModifiedCount, Is.EqualTo(1));

        // 放弃全部修改
        vm.DiscardChangesCommand.Execute(null);
        Assert.That(widthItem.IsModified, Is.False);
        Assert.That(widthItem.DoubleValue, Is.EqualTo(1920));
        Assert.That(vm.HasUnsavedChanges, Is.False);
        Assert.That(vm.ModifiedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task SaveConfigAsync_SendsModifiedSettings_AndClearsDirtyState()
    {
        var fakeService = new FakeKspConfigService();
        var vm = new KspConfigPageViewModel(fakeService);
        await vm.LoadConfigAsync();

        var volumeItem = vm.FilteredItems.First(i => i.Key == "MASTER_VOLUME");
        volumeItem.DoubleValue = 0.5;

        Assert.That(vm.HasUnsavedChanges, Is.True);

        await vm.SaveConfigAsync();

        Assert.That(fakeService.SavedSettings.Count, Is.EqualTo(1));
        Assert.That(fakeService.SavedSettings[0].Key, Is.EqualTo("MASTER_VOLUME"));
        Assert.That(fakeService.SavedSettings[0].Value, Is.EqualTo("0.5"));
    }

    [Test]
    public async Task RestoreBackupAsync_CallsServiceAndReloads()
    {
        var fakeService = new FakeKspConfigService();
        var vm = new KspConfigPageViewModel(fakeService);
        await vm.LoadConfigAsync();

        await vm.RestoreBackupAsync();

        Assert.That(fakeService.RestoredCalled, Is.True);
    }

    [Test]
    public async Task CommonSettings_ArePrioritizedAtTop()
    {
        var fakeService = new FakeKspConfigService();
        var vm = new KspConfigPageViewModel(fakeService);
        await vm.LoadConfigAsync();

        // 验证置顶逻辑：Priority 100 (FULLSCREEN) 在最前，其次 Priority 99 (SCREEN_RESOLUTION_WIDTH)
        Assert.That(vm.FilteredItems[0].Key, Is.EqualTo("FULLSCREEN"));
        Assert.That(vm.FilteredItems[0].IsCommon, Is.True);
        Assert.That(vm.FilteredItems[0].IsBoolean, Is.True);

        Assert.That(vm.FilteredItems[1].Key, Is.EqualTo("SCREEN_RESOLUTION_WIDTH"));
        Assert.That(vm.FilteredItems[1].IsCommon, Is.True);
        Assert.That(vm.FilteredItems[1].IsInteger, Is.True);
        Assert.That(vm.FilteredItems[1].IntegerValue, Is.EqualTo(1920));
    }

    [Test]
    public void SettingItemViewModel_TypeChecksAndValueConversions_Work()
    {
        var item = new KspSettingItem
        {
            Key = "TEST_FLOAT",
            Value = "0.75",
            OriginalValue = "0.75",
            DisplayName = "测试浮点",
            Category = KspSettingCategory.Audio,
            SettingType = KspSettingType.Float,
            Priority = 50
        };

        var vm = new KspSettingItemViewModel(item);
        Assert.That(vm.IsFloat, Is.True);
        Assert.That(vm.IsBoolean, Is.False);
        Assert.That(vm.IsCommon, Is.True);
        Assert.That(vm.DoubleValue, Is.EqualTo(0.75).Within(0.001));

        vm.IntegerValue = 42;
        Assert.That(vm.IntegerValue, Is.EqualTo(42));
        Assert.That(vm.Value, Is.EqualTo("42"));
        Assert.That(vm.IsModified, Is.True);

        vm.TextValue = "CustomText";
        Assert.That(vm.TextValue, Is.EqualTo("CustomText"));
        Assert.That(vm.Value, Is.EqualTo("CustomText"));
    }

    // ---- 工具条与列表摘要（原本这些都绑在不存在的属性上，界面上是死按钮与死空白） ----

    [Test]
    public void Tabs_CarryTitleIconAndCountForEveryCategory()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());

        Assert.That(vm.Tabs.Select(tab => tab.Title), Is.All.Not.Empty, "六个分类页签都得有标题：绑到不存在的 Header 上会渲染成一排空圆角块。");
        Assert.That(vm.Tabs.Select(tab => tab.Icon), Is.All.Not.Empty);
        Assert.That(vm.Tabs.Select(tab => tab.Title).Distinct().Count(), Is.EqualTo(vm.Tabs.Count), "页签标题不能重名。");
        Assert.That(vm.Tabs[0].Category, Is.Null, "第一个页签是「全部」，不该挂分类。");
        Assert.That(vm.SelectedTab, Is.SameAs(vm.Tabs[0]), "默认选中「全部」。");
    }

    [Test]
    public async Task ShowOnlyModified_FiltersToChangedItems_AndExplainsEmptyState()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());
        await vm.LoadConfigAsync();

        // 一条都没改：勾上「仅看已改」应当空列表 + 说明，而不是让人以为页面坏了。
        vm.ShowOnlyModified = true;
        Assert.That(vm.FilteredItems, Is.Empty);
        Assert.That(vm.HasEmptyResult, Is.True);
        Assert.That(vm.EmptyResultText, Does.Contain("还没有任何改动"));
        Assert.That(vm.CanResetFilters, Is.True);

        // 改一项之后立刻只留这一项。
        vm.ShowOnlyModified = false;
        vm.FilteredItems.First(item => item.Key == "MASTER_VOLUME").DoubleValue = 0.5;
        vm.ShowOnlyModified = true;

        Assert.That(vm.FilteredItems, Has.Count.EqualTo(1));
        Assert.That(vm.FilteredItems[0].Key, Is.EqualTo("MASTER_VOLUME"));
        Assert.That(vm.HasEmptyResult, Is.False);
    }

    [Test]
    public async Task ClearSearchCommand_EmptiesTheQuery()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());
        await vm.LoadConfigAsync();

        vm.SearchText = "全屏";
        Assert.That(vm.FilteredItems, Has.Count.EqualTo(1));
        Assert.That(vm.HasSearchText, Is.True);

        vm.ClearSearchCommand.Execute(null);

        Assert.That(vm.SearchText, Is.Empty);
        Assert.That(vm.HasSearchText, Is.False);
        Assert.That(vm.FilteredItems, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task ResetFiltersCommand_RestoresTabSearchAndModifiedOnly()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());
        await vm.LoadConfigAsync();

        vm.SelectedTab = vm.Tabs.First(tab => tab.Category == KspSettingCategory.Audio);
        vm.SearchText = "主音量";
        vm.ShowOnlyModified = true;
        Assert.That(vm.CanResetFilters, Is.True);

        vm.ResetFiltersCommand.Execute(null);

        Assert.That(vm.SelectedTab, Is.SameAs(vm.Tabs[0]));
        Assert.That(vm.SearchText, Is.Empty);
        Assert.That(vm.ShowOnlyModified, Is.False);
        Assert.That(vm.FilteredItems, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task VisibleCountText_ReportsFilteredOverTotal()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());
        await vm.LoadConfigAsync();

        Assert.That(vm.TotalCount, Is.EqualTo(5));
        Assert.That(vm.VisibleCountText, Is.EqualTo("5 / 5 项"));

        vm.SelectedTab = vm.Tabs.First(tab => tab.Category == KspSettingCategory.Audio);
        Assert.That(vm.VisibleCountText, Is.EqualTo("1 / 5 项"));
    }

    [Test]
    public async Task SaveButtonText_CarriesPendingCount()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());
        await vm.LoadConfigAsync();

        Assert.That(vm.SaveButtonText, Is.EqualTo("保存修改"), "没有待保存项时按钮就说「保存修改」。");

        vm.FilteredItems.First(item => item.Key == "MASTER_VOLUME").DoubleValue = 0.5;
        Assert.That(vm.SaveButtonText, Is.EqualTo("保存 1 项"));
    }

    [Test]
    public async Task HasStatus_TracksStatusMessage()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());

        Assert.That(vm.HasStatus, Is.False, "还没说话时状态条不占高度。");

        await vm.LoadConfigAsync();

        Assert.That(vm.StatusMessage, Is.Not.Empty);
        Assert.That(vm.HasStatus, Is.True);
    }

    [Test]
    public async Task EmptyResultText_DistinguishesNoMatchFromEmptyCategory()
    {
        var vm = new KspConfigPageViewModel(new FakeKspConfigService());
        await vm.LoadConfigAsync();

        vm.SelectedTab = vm.Tabs.First(tab => tab.Category == KspSettingCategory.Gameplay);
        vm.SearchText = "完全不存在的设置";

        Assert.That(vm.HasEmptyResult, Is.True);
        Assert.That(vm.EmptyResultText, Does.Contain("完全不存在的设置"), "搜索没命中要说明是搜索筛空的。");

        vm.SearchText = string.Empty;
        vm.SelectedTab = vm.Tabs.First(tab => tab.Category == KspSettingCategory.Other);

        Assert.That(vm.EmptyResultText, Does.Contain("其它项"), "分类下为空要说清是哪个分类。");
    }

    [Test]
    public void EmptyResultText_SaysSoWhenNothingWasLoaded()
    {
        var fakeService = new FakeKspConfigService();
        fakeService.Document = fakeService.Document with { Settings = [] };
        var vm = new KspConfigPageViewModel(fakeService);

        Assert.That(vm.HasEmptyResult, Is.True);
        Assert.That(vm.EmptyResultText, Does.Contain("还没有读到"));
    }
    [Test]
    public async Task Sections_GroupItemsByCategoryInTabOrder()
    {
        var viewModel = new KspConfigPageViewModel(new FakeKspConfigService());
        await viewModel.LoadConfigAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Sections.Select(section => section.Title),
                Is.EqualTo(new[] { "图像画质", "声音音效", "游戏常规", "高级调试" }),
                "「全部」页签下按分类分节，顺序跟页签一致，空分类不出现。");
            Assert.That(viewModel.Sections.Select(section => section.Count), Is.EqualTo(new[] { 2, 1, 1, 1 }));
            Assert.That(viewModel.Sections.Sum(section => section.Count), Is.EqualTo(viewModel.FilteredItems.Count),
                "分节不能丢条目，也不能重复。");
            Assert.That(viewModel.Sections[0].Items.Select(item => item.DisplayName),
                Is.EqualTo(new[] { "全屏显示", "屏幕分辨率" }),
                "节内顺序沿用「常用优先、再按键名」。");
        });
    }

    [Test]
    public async Task Sections_CollapseToOneWhenACategoryTabIsSelected()
    {
        var viewModel = new KspConfigPageViewModel(new FakeKspConfigService());
        await viewModel.LoadConfigAsync();

        viewModel.SelectedTab = viewModel.Tabs.Single(tab => tab.Title == "图像画质");

        Assert.That(viewModel.Sections, Has.Count.EqualTo(1), "选中具体分类时只该有一节。");
        Assert.That(viewModel.Sections[0].Title, Is.EqualTo("图像画质"));
        Assert.That(viewModel.Sections[0].Items.All(item => item.Category == KspSettingCategory.Graphics), Is.True);
    }

    [Test]
    public async Task Sections_FollowTheSearchFilter()
    {
        var viewModel = new KspConfigPageViewModel(new FakeKspConfigService());
        await viewModel.LoadConfigAsync();

        viewModel.SearchText = "MASTER";
        Assert.That(viewModel.Sections, Has.Count.EqualTo(1), "只搜到声音项目时不该留三个空节。");
        Assert.That(viewModel.Sections[0].Title, Is.EqualTo("声音音效"));
        Assert.That(viewModel.Sections[0].Items.Single().Key, Is.EqualTo("MASTER_VOLUME"));

        viewModel.SearchText = string.Empty;
        Assert.That(viewModel.Sections, Has.Count.EqualTo(4), "清空搜索后分节要复原。");
    }

}
