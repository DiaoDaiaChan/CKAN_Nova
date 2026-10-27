using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>仓库快照 + 注册表快照 → 列表行（04 篇 §2.1 / VM-01）。</summary>
[TestFixture]
public sealed class ModsProjectionTests
{
    [Test]
    public void NullSnapshots_ProduceEmptyList()
    {
        Assert.That(ModsRowProjection.Merge(null, null), Is.Empty);
    }

    [Test]
    public void RepositoryRow_GetsInstalledMarkFromRegistry()
    {
        var repository = ModsRowFactory.Repository(ModsRowFactory.Create("A", "模组 A", latestVersion: "2.0.0"));
        var registry = ModsRowFactory.Registry(ModsRowFactory.Installed("A", "1.0.0"));

        var rows = ModsRowProjection.Merge(repository, registry);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].IsInstalled, Is.True);
        Assert.That(rows[0].InstalledVersion, Is.EqualTo("1.0.0"));
        Assert.That(rows[0].HasUpdate, Is.True);
    }

    [Test]
    public void InstalledModuleOutsideRepository_IsAppended()
    {
        var repository = ModsRowFactory.Repository(ModsRowFactory.Create("A", "模组 A"));
        var registry = ModsRowFactory.Registry(
            ModsRowFactory.Installed("A", "1.0.0"),
            ModsRowFactory.Installed("LocalOnly", "0.1.0", auto: true));

        var rows = ModsRowProjection.Merge(repository, registry);

        Assert.That(rows, Has.Count.EqualTo(2));
        var orphan = rows[1];
        Assert.That(orphan.Identifier, Is.EqualTo("LocalOnly"));
        Assert.That(orphan.Name, Is.EqualTo("LocalOnly"));
        Assert.That(orphan.IsInstalled, Is.True);
        Assert.That(orphan.IsAutoInstalled, Is.True);
        Assert.That(orphan.IsCompatible, Is.True);
    }

    [Test]
    public void VersionComparison_IsNumericNotLexicographic()
    {
        Assert.That(ModsVersionComparer.IsNewer("1.10.0", "1.9.0"), Is.True);
        Assert.That(ModsVersionComparer.IsNewer("1.9.0", "1.10.0"), Is.False);
        Assert.That(ModsVersionComparer.IsNewer("2.0.0", null), Is.False);
    }
}
