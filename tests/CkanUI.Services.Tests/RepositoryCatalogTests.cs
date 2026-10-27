using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CKAN;
using CkanUI.Services.Repositories;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="RepositoryCatalog"/> 的离线用例（06 篇 UI-06）：校验、重编号、优先级交换、投影与失败语义。
/// 全部走假存储，不联网、不碰真实注册表。
/// </summary>
[TestFixture]
public sealed class RepositoryCatalogTests
{
    private FakeRepositoryStore _store = null!;
    private RepositoryCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeRepositoryStore();
        _catalog = new RepositoryCatalog(_store);
    }

    [Test]
    public void Load_EmptyStore_ReturnsEmptyList()
    {
        var result = _catalog.Load();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
        Assert.That(_catalog.Snapshot, Is.Empty);
    }

    [Test]
    public void Load_ProjectsRowsSortedByPriority()
    {
        _store.Seed(("B", "https://example.com/b/repositories.json", 1),
                    ("A", "https://example.com/a/repositories.json", 0));

        var result = _catalog.Load();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(result.Value![0].Priority, Is.EqualTo(0));
        Assert.That(result.Value![1].Priority, Is.EqualTo(1));
    }

    [Test]
    public void Load_MarksOfficialDefaultRepository()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        _store.DefaultRepositoryName = "A";

        var rows = _catalog.Load().Value!;

        Assert.That(rows[0].IsDefault, Is.True);
    }

    [Test]
    public void Load_StaleAfterThreeDays_VeryStaleAfterFourteen()
    {
        _store.Seed(("Fresh", "https://example.com/1.json", 0),
                    ("Stale", "https://example.com/2.json", 1),
                    ("VeryStale", "https://example.com/3.json", 2));
        _store.Ages["Stale"] = TimeSpan.FromDays(5);
        _store.Ages["VeryStale"] = TimeSpan.FromDays(20);

        var rows = _catalog.Load().Value!;

        Assert.That(rows[0].IsStale, Is.False);
        Assert.That(rows[0].IsVeryStale, Is.False);
        Assert.That(rows[0].LastUpdatedAt, Is.Null);
        Assert.That(rows[1].IsStale, Is.True);
        Assert.That(rows[1].IsVeryStale, Is.False);
        Assert.That(rows[1].LastUpdatedAt, Is.Not.Null);
        Assert.That(rows[2].IsStale, Is.True);
        Assert.That(rows[2].IsVeryStale, Is.True);
    }

    [Test]
    public void Add_TrimsNameAndUri_AndAppendsAtLowestPriority()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));

        var result = _catalog.Add("  B  ", "  https://example.com/b.json  ");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(result.Value![1].Uri, Is.EqualTo("https://example.com/b.json"));
        Assert.That(result.Value![1].Priority, Is.EqualTo(1));
        Assert.That(_store.WriteCount, Is.EqualTo(1));
    }

    [TestCase(null)]
    [TestCase("   ")]
    public void Add_EmptyName_FailsWithoutRetry(string? name)
    {
        var result = _catalog.Add(name, "https://example.com/a.json");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("仓库名称不能为空。"));
        Assert.That(result.Retryable, Is.False);
        Assert.That(_store.WriteCount, Is.Zero);
    }

    [TestCase(null)]
    [TestCase("")]
    public void Add_EmptyUri_FailsWithoutRetry(string? uri)
    {
        var result = _catalog.Add("A", uri);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("仓库地址不能为空。"));
        Assert.That(result.Retryable, Is.False);
    }

    [TestCase("ftp://example.com/a.json")]
    [TestCase("not-a-uri")]
    [TestCase("/local/path/repositories.json")]
    public void Add_NonHttpUri_FailsWithoutRetry(string uri)
    {
        var result = _catalog.Add("A", uri);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("仓库地址必须是 http 或 https 开头的完整链接。"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public void Add_DuplicateNameIgnoringCase_Fails()
    {
        _store.Seed(("Mirror", "https://example.com/a.json", 0));

        var result = _catalog.Add("mirror", "https://example.com/b.json");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("已存在同名仓库"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public void Add_DuplicateUri_Fails()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));

        var result = _catalog.Add("B", "https://example.com/a.json");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("该仓库地址已存在"));
    }

    [Test]
    public void Remove_LastRepository_Fails()
    {
        _store.Seed(("Only", "https://example.com/a.json", 0));

        var result = _catalog.Remove("Only");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("至少要保留一个仓库，不能删除最后一个。"));
        Assert.That(result.Retryable, Is.False);
        Assert.That(_store.WriteCount, Is.Zero);
    }

    [Test]
    public void Remove_UnknownName_Fails()
    {
        _store.Seed(("A", "https://example.com/a.json", 0), ("B", "https://example.com/b.json", 1));

        var result = _catalog.Remove("Missing");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有找到名为"));
        Assert.That(_store.WriteCount, Is.Zero);
    }

    [Test]
    public void Remove_RenumbersRemainingPriorities()
    {
        _store.Seed(("A", "https://example.com/a.json", 0),
                    ("B", "https://example.com/b.json", 1),
                    ("C", "https://example.com/c.json", 2));

        var result = _catalog.Remove("A");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "B", "C" }));
        Assert.That(result.Value!.Select(row => row.Priority), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Move_DownSwapsWithNeighbour()
    {
        _store.Seed(("A", "https://example.com/a.json", 0), ("B", "https://example.com/b.json", 1));

        var result = _catalog.Move("A", 1);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "B", "A" }));
        Assert.That(result.Value![0].Priority, Is.EqualTo(0));
        Assert.That(result.Value![1].Priority, Is.EqualTo(1));
    }

    [Test]
    public void Move_UpSwapsWithNeighbour()
    {
        _store.Seed(("A", "https://example.com/a.json", 0), ("B", "https://example.com/b.json", 1));

        var result = _catalog.Move("B", -1);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "B", "A" }));
    }

    [Test]
    public void Move_OutOfRange_SucceedsWithoutWriting()
    {
        _store.Seed(("A", "https://example.com/a.json", 0), ("B", "https://example.com/b.json", 1));

        var up = _catalog.Move("A", -1);
        var down = _catalog.Move("B", 1);
        var zero = _catalog.Move("A", 0);

        Assert.That(up.IsSuccess, Is.True);
        Assert.That(down.IsSuccess, Is.True);
        Assert.That(zero.IsSuccess, Is.True);
        Assert.That(_store.WriteCount, Is.Zero);
    }

    [Test]
    public void Move_UnknownName_Fails()
    {
        _store.Seed(("A", "https://example.com/a.json", 0), ("B", "https://example.com/b.json", 1));

        var result = _catalog.Move("Missing", 1);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有找到名为"));
    }

    [Test]
    public void RestoreDefaults_WithoutNetwork_FailsRetryable()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        _store.OfficialDefaults = null;

        var result = _catalog.RestoreDefaults();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("无法获取官方仓库列表，请检查网络后重试。"));
        Assert.That(result.Retryable, Is.True);
        Assert.That(_store.WriteCount, Is.Zero);
    }

    [Test]
    public void RestoreDefaults_ReplacesWholeList()
    {
        _store.Seed(("Custom", "https://example.com/custom.json", 0));
        _store.OfficialDefaults = new[]
        {
            new RepositoryEntry("X", "https://example.com/x.json", 0),
            new RepositoryEntry("Y", "https://example.com/y.json", 1),
        };

        var result = _catalog.RestoreDefaults();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "X", "Y" }));
    }

    [Test]
    public void EditOperations_WithoutInstance_FailWithoutRetry()
    {
        _store.IsAvailable = false;

        var add = _catalog.Add("A", "https://example.com/a.json");
        var remove = _catalog.Remove("A");
        var move = _catalog.Move("A", 1);
        var restore = _catalog.RestoreDefaults();

        foreach (var result in new[] { add, remove, move, restore })
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.EqualTo("还没有选择游戏实例，请先选择一个实例再编辑仓库列表。"));
            Assert.That(result.Retryable, Is.False);
        }

        Assert.That(_catalog.Load().IsSuccess, Is.True, "读取在无实例时应返回空列表而不是错误。");
    }

    [Test]
    public void MarkFailure_ProjectsFailureOntoRow_AndClearRemovesIt()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        _catalog.Load();

        _catalog.MarkFailure("A", "无法连接服务器。");
        var failed = _catalog.Load().Value!;

        Assert.That(failed[0].HasFailure, Is.True);
        Assert.That(failed[0].FailureMessage, Is.EqualTo("无法连接服务器。"));

        _catalog.ClearFailure("A");
        var cleared = _catalog.Load().Value!;

        Assert.That(cleared[0].HasFailure, Is.False);
        Assert.That(cleared[0].FailureMessage, Is.Null);
    }

    [Test]
    public void Write_WhenStoreThrows_ReturnsRetryableFailure()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        _store.WriteFailure = new IOException("磁盘只读");

        var result = _catalog.Add("B", "https://example.com/b.json");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("保存仓库列表失败，修改未生效。"));
        Assert.That(result.Detail, Is.EqualTo("磁盘只读"));
        Assert.That(result.Retryable, Is.True);
    }
}

