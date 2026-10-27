using CKAN.Versioning;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Models;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 单元测试：ModListProjection 纯函数对拍（MOD-04 / 04 篇 §4、§5）。
/// 对拍点：
/// 1. 注册表与可用仓库聚合为 ModRowData 集合。
/// 2. IsInstalled 判定与版本匹配。
/// 3. HasUpdate 判定与版本比较对拍。
/// 4. IsCompatible 判定对拍。
/// 5. IsAutoInstalled 字段提取。
/// </summary>
[TestFixture]
public sealed class ModListProjectionTests
{
    [Test]
    public void Project_EmptyInputs_ReturnsEmptyList()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>();
        var repo = RepositorySnapshot.Empty;

        var result = ModListProjection.Project(installed, repo);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Project_AvailableMod_ProjectsCorrectly()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>();
        var availableRow = new ModRowData(
            Identifier: "TestMod",
            Name: "Test Mod",
            Author: "Tester",
            InstalledVersion: null,
            LatestVersion: "1.2.0",
            DownloadCount: 100,
            Tags: new[] { "tag1" },
            SizeBytes: 1024,
            IsInstalled: false,
            HasUpdate: false,
            IsCompatible: true,
            IsAutoInstalled: false,
            IsSelected: false,
            IsImplicit: false
        );

        var repo = new RepositorySnapshot(
            new[] { availableRow },
            new[] { "default" },
            DateTime.UtcNow,
            false
        );

        var result = ModListProjection.Project(installed, repo);

        Assert.That(result, Has.Count.EqualTo(1));
        var row = result[0];
        Assert.That(row.Identifier, Is.EqualTo("TestMod"));
        Assert.That(row.Name, Is.EqualTo("Test Mod"));
        Assert.That(row.Author, Is.EqualTo("Tester"));
        Assert.That(row.LatestVersion, Is.EqualTo("1.2.0"));
        Assert.That(row.InstalledVersion, Is.Null);
        Assert.That(row.IsInstalled, Is.False);
        Assert.That(row.IsCompatible, Is.True);
        Assert.That(row.HasUpdate, Is.False);
        Assert.That(row.SizeBytes, Is.EqualTo(1024));
    }

    [Test]
    public void Project_InstalledAndAvailable_ChecksHasUpdateCorrectly()
    {
        var installedInfo = new InstalledModuleInfo(
            Identifier: "TestMod",
            Version: "1.0.0",
            IsAutoInstalled: true,
            SourceRepository: "default",
            InstallTime: DateTime.UtcNow.AddDays(-10)
        );
        var installed = new Dictionary<string, InstalledModuleInfo>
        {
            ["TestMod"] = installedInfo
        };

        var availableRow = new ModRowData(
            Identifier: "TestMod",
            Name: "Test Mod",
            Author: "Tester",
            InstalledVersion: null,
            LatestVersion: "1.5.0",
            DownloadCount: 100,
            Tags: Array.Empty<string>(),
            SizeBytes: 2048,
            IsInstalled: false,
            HasUpdate: false,
            IsCompatible: true,
            IsAutoInstalled: false,
            IsSelected: false,
            IsImplicit: false
        );

        var repo = new RepositorySnapshot(
            new[] { availableRow },
            new[] { "default" },
            DateTime.UtcNow,
            false
        );

        var result = ModListProjection.Project(installed, repo);

        Assert.That(result, Has.Count.EqualTo(1));
        var row = result[0];
        Assert.That(row.Identifier, Is.EqualTo("TestMod"));
        Assert.That(row.IsInstalled, Is.True);
        Assert.That(row.InstalledVersion, Is.EqualTo("1.0.0"));
        Assert.That(row.LatestVersion, Is.EqualTo("1.5.0"));
        Assert.That(row.HasUpdate, Is.True);
        Assert.That(row.IsAutoInstalled, Is.True);
    }

    [Test]
    public void CheckHasUpdate_WithInvalidOrEqualVersions_ReturnsFalse()
    {
        Assert.That(ModListProjection.CheckHasUpdate(null, "1.0.0"), Is.False);
        Assert.That(ModListProjection.CheckHasUpdate("1.0.0", null), Is.False);
        Assert.That(ModListProjection.CheckHasUpdate("1.0.0", "1.0.0"), Is.False);
        Assert.That(ModListProjection.CheckHasUpdate("2.0.0", "1.0.0"), Is.False);
        Assert.That(ModListProjection.CheckHasUpdate("1.0.0", "2.0.0"), Is.True);
    }
}
