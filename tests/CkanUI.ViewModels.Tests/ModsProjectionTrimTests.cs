using CkanUI.TestSupport;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 模组页的行投影释放（18 篇 §3 L1）：启动游戏前把整张表放掉，回来自动重建，
/// 而「勾选」这类用户输入必须原样留着——那是待变更集，不是缓存。
/// </summary>
[TestFixture]
public sealed class ModsProjectionTrimTests
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

    private ModsPageViewModel CreatePage()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("Alpha", installedVersion: "1.0.0"),
            ModsRowFactory.Create("Beta"));

        return new ModsPageViewModel(_repositories, _registry, _instances, _errors)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };
    }

    [Test]
    public void TrimProjection_ReleasesRowsAndKeepsTheSelection()
    {
        var page = CreatePage();
        page.ReloadFromSnapshots();
        Assert.That(page.Rows.Count, Is.EqualTo(2));
        page.ToggleSelection("Alpha");

        var released = page.TrimProjection();

        Assert.That(released, Is.EqualTo(4), "Rows 与 _all 各两条");
        Assert.That(page.Rows, Is.Empty);
        Assert.That(page.SelectedCount, Is.EqualTo(1), "勾选是待变更集的输入，释放不能替用户撤销");
        Assert.That(page.PageName, Is.EqualTo("模组"));

        // 快照还在，重建只走投影，不发请求。
        page.ReloadFromSnapshots();
        Assert.That(page.Rows.Count, Is.EqualTo(2));
        Assert.That(_repositories.RefreshCount, Is.EqualTo(0));
    }

    [Test]
    public void TrimProjection_WithoutRows_ReleasesNothing()
    {
        var page = CreatePage();

        Assert.That(page.TrimProjection(), Is.EqualTo(0));
    }

    [Test]
    public void TrimProjection_WhileThePageIsOnScreen_ReleasesNothing()
    {
        var page = CreatePage();
        page.ReloadFromSnapshots();
        page.Activate();

        var released = page.TrimProjection();

        Assert.That(released, Is.EqualTo(0), "正在显示的页面不释放投影");
        Assert.That(page.Rows.Count, Is.EqualTo(2), "否则用户眼前就是一张空表，状态栏还挂着「载入完成」");

        page.Deactivate();
        Assert.That(page.TrimProjection(), Is.EqualTo(4), "离开页面后照常释放");
    }
}

