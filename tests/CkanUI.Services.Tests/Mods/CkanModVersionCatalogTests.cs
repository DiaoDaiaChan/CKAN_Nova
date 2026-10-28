using CKAN;
using CKAN.Versioning;
using CkanUI.Services.Mods;
using NUnit.Framework;

namespace CkanUI.Services.Tests.Mods;

/// <summary>
/// F-30 版本目录投影规则的单元测试。
/// </summary>
/// <remarks>
/// 只测 <see cref="CkanModVersionCatalog.Project"/> 这一层纯函数：它不碰注册表、不碰磁盘，
/// 直接把 <c>CkanModule</c> 喂进来就能验证排序、已装标记、兼容性、不稳定标记与下载元数据映射。
/// 真正读注册表的那条路径依赖游戏实例，本机没有 KSP 安装，因此不做集成断言。
/// </remarks>
[TestFixture]
public sealed class CkanModVersionCatalogTests
{
    private static readonly GameVersionCriteria Criteria = new(GameVersion.Parse("1.12.3"));

    [Test]
    public void Project_SortsByVersionDescending()
    {
        var modules = new[]
        {
            Module(version: "1.0.2"),
            Module(version: "1.10.0"),
            Module(version: "1.2.0"),
        };

        var options = CkanModVersionCatalog.Project(modules, null, Criteria);

        Assert.That(options.Select(option => option.Version), Is.EqualTo(new[] { "1.10.0", "1.2.0", "1.0.2" }));
    }

    [Test]
    public void Project_NullModules_ReturnsEmpty()
    {
        Assert.That(CkanModVersionCatalog.Project(null, null, Criteria), Is.Empty);
    }

    [Test]
    public void Project_EmptyModules_ReturnsEmpty()
    {
        Assert.That(CkanModVersionCatalog.Project(Array.Empty<CkanModule>(), null, Criteria), Is.Empty);
    }

    [Test]
    public void Project_MarksTheInstalledVersionOnly()
    {
        var installed = Module(version: "1.2.0");
        var modules = new[] { Module(version: "1.0.2"), installed, Module(version: "1.10.0") };

        var options = CkanModVersionCatalog.Project(modules, installed, Criteria);

        Assert.That(options.Where(option => option.IsInstalled).Select(option => option.Version),
            Is.EqualTo(new[] { "1.2.0" }));
    }

    [Test]
    public void Project_WithoutInstalledModule_MarksNothingInstalled()
    {
        var options = CkanModVersionCatalog.Project(new[] { Module(version: "1.2.0") }, null, Criteria);

        Assert.That(options.Single().IsInstalled, Is.False);
    }

    [Test]
    public void Project_FlagsNonStableReleaseStatusAsUnstable()
    {
        var modules = new[]
        {
            Module(version: "2.0.0", releaseStatus: "development"),
            Module(version: "1.5.0", releaseStatus: "testing"),
            Module(version: "1.0.0", releaseStatus: "stable"),
        };

        var options = CkanModVersionCatalog.Project(modules, null, Criteria);

        Assert.That(options.Select(option => option.IsUnstable), Is.EqualTo(new[] { true, true, false }));
    }

