using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.ModConfig;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.ModConfig;

/// <summary>
/// Mod 配置页 VM（M9 / UI-CFG-01 骨架）：模型装配、选中优先序、搜索与"仅看已改"筛选、
/// KSP 运行中警示、空模型不崩、读盘失败降级成文案、译文委托。
/// </summary>
[TestFixture]
public sealed class ModConfigPageViewModelTests
{
    [Test]
    public void DesignConstructor_WorksWithoutServices()
    {
        var vm = new ModConfigPageViewModel();

        Assert.That(vm.Title, Is.EqualTo("Mod 配置"));
        Assert.That(vm.Mods, Is.Empty);
        Assert.That(vm.IsSelectionEmpty, Is.True);
        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.HasChanges, Is.False);
        Assert.That(vm.VisibleCountText, Is.Empty);
    }

    [Test]
    public void TranslateDelegate_IsUsedForFixedTexts()
    {
        var vm = Create(new FakeConfigService(), translate: text => "T(" + text + ")");

        Assert.That(vm.Title, Is.EqualTo("T(Mod 配置)"));
        Assert.That(vm.NoSelectionHintText, Is.EqualTo("T(左侧选择一个 mod 查看它的可配置项。)"));
    }

    [Test]
    public void ApplyModel_BuildsCardsAndCounts()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(
            mods:
            [
                ModConfigTestData.Mod(adapterId: "a", name: "A Mod"),
                ModConfigTestData.Mod(adapterId: "b", name: "B Mod"),
            ],
            adapterlessModCount: 3));

        Assert.That(vm.Mods.Count, Is.EqualTo(2));
        Assert.That(vm.HasMods, Is.True);
        Assert.That(vm.ModCountText, Is.EqualTo("共 2 个已适配 mod"));
        Assert.That(vm.HasSelection, Is.True);
        Assert.That(vm.AdapterlessText, Is.EqualTo("⊘ 3 个 mod 无适配器"));
        Assert.That(vm.HasAdapterless, Is.True);
        Assert.That(vm.InstanceSummaryText, Is.EqualTo("实例：默认｜游戏目录：C:/game"));
    }

    [Test]
    public void ApplyModel_PrefersEditableCardWithChanges()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods:
        [
            ModConfigTestData.Mod(adapterId: "readonly", name: "只读 Mod", state: ModConfigAdapterState.ReadOnlyVersion),
            ModConfigTestData.Mod(adapterId: "editable", name: "可编辑 Mod"),
            ModConfigTestData.Mod(adapterId: "dirty", name: "改过的 Mod", modifiedCount: 2),
        ]));

        Assert.That(vm.SelectedMod!.AdapterId, Is.EqualTo("dirty"));
        Assert.That(vm.HeaderTitleText, Is.EqualTo("改过的 Mod v1.2.3"));
        Assert.That(vm.ModifiedBarText, Is.EqualTo("改动 2 项"));
        Assert.That(vm.HasChanges, Is.True);
    }

    [Test]
    public void ApplyModel_FallsBackToFirstCardWhenNothingEditable()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods:
        [
            ModConfigTestData.Mod(adapterId: "first", name: "第一个", state: ModConfigAdapterState.ReadOnlyVersion),
            ModConfigTestData.Mod(adapterId: "second", name: "第二个", state: ModConfigAdapterState.ReadOnlyRaw),
        ]));

        Assert.That(vm.SelectedMod!.AdapterId, Is.EqualTo("first"));
        Assert.That(vm.HasChanges, Is.False);
        Assert.That(vm.ModifiedBarText, Is.Empty);
    }

    [Test]
    public void ApplyModel_GameRunningShowsWarning()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods: [ModConfigTestData.Mod()], isGameRunning: true));

        Assert.That(vm.IsGameRunning, Is.True);
        Assert.That(vm.ShowGameRunningWarning, Is.True);
        Assert.That(vm.GameRunningWarningText, Does.Contain("重启"));
    }

    [Test]
    public void ApplyModel_NullClearsEverything()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods: [ModConfigTestData.Mod()], adapterlessModCount: 2));
        vm.ApplyModel(null);

        Assert.That(vm.Mods, Is.Empty);
        Assert.That(vm.SelectedMod, Is.Null);
        Assert.That(vm.IsSelectionEmpty, Is.True);
        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.InstanceSummaryText, Is.Empty);
        Assert.That(vm.HasAdapterless, Is.False);
        Assert.That(vm.IsGameRunning, Is.False);
    }

    [Test]
    public void ApplyModel_SummaryWithoutInstanceName()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods: [ModConfigTestData.Mod()], instanceName: string.Empty));

        Assert.That(vm.InstanceSummaryText, Is.EqualTo("游戏目录：C:/game"));
    }

    [Test]
    public void Search_FiltersRowsByKeyLabelOrComment()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods:
        [
            ModConfigTestData.Mod(files:
            [
                ModConfigTestData.WritableFile(
                    ModConfigTestData.Field("MaxPhysicsDelta", ModConfigFieldType.Float, "0.05", label: "物理步长"),
                    ModConfigTestData.Field("Language", ModConfigFieldType.Enum, "en-us", comment: "界面语言")),
            ]),
        ]));

        Assert.That(vm.FieldTotal, Is.EqualTo(2));
        Assert.That(vm.SearchPlaceholder, Is.EqualTo("搜索 2 项…"));
        Assert.That(vm.VisibleFieldCount, Is.EqualTo(2));

        vm.SearchText = "物理";

        Assert.That(vm.VisibleFieldCount, Is.EqualTo(1));
        Assert.That(vm.VisibleCountText, Is.EqualTo("显示 1 / 2 项"));

        vm.ClearSearchCommand.Execute(null);

        Assert.That(vm.SearchText, Is.Empty);
        Assert.That(vm.VisibleFieldCount, Is.EqualTo(2));
    }

    [Test]
    public void OnlyModified_FiltersUnchangedRows()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods:
        [
            ModConfigTestData.Mod(modifiedCount: 1, files:
            [
                ModConfigTestData.WritableFile(
                    ModConfigTestData.Field("a", value: "2", defaultValue: "1"),
                    ModConfigTestData.Field("b", value: "1", defaultValue: "1")),
            ]),
        ]));

        Assert.That(vm.VisibleFieldCount, Is.EqualTo(2));

        vm.OnlyModified = true;

        Assert.That(vm.VisibleFieldCount, Is.EqualTo(1));
        Assert.That(vm.FieldGroups.Single().Rows.Single().Key, Is.EqualTo("a"));
    }

    [Test]
    public void Groups_AreOrderedAndTitlelessGroupKeepsEmptyTitle()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods:
        [
            ModConfigTestData.Mod(files:
            [
                ModConfigTestData.WritableFile(
                    ModConfigTestData.Field("a", group: "游戏性"),
                    ModConfigTestData.Field("b"),
                    ModConfigTestData.Field("c", group: "游戏性")),
            ]),
        ]));

        Assert.That(vm.FieldGroups.Count, Is.EqualTo(2));
        var gameplay = vm.FieldGroups.Single(group => group.Title == "游戏性");
        Assert.That(gameplay.HasTitle, Is.True);
        Assert.That(gameplay.Rows.Count, Is.EqualTo(2));

        var untitled = vm.FieldGroups.Single(group => group.Title.Length == 0);
        Assert.That(untitled.HasTitle, Is.False);
        Assert.That(untitled.Rows.Single().Key, Is.EqualTo("b"));
    }

    [Test]
    public void Fields_OfReadonlyFileAreNotEditable()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods:
        [
            ModConfigTestData.Mod(files:
            [
                ModConfigTestData.ReadonlyFile("该文件控制堆垫片总量", ModConfigTestData.Field("size")),
            ]),
        ]));

        var row = vm.FieldGroups.Single().Rows.Single();
        Assert.That(row.IsFileWritable, Is.False);
        Assert.That(row.CanEdit, Is.False);
    }

    [Test]
    public void SelectMod_NullClearsSelection()
    {
        var vm = Create(new FakeConfigService());
        vm.ApplyModel(ModConfigTestData.Model(mods: [ModConfigTestData.Mod()]));

        vm.SelectModCommand.Execute(null);

        Assert.That(vm.IsSelectionEmpty, Is.True);
        Assert.That(vm.HeaderTitleText, Is.EqualTo("未选择 mod"));
        Assert.That(vm.FieldGroups, Is.Empty);
    }

    [Test]
    public void Adapters_AreListedFromCatalog()
    {
        var catalog = new FakeCatalog
        {
            Adapters = [ModConfigTestData.Adapter(), ModConfigTestData.Adapter("other-mod", "Other Mod", isUserProvided: true)],
        };
        var vm = Create(new FakeConfigService(), catalog: catalog);

        vm.ReloadAdaptersCommand.Execute(null);

        Assert.That(vm.Adapters.Count, Is.EqualTo(2));
        Assert.That(vm.HasAdapters, Is.True);
        Assert.That(vm.AdapterListTitle, Is.EqualTo("已加载 2 个适配器"));
        Assert.That(vm.Adapters[1].SourceText, Is.EqualTo("用户目录"));
        Assert.That(catalog.ReloadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Activate_LoadsModelFromService()
    {
        var service = new FakeConfigService
        {
            Result = OperationResult<ModConfigInstanceModel>.Ok(ModConfigTestData.Model(mods: [ModConfigTestData.Mod()])),
        };
        var vm = Create(service);

        vm.Activate();
        await WaitUntilAsync(() => vm.Mods.Count == 1);

        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.StatusText, Is.EqualTo("读取完成：共 1 个已适配 mod。"));
        Assert.That(service.Calls, Is.EqualTo(new[] { string.Empty }));
    }

    [Test]
    public async Task Activate_ShowsFailureMessageAndEmptyState()
    {
        var service = new FakeConfigService
        {
            Result = OperationResult<ModConfigInstanceModel>.Fail("读取 mod 配置失败。", "io"),
        };
        var vm = Create(service);

        vm.Activate();
        await WaitUntilAsync(() => vm.HasError);

        Assert.That(vm.Mods, Is.Empty);
        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.ErrorText, Is.EqualTo("读取 mod 配置失败。（io）"));
        Assert.That(vm.StatusText, Is.EqualTo("读取 mod 配置失败。（io）"));
    }

    [Test]
    public async Task Activate_WhenServiceThrows_ReportsErrorInsteadOfCrashing()
    {
        var vm = Create(new ThrowingConfigService());

        vm.Activate();
        await WaitUntilAsync(() => vm.HasError);

        Assert.That(vm.ErrorText, Does.Contain("读取 mod 配置失败："));
        Assert.That(vm.ErrorText, Does.Contain("boom"));
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task Activate_SetsEmptyHintWhenNoAdaptedMod()
    {
        var service = new FakeConfigService
        {
            Result = OperationResult<ModConfigInstanceModel>.Ok(ModConfigTestData.Model()),
        };
        var vm = Create(service);

        vm.Activate();
        await WaitUntilAsync(() => vm.StatusText.Contains("没有已适配", StringComparison.Ordinal));

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.ModCountText, Is.EqualTo("暂无已适配的 mod"));
    }

    private static ModConfigPageViewModel Create(
        IModConfigService service,
        IAdapterCatalog? catalog = null,
        Func<string, string>? translate = null)
        => new(service, catalog, translate);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("等待页面加载超时");
    }

    /// <summary>返回固定结果的配置服务替身。</summary>
    private sealed class FakeConfigService : IModConfigService
    {
        public OperationResult<ModConfigInstanceModel> Result { get; set; } =
            OperationResult<ModConfigInstanceModel>.Ok(ModConfigTestData.Model());

        public IReadOnlyList<string> Calls => _calls;

        private readonly List<string> _calls = [];

        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(string instanceName, CancellationToken cancellationToken = default)
        {
            _calls.Add(instanceName);
            return Task.FromResult(Result);
        }
    }

    /// <summary>抛异常的配置服务替身（验证页面兜底）。</summary>
    private sealed class ThrowingConfigService : IModConfigService
    {
        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(string instanceName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    /// <summary>适配器目录替身。</summary>
    private sealed class FakeCatalog : IAdapterCatalog
    {
        public IReadOnlyList<ModConfigAdapterDescription> Adapters { get; set; } = [];

        public IReadOnlyList<ModConfigDiagnostic> Diagnostics => [];

        public int ReloadCount { get; private set; }

        public void Reload() => ReloadCount++;
    }
}