/// <summary>离线假存储：只用内存列表模拟注册表，可注入过期时长、官方默认列表与写盘异常。</summary>
internal sealed class FakeRepositoryStore : IRepositoryStore
{
    private readonly List<RepositoryEntry> _entries = new();

    public bool IsAvailable { get; set; } = true;

    public string? DefaultRepositoryName { get; set; }

    public IReadOnlyList<RepositoryEntry>? OfficialDefaults { get; set; }

    public RepositoryUpdateOutcome NextOutcome { get; set; } = RepositoryUpdateOutcome.Updated;

    public Exception? WriteFailure { get; set; }

    public int WriteCount { get; private set; }

    public int RefreshCount { get; private set; }

    public Dictionary<string, TimeSpan> Ages { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<IReadOnlyList<RepositoryEntry>> Writes { get; } = new List<IReadOnlyList<RepositoryEntry>>();

    public void Seed(params (string Name, string Uri, int Priority)[] entries)
    {
        _entries.Clear();
        foreach (var entry in entries)
        {
            _entries.Add(new RepositoryEntry(entry.Name, entry.Uri, entry.Priority));
        }
    }

    public IReadOnlyList<RepositoryEntry> Read() => _entries.ToArray();

    public void Write(IReadOnlyList<RepositoryEntry> entries)
    {
        WriteCount++;
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        _entries.Clear();
        _entries.AddRange(entries);
        ((List<IReadOnlyList<RepositoryEntry>>)Writes).Add(entries.ToArray());
    }

    public IReadOnlyList<RepositoryEntry>? ReadOfficialDefaults() => OfficialDefaults;

    public RepositoryUpdateOutcome Refresh(string name)
    {
        RefreshCount++;
        return NextOutcome;
    }

    public TimeSpan? AgeOf(string name) => Ages.TryGetValue(name, out var age) ? age : null;

    public Repository? Find(string name)
        => _entries.Any(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            ? new Repository(name, "https://example.com/placeholder.json")
            : null;
}