    [Test]
    public void Project_MapsDownloadMetadata()
    {
        var module = Module(
            version: "1.2.0",
            releaseDate: "2023-01-02T03:04:05Z",
            downloadSize: 4096,
            downloadUrl: "https://example.invalid/ckan/mod-1.2.0.zip");

        var option = CkanModVersionCatalog.Project(new[] { module }, null, Criteria).Single();

        Assert.Multiple(() =>
        {
            Assert.That(option.DownloadSizeBytes, Is.EqualTo(4096));
            Assert.That(option.DownloadUrl, Is.EqualTo("https://example.invalid/ckan/mod-1.2.0.zip"));
            Assert.That(option.ReleaseDate, Is.EqualTo(new DateTimeOffset(2023, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        });
    }

    [Test]
    public void Project_WithoutDownloadSizeOrUrl_LeavesThemNull()
    {
        var option = CkanModVersionCatalog.Project(new[] { Module(version: "1.2.0") }, null, Criteria).Single();

        Assert.Multiple(() =>
        {
            Assert.That(option.DownloadSizeBytes, Is.Null);
            Assert.That(option.DownloadUrl, Is.Null);
            Assert.That(option.ReleaseDate, Is.Null);
        });
    }

    [Test]
    public void Project_DescribesExactGameVersion()
    {
        var option = CkanModVersionCatalog.Project(new[] { Module(version: "1.2.0", kspVersion: "1.12.3") }, null, Criteria)
            .Single();

        Assert.That(option.GameCompatibility, Is.EqualTo("KSP 1.12.3"));
    }

    [Test]
    public void Project_DescribesGameVersionRangeAndOpenEnds()
    {
        var range = Module(version: "3.0.0", kspVersion: null, kspMin: "1.10.0", kspMax: "1.12.3");
        var minOnly = Module(version: "2.0.0", kspVersion: null, kspMin: "1.10.0");
        var maxOnly = Module(version: "1.0.0", kspVersion: null, kspMax: "1.12.3");

        var options = CkanModVersionCatalog.Project(new[] { range, minOnly, maxOnly }, null, Criteria);

        Assert.That(options.Select(option => option.GameCompatibility),
            Is.EqualTo(new[] { "KSP 1.10.0 – 1.12.3", "KSP ≥ 1.10.0", "KSP ≤ 1.12.3" }));
    }

    [Test]
    public void Project_WithoutAnyGameVersion_LeavesCompatibilityTextNull()
    {
        var option = CkanModVersionCatalog.Project(
            new[] { Module(version: "1.0.0", kspVersion: null) }, null, Criteria).Single();

        Assert.That(option.GameCompatibility, Is.Null);
    }

    [Test]
    public void Project_TakesCompatibilityFromEachModule()
    {
        var compatible = Module(version: "2.0.0");
        var incompatible = Module(version: "1.0.0", comparator: new StubComparator("test-mod"));

        var options = CkanModVersionCatalog.Project(new[] { compatible, incompatible }, null, Criteria);

        Assert.That(options.Select(option => option.IsCompatible), Is.EqualTo(new[] { true, false }));
    }

    private static CkanModule Module(
        string version = "1.0.0",
        string? kspVersion = "1.12.3",
        string? kspMin = null,
        string? kspMax = null,
        string? releaseStatus = "stable",
        string? releaseDate = null,
        long? downloadSize = null,
        string? downloadUrl = null,
        IGameComparator? comparator = null)
    {
        var json = new System.Text.StringBuilder();
        json.Append("{\"spec_version\":\"v1.4\",\"identifier\":\"test-mod\",\"name\":\"Test Mod\",");
        json.Append("\"abstract\":\"仅供测试\",\"author\":[\"tester\"],\"license\":[\"MIT\"],");
        json.Append("\"version\":\"").Append(version).Append('\"');
        if (kspVersion is not null)
        {
            json.Append(",\"ksp_version\":\"").Append(kspVersion).Append('\"');
        }

        if (kspMin is not null)
        {
            json.Append(",\"ksp_version_min\":\"").Append(kspMin).Append('\"');
        }

        if (kspMax is not null)
        {
            json.Append(",\"ksp_version_max\":\"").Append(kspMax).Append('\"');
        }

        if (releaseStatus is not null)
        {
            json.Append(",\"release_status\":\"").Append(releaseStatus).Append('\"');
        }

        if (releaseDate is not null)
        {
            json.Append(",\"release_date\":\"").Append(releaseDate).Append('\"');
        }

        if (downloadSize is not null)
        {
            json.Append(",\"download_size\":").Append(downloadSize.Value);
        }

        if (downloadUrl is not null)
        {
            json.Append(",\"download\":\"").Append(downloadUrl).Append('\"');
        }
        else
        {
            // package 类型必须声明 download（CkanModule.CheckHealth 强制），没有下载地址的版本只能是元包。
            json.Append(",\"kind\":\"metapackage\"");
        }

        json.Append('}');
        return new CkanModule(json.ToString(), comparator ?? new StubComparator());
    }

    /// <summary>最小比较器替身：把指定标识符判为不兼容，其余一律兼容（不依赖 Core 的 ServiceLocator）。</summary>
    private sealed class StubComparator : IGameComparator
    {
        private readonly string? _incompatibleIdentifier;

        public StubComparator(string? incompatibleIdentifier = null)
            => _incompatibleIdentifier = incompatibleIdentifier;

        public bool Compatible(GameVersionCriteria gameVersion, CkanModule module)
            => !string.Equals(module.identifier, _incompatibleIdentifier, StringComparison.Ordinal);
    }
}
