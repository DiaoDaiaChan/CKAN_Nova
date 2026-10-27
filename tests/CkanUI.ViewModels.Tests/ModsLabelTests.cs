using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// F-24 标签组在模组页的接线：筛选（多选为"或"）、详情标记、收藏/隐藏/锁定命令。
/// 服务侧一律用 <see cref="FakeLabelService"/>——本沙箱造不出合法的 KSP 实例，
/// 也不该让视图模型测试去碰 labels.json。
/// </summary>
[TestFixture]
public sealed class ModsLabelTests
{
    private FakeRepositoryService _repositories = null!;
    private FakeRegistryService _registry = null!;
    private FakeInstanceService _instances = null!;
    private RecordingErrorReporter _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _repositories = new FakeRepositoryService();
        _registry = new FakeRegistryService();
        _instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        _errors = new RecordingErrorReporter();
    }

    private ModsPageViewModel CreateViewModel(ILabelService? labels = null) =>
        new(_repositories, _registry, _instances, _errors, labels: labels)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };

    private void SeedLabelledRows()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("Alpha", "甲", facts: new ModSearchFacts { Labels = new[] { "Favourites" } }),
            ModsRowFactory.Create("Bravo", "乙"),
            ModsRowFactory.Create("Charlie", "丙", facts: new ModSearchFacts { Labels = new[] { "Hidden" } }));
    }

    [Test]
    public void WithoutService_LabelUiStaysHidden()
    {
        SeedLabelledRows();
        var vm = CreateViewModel();

        vm.Activate();

        Assert.That(vm.HasLabelService, Is.False, "未接线时筛选区不应出现标签开关");
        Assert.That(vm.LabelFilters, Is.Empty);
        Assert.That(vm.HasLabelFilter, Is.False);
        Assert.That(vm.DetailLabels, Is.Empty);
        Assert.That(vm.CanToggleLabels, Is.False);
        Assert.That(vm.Rows, Has.Count.EqualTo(3), "没有标签服务时列表照常可用");
    }

    [Test]
    public void Chips_AreProjectedFromService()
    {
        SeedLabelledRows();
        var labels = new FakeLabelService(
            LabelInfo.Named("Favourites") with { ModuleCount = 2, ColorHex = "#90EE90" },
            LabelInfo.Named("Hidden"),
            LabelInfo.Named("自建组"));
        var vm = CreateViewModel(labels);

        vm.Activate();

        Assert.That(vm.HasLabelService, Is.True);
        Assert.That(vm.LabelFilters.Select(chip => chip.Name),
            Is.EqualTo(new[] { "Favourites", "Hidden", "自建组" }));
        Assert.That(vm.LabelFilters[0].DisplayText, Is.EqualTo("Favourites（2）"), "有成员时带上计数");
        Assert.That(vm.LabelFilters[1].DisplayText, Is.EqualTo("Hidden"));
        Assert.That(vm.LabelFilters[0].HasColor, Is.True);
        Assert.That(vm.LabelFilters[0].StateText, Is.EqualTo("未筛选"));
    }

    [Test]
    public void ToggleLabelFilter_KeepsOnlyRowsWithThatLabel()
    {
        SeedLabelledRows();
        var vm = CreateViewModel(new FakeLabelService(LabelInfo.Named("Favourites"), LabelInfo.Named("Hidden")));
        vm.Activate();

        vm.ToggleLabelFilter("Favourites");

        Assert.That(vm.HasLabelFilter, Is.True);
        Assert.That(vm.SelectedLabels, Is.EqualTo(new[] { "Favourites" }));
        Assert.That(vm.Rows.Select(row => row.Identifier), Is.EqualTo(new[] { "Alpha" }));
        Assert.That(vm.LabelFilters.First(chip => chip.Name == "Favourites").IsActive, Is.True);
        Assert.That(vm.LabelFilters.First(chip => chip.Name == "Favourites").StateText, Is.EqualTo("已筛选"));
    }

    [Test]
    public void MultipleLabels_MatchAnyOfThem()
    {
        SeedLabelledRows();
        var vm = CreateViewModel(new FakeLabelService(LabelInfo.Named("Favourites"), LabelInfo.Named("Hidden")));
        vm.Activate();

        vm.ToggleLabelFilter("Favourites");
        vm.ToggleLabelFilter("Hidden");

        Assert.That(vm.SelectedLabels, Is.EqualTo(new[] { "Favourites", "Hidden" }), "选中集合按名称排序，便于断言与显示");
        Assert.That(vm.Rows.Select(row => row.Identifier),
            Is.EquivalentTo(new[] { "Alpha", "Charlie" }),
            "多选之间是或关系");
    }

    [Test]
    public void TogglingSameLabelTwice_RestoresAllRows()
    {
        SeedLabelledRows();
        var vm = CreateViewModel(new FakeLabelService(LabelInfo.Named("Favourites")));
        vm.Activate();

        vm.ToggleLabelFilter("Favourites");
        Assert.That(vm.Rows, Has.Count.EqualTo(1));

        vm.ToggleLabelFilter("Favourites");

        Assert.That(vm.HasLabelFilter, Is.False);
        Assert.That(vm.Rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void ResetLabelFiltersCommand_ClearsSelection()
    {
        SeedLabelledRows();
        var vm = CreateViewModel(new FakeLabelService(LabelInfo.Named("Favourites")));
        vm.Activate();

        vm.ToggleLabelFilter("Favourites");
        Assert.That(vm.ResetLabelFiltersCommand.CanExecute(null), Is.True);

        vm.ResetLabelFiltersCommand.Execute(null);

        Assert.That(vm.HasLabelFilter, Is.False);
        Assert.That(vm.SelectedLabels, Is.Empty);
        Assert.That(vm.Rows, Has.Count.EqualTo(3));
        Assert.That(vm.ResetLabelFiltersCommand.CanExecute(null), Is.False, "没有筛选时按钮应禁用");
    }

    [Test]
    public void LabelFilter_ShowsUpInActiveFilterChips_AndClearFiltersResetsIt()
    {
        SeedLabelledRows();
        var vm = CreateViewModel(new FakeLabelService(LabelInfo.Named("Favourites")));
        vm.Activate();

        vm.ToggleLabelFilter("Favourites");

        Assert.That(vm.ActiveFilterChips, Has.Some.Contains("Favourites"));
        Assert.That(vm.LabelFilterSummary, Does.Contain("Favourites"));

        vm.ClearFilters();

        Assert.That(vm.HasLabelFilter, Is.False);
        Assert.That(vm.LabelFilterSummary, Is.EqualTo("未按标签组过滤"));
        Assert.That(vm.ActiveFilterChips, Is.Empty);
        Assert.That(vm.Rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void FilterWithoutMatch_ShowsEmptyState()
    {
        SeedLabelledRows();
        var vm = CreateViewModel(new FakeLabelService(LabelInfo.Named("自建组")));
        vm.Activate();

        vm.ToggleLabelFilter("自建组");

        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.ShowEmptyState, Is.True);
    }

    [Test]
    public void DetailLabels_ExposeNameAndColor()
    {
        SeedLabelledRows();
        var labels = new FakeLabelService(LabelInfo.Named("Favourites") with { ColorHex = "#90EE90" });
        var vm = CreateViewModel(labels);
        vm.Activate();
        vm.SelectedRow = vm.Rows.First(row => row.Identifier == "Alpha");

        Assert.That(vm.HasDetailLabels, Is.True);
        Assert.That(vm.DetailLabelText, Is.EqualTo("Favourites"));
        Assert.That(vm.DetailLabels, Has.Count.EqualTo(1));
        Assert.That(vm.DetailLabels[0].Name, Is.EqualTo("Favourites"));
        Assert.That(vm.DetailLabels[0].ColorHex, Is.EqualTo("#90EE90"));
        Assert.That(vm.DetailLabels[0].HasColor, Is.True);
        Assert.That(vm.CanToggleLabels, Is.True);

        vm.SelectedRow = vm.Rows.First(row => row.Identifier == "Bravo");

        Assert.That(vm.HasDetailLabels, Is.False);
        Assert.That(vm.DetailLabelText, Is.EqualTo("无标签组"), "无标记时给明确提示，不留空白");
    }

    [Test]
    public async Task ToggleFavouriteCommand_WritesThroughAndReportsStatus()
    {
        SeedLabelledRows();
        var labels = new FakeLabelService(LabelInfo.Named("Favourites"));
        var vm = CreateViewModel(labels);
        vm.Activate();
        vm.SelectedRow = vm.Rows.First(row => row.Identifier == "Bravo");

        await vm.ToggleFavouriteCommand.ExecuteAsync(null);

        Assert.That(labels.FavouriteCalls, Has.Count.EqualTo(1));
        Assert.That(labels.FavouriteCalls[0], Is.EqualTo(("Bravo", true)));
        Assert.That(vm.StatusText, Does.Contain("收藏"));
        Assert.That(_errors.Recent, Is.Empty, "成功路径不该进错误栏");
    }

    [Test]
    public async Task ToggleHiddenCommand_SurfacesServiceFailure()
    {
        SeedLabelledRows();
        var labels = new FakeLabelService(LabelInfo.Named("Hidden"))
        {
            WriteResult = OperationResult<bool>.Fail("标签文件保存失败，请检查磁盘权限。"),
        };
        var vm = CreateViewModel(labels);
        vm.Activate();
        vm.SelectedRow = vm.Rows.First(row => row.Identifier == "Alpha");

        await vm.ToggleHiddenCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Is.EqualTo("标签文件保存失败，请检查磁盘权限。"), "失败原因要原样显示，不能吞掉");
        Assert.That(labels.HiddenCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ToggleHeldCommand_WithoutSelection_DoesNothing()
    {
        SeedLabelledRows();
        var labels = new FakeLabelService(LabelInfo.Named("Held"));
        var vm = CreateViewModel(labels);
        vm.Activate();

        Assert.That(vm.CanToggleLabels, Is.False);
        Assert.That(vm.ToggleHeldCommand.CanExecute(null), Is.False);

        await vm.ToggleHeldCommand.ExecuteAsync(null);

        Assert.That(labels.HeldCalls, Is.Empty);
    }

    [Test]
    public void LabelsChangedEvent_RebuildsChips()
    {
        SeedLabelledRows();
        var labels = new FakeLabelService(LabelInfo.Named("Favourites"));
        var vm = CreateViewModel(labels);
        vm.Activate();
        Assert.That(vm.LabelFilters, Has.Count.EqualTo(1));

        labels.Add(LabelInfo.Named("新增组"));
        labels.RaiseChanged();

        Assert.That(vm.LabelFilters, Has.Count.EqualTo(2), "服务推送整表快照后应立即重建开关");
        Assert.That(vm.LabelFilters.Any(chip => chip.Name == "新增组"), Is.True);
    }

    [Test]
    public void ServiceThrowingOnRead_DegradesToEmptyChipsAndReportsError()
    {
        SeedLabelledRows();
        var vm = CreateViewModel(new FakeLabelService { ThrowOnRead = true });

        Assert.DoesNotThrow(() => vm.Activate());

        Assert.That(vm.LabelFilters, Is.Empty);
        Assert.That(_errors.Recent, Is.Not.Empty, "读标签失败要进错误栏，而不是崩在绑定里");
        Assert.That(vm.Rows, Has.Count.EqualTo(3), "标签不可用不应连带拖垮列表");
    }
}

/// <summary>假标签服务：只实现视图模型真正用到的那几个成员，写操作可注入失败。</summary>
internal sealed class FakeLabelService : ILabelService
{
    private readonly List<LabelInfo> _labels;
    private readonly HashSet<string> _favourites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);

    public FakeLabelService(params LabelInfo[] labels) => _labels = new List<LabelInfo>(labels);

    public IReadOnlyList<LabelInfo> Labels => ThrowOnRead ? throw new InvalidOperationException("标签文件不可读") : _labels;

    public event Action<IReadOnlyList<LabelInfo>>? LabelsChanged;

    public bool HasActiveInstance { get; set; } = true;

    public string? InstanceName { get; set; } = "测试实例";

    /// <summary>读标签组时抛异常（验证视图模型的降级路径）。</summary>
    public bool ThrowOnRead { get; set; }

    /// <summary>写操作返回值（注入失败用）。</summary>
    public OperationResult<bool> WriteResult { get; set; } = OperationResult<bool>.Ok(true);

    public List<(string Identifier, bool On)> FavouriteCalls { get; } = new();

    public List<(string Identifier, bool On)> HiddenCalls { get; } = new();

    public List<(string Identifier, bool On)> HeldCalls { get; } = new();

    public IReadOnlyCollection<string> HeldIdentifiers => Array.Empty<string>();

    public IReadOnlyCollection<string> IgnoreMissingIdentifiers => Array.Empty<string>();

    public void Add(LabelInfo info) => _labels.Add(info);

    public void RaiseChanged() => LabelsChanged?.Invoke(_labels.ToArray());

    public Task<OperationResult<IReadOnlyList<LabelInfo>>> RefreshAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<LabelInfo>>.Ok(Labels));

    public IReadOnlyList<string> GetLabelsFor(string identifier) => Array.Empty<string>();

    public Task<OperationResult<bool>> SetLabelsAsync(
        string identifier,
        IReadOnlyCollection<string> labels,
        CancellationToken ct = default)
        => Task.FromResult(WriteResult);

    public Task<OperationResult<LabelInfo>> CreateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
        => Task.FromResult(OperationResult<LabelInfo>.Ok(LabelInfo.Named(request.Name)));

    public Task<OperationResult<LabelInfo>> UpdateLabelAsync(LabelEditRequest request, CancellationToken ct = default)
        => Task.FromResult(OperationResult<LabelInfo>.Ok(LabelInfo.Named(request.Name)));

    public Task<OperationResult<bool>> DeleteLabelAsync(string name, CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Ok(true));

    public bool IsHidden(string identifier) => _hidden.Contains(identifier);

    public bool IsFavourite(string identifier) => _favourites.Contains(identifier);

    public bool IsHeld(string identifier) => _held.Contains(identifier);

    public Task<OperationResult<bool>> SetFavouriteAsync(string identifier, bool favourite, CancellationToken ct = default)
        => Write(FavouriteCalls, _favourites, identifier, favourite);

    public Task<OperationResult<bool>> SetHiddenAsync(string identifier, bool hidden, CancellationToken ct = default)
        => Write(HiddenCalls, _hidden, identifier, hidden);

    public Task<OperationResult<bool>> SetHeldAsync(string identifier, bool held, CancellationToken ct = default)
        => Write(HeldCalls, _held, identifier, held);

    /// <summary>记录调用；只有写成功才改成员关系（与真实服务一致）。</summary>
    private Task<OperationResult<bool>> Write(
        List<(string Identifier, bool On)> calls,
        HashSet<string> members,
        string identifier,
        bool on)
    {
        calls.Add((identifier, on));
        if (WriteResult.IsSuccess)
        {
            if (on)
            {
                members.Add(identifier);
            }
            else
            {
                members.Remove(identifier);
            }
        }

        return Task.FromResult(WriteResult);
    }
}
