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
}
