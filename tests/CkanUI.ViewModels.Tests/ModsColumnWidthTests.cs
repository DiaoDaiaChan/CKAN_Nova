using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 模组页列宽持久化（06 篇 UI-02「列定义 / 列宽记忆」、03 篇 §3.1）：
/// 恢复、合并、防抖落盘、脏数据与失败降级全部在 VM 层，可 headless 验证。
/// </summary>
[TestFixture]
public sealed class ModsColumnWidthTests
{
    private FakeRepositoryService _repositories = null!;
    private FakeRegistryService _registry = null!;
    private FakeInstanceService _instances = null!;
    private RecordingErrorReporter _errors = null!;
    private FakeUiSettingsStore _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _repositories = new FakeRepositoryService();
        _registry = new FakeRegistryService();
        _instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        _errors = new RecordingErrorReporter();
        _settings = new FakeUiSettingsStore();
    }

    private ModsPageViewModel CreateViewModel(IModsDelayScheduler? scheduler = null, IUiSettingsStore? store = null) =>
        new(_repositories, _registry, _instances, _errors, null, store ?? _settings)
        {
            Scheduler = scheduler ?? new ImmediateDelayScheduler(),
        };

    [Test]
    public void DefaultColumnWidths_CoverAllEightColumns()
    {
        Assert.That(ModsPageViewModel.DefaultColumnWidths, Has.Count.EqualTo(8));
        Assert.That(ModsPageViewModel.DefaultColumnWidths["select"], Is.EqualTo(32));
        Assert.That(ModsPageViewModel.DefaultColumnWidths["name"], Is.EqualTo(260));
    }

    [Test]
    public void Restore_WhenStoreHasNoWidths_UsesDefaults()
    {
        var vm = CreateViewModel();

        var widths = vm.RestoreColumnWidths();

        Assert.That(widths, Has.Count.EqualTo(ModsPageViewModel.DefaultColumnWidths.Count));
        Assert.That(widths["name"], Is.EqualTo(260));
        Assert.That(vm.ColumnWidths["name"], Is.EqualTo(260));
    }

    [Test]
    public void Restore_MergesStoredWidthsOverDefaults()
    {
        _settings.With(new UiSettings { ColumnWidths = new Dictionary<string, double> { ["name"] = 320, ["author"] = 150 } });
        var vm = CreateViewModel();

        var widths = vm.RestoreColumnWidths();

        Assert.That(widths["name"], Is.EqualTo(320));
        Assert.That(widths["author"], Is.EqualTo(150));
        Assert.That(widths["installed"], Is.EqualTo(90), "未存过的列用默认值补齐");
    }

    [Test]
    public void Restore_IgnoresDirtyStoredWidths()
    {
        _settings.With(new UiSettings
        {
            ColumnWidths = new Dictionary<string, double>
            {
                ["name"] = double.NaN,
                ["author"] = -5,
                ["tags"] = 9999,
            },
        });
        var vm = CreateViewModel();

        var widths = vm.RestoreColumnWidths();

        Assert.That(widths["name"], Is.EqualTo(260));
        Assert.That(widths["author"], Is.EqualTo(140));
        Assert.That(widths["tags"], Is.EqualTo(200));
    }

    [Test]
    public void Restore_WhenStoreThrows_FallsBackToDefaultsAndReports()
    {
        var vm = CreateViewModel(store: new ThrowingUiSettingsStore());

        var widths = vm.RestoreColumnWidths();

        Assert.That(widths["name"], Is.EqualTo(260));
        Assert.That(_errors.Recent, Has.Count.EqualTo(1));
    }

    [Test]
    public void Restore_WhenStoreIsNull_ReturnsDefaults()
    {
        var vm = new ModsPageViewModel(_repositories, _registry, _instances, _errors)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };

        var widths = vm.RestoreColumnWidths();

        Assert.That(widths, Has.Count.EqualTo(ModsPageViewModel.DefaultColumnWidths.Count));
        Assert.That(vm.ColumnWidths["select"], Is.EqualTo(32));
    }

    [Test]
    public void Activate_RestoresStoredColumnWidths()
    {
        _settings.With(new UiSettings { ColumnWidths = new Dictionary<string, double> { ["name"] = 333 } });
        var vm = CreateViewModel();

        vm.Activate();

        Assert.That(vm.ColumnWidths["name"], Is.EqualTo(333));
    }

    [Test]
    public async Task Save_MergesWithExistingStoredWidths()
    {
        _settings.With(new UiSettings { ColumnWidths = new Dictionary<string, double> { ["name"] = 300 } });
        var vm = CreateViewModel();

        vm.SaveColumnWidths(new Dictionary<string, double> { ["downloads"] = 95 });
        await vm.PendingColumnWidthSave;

        Assert.That(_settings.Saved, Has.Count.EqualTo(1));
        var saved = _settings.Saved[0].ColumnWidths;
        Assert.That(saved["name"], Is.EqualTo(300), "旧列宽不能被覆盖掉");
        Assert.That(saved["downloads"], Is.EqualTo(95));
        Assert.That(vm.ColumnWidths["downloads"], Is.EqualTo(95));
    }

    [Test]
    public void Save_UsesDebounceOf300Milliseconds()
    {
        var scheduler = new ControlledDelayScheduler();
        var vm = CreateViewModel(scheduler);

        vm.SaveColumnWidths(new Dictionary<string, double> { ["name"] = 300 });

        Assert.That(scheduler.Requested, Has.Count.EqualTo(1));
        Assert.That(
            scheduler.Requested[0],
            Is.EqualTo(TimeSpan.FromMilliseconds(ModsPageViewModel.ColumnWidthSaveDebounceMilliseconds)));
        Assert.That(ModsPageViewModel.ColumnWidthSaveDebounceMilliseconds, Is.LessThanOrEqualTo(1000));
    }

    [Test]
    public async Task Save_Debounce_WritesOnlyOnceForRapidChanges()
    {
        var scheduler = new ControlledDelayScheduler();
        var vm = CreateViewModel(scheduler);

        vm.SaveColumnWidths(new Dictionary<string, double> { ["name"] = 300 });
        vm.SaveColumnWidths(new Dictionary<string, double> { ["name"] = 310 });
        vm.SaveColumnWidths(new Dictionary<string, double> { ["name"] = 320 });
        scheduler.ReleaseAll();
        await vm.PendingColumnWidthSave;

        Assert.That(_settings.Saved, Has.Count.EqualTo(1), "连续拖拽只落一次盘");
        Assert.That(_settings.Saved[0].ColumnWidths["name"], Is.EqualTo(320), "落盘的是最后一次的值");
    }

    [Test]
    public async Task Save_IgnoresEmptyOrDirtyInput()
    {
        var vm = CreateViewModel();

        vm.SaveColumnWidths(new Dictionary<string, double>());
        await vm.PendingColumnWidthSave;
        vm.SaveColumnWidths(new Dictionary<string, double> { ["name"] = double.NaN, [""] = 100 });
        await vm.PendingColumnWidthSave;

        Assert.That(_settings.Saved, Is.Empty);
    }

    [Test]
    public async Task Save_WhenStoreSaveFails_ReportsAndKeepsState()
    {
        _settings.SaveResult = OperationResult<bool>.Fail("设置文件只读");
        var vm = CreateViewModel();

        vm.SaveColumnWidths(new Dictionary<string, double> { ["name"] = 400 });
        await vm.PendingColumnWidthSave;

        Assert.That(_settings.Saved, Is.Empty);
        Assert.That(_errors.Recent, Has.Count.EqualTo(1));
        Assert.That(vm.ColumnWidths["name"], Is.EqualTo(260), "落盘失败不改内存列宽");
    }

    [Test]
    public void Persist_WhenStoreThrows_ReportsAndReturnsFalse()
    {
        var vm = CreateViewModel(store: new ThrowingUiSettingsStore());

        var ok = vm.PersistColumnWidths(new Dictionary<string, double> { ["name"] = 300 });

        Assert.That(ok, Is.False);
        Assert.That(_errors.Recent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Save_WhenStoreIsNull_IsNoOp()
    {
        var vm = new ModsPageViewModel(_repositories, _registry, _instances, _errors)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };

        vm.SaveColumnWidths(new Dictionary<string, double> { ["name"] = 300 });
        await vm.PendingColumnWidthSave;

        Assert.That(vm.PersistColumnWidths(new Dictionary<string, double> { ["name"] = 300 }), Is.False);
        Assert.That(_errors.Recent, Is.Empty, "缺设置服务属于正常降级，不算错误");
    }

    /// <summary>测试替身：设置读写全部抛异常（验证 05 篇的异常收口）。</summary>
    private sealed class ThrowingUiSettingsStore : IUiSettingsStore
    {
        public UiSettings Current => throw new InvalidOperationException("设置文件损坏");

        public UiSettings Load() => throw new InvalidOperationException("设置文件损坏");

        public event Action<UiSettings>? Changed
        {
            add { }
            remove { }
        }

        public OperationResult<bool> Save(UiSettings settings) => throw new InvalidOperationException("磁盘只读");
    }
}
