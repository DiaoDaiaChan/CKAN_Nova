using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>过滤 / 排序 / 搜索管线（03 篇 §3.4/§3.5，纯函数）。</summary>
[TestFixture]
public sealed class ModsListPipelineTests
{
    private static readonly ModRowData UpToDate = ModsRowFactory.Create(
        "A", "Alpha", "zoe", installedVersion: "2.0.0", latestVersion: "2.0.0", downloadCount: 10);

    private static readonly ModRowData Outdated = ModsRowFactory.Create(
        "B", "Bravo", "adam", installedVersion: "1.0.0", latestVersion: "1.9.0", downloadCount: 500);

    private static readonly ModRowData NotInstalled = ModsRowFactory.Create(
        "C", "Charlie", "mike", latestVersion: "1.10.0", downloadCount: 50);

    private static readonly ModRowData Incompatible = ModsRowFactory.Create(
        "D", "Delta", "kate", latestVersion: "3.0.0", downloadCount: 7, isCompatible: false);

    private static readonly ModRowData[] All = { UpToDate, Outdated, NotInstalled, Incompatible };

    [Test]
    public void InstallFilter_Upgradeable_ReturnsOnlyOutdatedInstalledRows()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.Upgradeable, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, true);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Identifier, Is.EqualTo("B"));
    }

    [Test]
    public void InstallFilter_NotInstalled_ExcludesInstalledRows()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.NotInstalled, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, true);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.All(row => !row.IsInstalled), Is.True);
    }

    [Test]
    public void CompatibilityFilter_CompatibleOnly_DropsIncompatibleRows()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.All, ModsCompatibilityFilter.CompatibleOnly,
            ModsSortColumn.Name, true);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.All(row => row.IsCompatible), Is.True);
    }

    [Test]
    public void SortByName_RespectsDirection()
    {
        var ascending = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.All, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, true);
        var descending = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.All, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, false);

        Assert.That(ascending[0].Name, Is.EqualTo("Alpha"));
        Assert.That(descending[0].Name, Is.EqualTo("Delta"));
    }

    [Test]
    public void SortByLatestVersion_UsesVersionSemantics()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.All, ModsCompatibilityFilter.All,
            ModsSortColumn.LatestVersion, true);

        var order = new List<string>();
        foreach (var row in rows)
        {
            order.Add(row.LatestVersion ?? string.Empty);
        }

        // 1.10.0 必须排在 1.9.0 之后（按版本而非字符串）。
        Assert.That(order.IndexOf("1.9.0"), Is.LessThan(order.IndexOf("1.10.0")));
    }

    [Test]
    public void SortByDownloadCount_Descending()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.All, ModsCompatibilityFilter.All,
            ModsSortColumn.DownloadCount, false);

        Assert.That(rows[0].Identifier, Is.EqualTo("B"));
    }

    [Test]
    public void QueryAndFilters_AreCombinedWithAnd()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse("tag:plugin @adam"), ModsInstallFilter.Installed, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, true);

        // 测试数据没有标签，两个条件叠加后应为空（验证"与"而不是"或"）。
        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void Apply_DoesNotMutateInput()
    {
        var before = All.Length;
        _ = ModsListPipeline.Apply(
            All, Search.Parse("alpha"), ModsInstallFilter.All, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, true);

        Assert.That(All, Has.Length.EqualTo(before));
        Assert.That(All[0].Identifier, Is.EqualTo("A"));
    }
}
