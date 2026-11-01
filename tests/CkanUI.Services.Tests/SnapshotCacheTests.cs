using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Repositories;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 模组快照磁盘缓存用例（docs/ops/perf-baseline.md §17）：验证"投影行能原样落盘再读回"、
/// "指纹一变就不命中"、"坏文件不抛异常"，因为这条路径要是出错，表现是用户看不到模组列表。
/// </summary>
[TestFixture]
public sealed class SnapshotCacheTests
{
    [Test]
    public void SaveThenLoad_RoundTripsEveryProjectedField()
    {
        using var fixture = new Fixture();
        var rows = new[]
        {
            Row("ModuleA", "第一个模组", installed: true, compatible: true, download: 1234,
                facts: Facts(@"描述 A", ["MIT"], ["zh-CN"], ["dep.x"]), tags: ["图形", "部件"]),
            Row("ModuleB", "第二个模组", installed: false, compatible: false, download: 0,
                facts: Facts(@"描述 B", null, null, null), tags: []),
        };

        fixture.Cache.Save("key-1", rows);
        var loaded = fixture.Cache.TryLoad("key-1");

        Assert.That(loaded, Is.Not.Null, "同一指纹应命中");
        Assert.That(loaded!.Count, Is.EqualTo(2), "行数必须一致");

        // 记录结构体的相等性是引用比较列表字段，所以逐字段核对内容（JSON 往返会把数组变成 List<string>）。
        Assert.That(loaded[0].Identifier, Is.EqualTo("ModuleA"));
        Assert.That(loaded[0].Name, Is.EqualTo("第一个模组"));
        Assert.That(loaded[0].Author, Is.EqualTo("作者"));
        Assert.That(loaded[0].InstalledVersion, Is.EqualTo("1.0.0"));
        Assert.That(loaded[0].LatestVersion, Is.EqualTo("2.0.0"));
        Assert.That(loaded[0].DownloadCount, Is.EqualTo(1234));
        Assert.That(loaded[0].Tags, Is.EquivalentTo(new[] { "图形", "部件" }));
        Assert.That(loaded[0].SizeBytes, Is.EqualTo(1024));
        Assert.That(loaded[0].IsInstalled, Is.True);
        Assert.That(loaded[0].HasUpdate, Is.True);
        Assert.That(loaded[0].IsCompatible, Is.True);
        Assert.That(loaded[0].Facts.Abstract, Is.EqualTo("描述 A"));
        Assert.That(loaded[0].Facts.Licenses, Is.EquivalentTo(new[] { "MIT" }));
        Assert.That(loaded[0].Facts.Localizations, Is.EquivalentTo(new[] { "zh-CN" }));
        Assert.That(loaded[0].Facts.DependsOn, Is.EquivalentTo(new[] { "dep.x" }));
        Assert.That(loaded[0].Facts.IsCached, Is.True);
        Assert.That(loaded[0].Facts.Homepage, Is.EqualTo("https://example.invalid"));

        Assert.That(loaded[1].Identifier, Is.EqualTo("ModuleB"));
        Assert.That(loaded[1].Tags, Is.Empty);
        Assert.That(loaded[1].IsCompatible, Is.False);
        Assert.That(loaded[1].Facts.Licenses, Is.Null, "null 列表必须回来还是 null");
    }

    [Test]
    public void TryLoad_DifferentKey_ReturnsNull()
    {
        using var fixture = new Fixture();
        fixture.Cache.Save("key-1", [Row("ModuleA", "A", false, true, 1, default, [])]);

        Assert.That(fixture.Cache.TryLoad("key-2"), Is.Null, "指纹不同不能命中，否则会把过期行当最新");
    }

    [Test]
    public void Save_EmptyRows_WritesNothing()
    {
        using var fixture = new Fixture();
        fixture.Cache.Save("key-1", []);

        Assert.That(fixture.Cache.TryLoad("key-1"), Is.Null, "空列表无意义，不落盘也不命中");
        Assert.That(Directory.GetFiles(fixture.Directory), Is.Empty);
    }

    [Test]
    public void TryLoad_CorruptFile_ReturnsNullInsteadOfThrowing()
    {
        using var fixture = new Fixture();
        fixture.Cache.Save("key-1", [Row("ModuleA", "A", false, true, 1, default, [])]);
        var file = Directory.GetFiles(fixture.Directory).Single();
        File.WriteAllText(file, "{ this is not json");

        Assert.That(fixture.Cache.TryLoad("key-1"), Is.Null, "坏文件只能退回解析，不能把异常抛给启动序列");
    }

    [Test]
    public void KeyFrom_IsOrderSensitiveAndStable()
    {
        var first = SnapshotCache.KeyFrom(["a", "b"]);
        var second = SnapshotCache.KeyFrom(["a", "b"]);
        var reordered = SnapshotCache.KeyFrom(["b", "a"]);

        Assert.That(first, Is.EqualTo(second), "同样的输入必须得到同样的指纹");
        Assert.That(first, Is.Not.EqualTo(reordered), "顺序变了就是另一份输入");
    }

    [Test]
    public void StampFile_MissingFile_IsMarkedInsteadOfThrowing()
    {
        using var fixture = new Fixture();
        var stamp = SnapshotCache.StampFile(Path.Combine(fixture.Directory, "nope.json"));

        Assert.That(stamp, Does.Contain("missing"), "文件不在时要给出可比较的标记，而不是抛异常");
    }

    [Test]
    public void StampFile_ChangesWhenFileIsRewritten()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Directory, "index.json");
        File.WriteAllText(path, "{}");
        var before = SnapshotCache.StampFile(path);
        File.WriteAllText(path, "{\"a\":1}");

        Assert.That(SnapshotCache.StampFile(path), Is.Not.EqualTo(before), "索引被重写后指纹必须变化");
    }

    private static ModRowData Row(string identifier,
                                  string name,
                                  bool installed,
                                  bool compatible,
                                  long download,
                                  ModSearchFacts facts,
                                  string[] tags) => new(
        Identifier: identifier,
        Name: name,
        Author: "作者",
        InstalledVersion: installed ? "1.0.0" : null,
        LatestVersion: "2.0.0",
        DownloadCount: download,
        Tags: tags,
        SizeBytes: 1024,
        IsInstalled: installed,
        HasUpdate: installed,
        IsCompatible: compatible,
        IsAutoInstalled: false,
        IsSelected: false,
        IsImplicit: false)
    {
        Facts = facts,
    };

    private static ModSearchFacts Facts(string description,
                                        string[]? licenses,
                                        string[]? localizations,
                                        string[]? dependsOn) => new()
    {
        Abstract = description,
        Description = description + "（详细）",
        Licenses = licenses,
        Localizations = localizations,
        DependsOn = dependsOn,
        Homepage = "https://example.invalid",
        IsCached = licenses is not null,
        IsNew = false,
        IsReplaceable = false,
    };

    private sealed class Fixture : IDisposable
    {
        private readonly TempWorkspace _workspace = new("snapshot-cache");

        public Fixture()
        {
            Directory = _workspace.Path;
            Cache = new SnapshotCache(Directory);
        }

        public string Directory { get; }

        public SnapshotCache Cache { get; }

        public void Dispose()
        {
            _workspace.Dispose();
        }
    }
}
