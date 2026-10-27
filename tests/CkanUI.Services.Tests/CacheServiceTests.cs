using CkanUI.Services.Abstractions;
using CkanUI.Services.Cache;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 下载缓存服务（12 篇 F-50 / F-40、06 篇 UI-19）：上限口径、按修改时间清理、引用条目保护、
/// ZIP 灌入失败、删除不存在的条目。
/// </summary>
/// <remarks>全部用 <see cref="FakeCacheStore"/> 造假存储，不碰真实缓存目录与注册表（04 篇 §3）。</remarks>
[TestFixture]
public sealed class CacheServiceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task GetSnapshotAsync_ComputesTotalsAndUsage()
    {
        var store = new FakeCacheStore { SizeLimitBytes = 1000 }
            .Add("AAAA1111-a.zip", 100, T0)
            .Add("BBBB2222-b.zip", 250, T0.AddDays(1));
        var service = new CacheService(store);

        var result = await service.GetSnapshotAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var snapshot = result.Value!;
        Assert.That(snapshot.EntryCount, Is.EqualTo(2));
        Assert.That(snapshot.TotalBytes, Is.EqualTo(350));
        Assert.That(snapshot.SizeLimitBytes, Is.EqualTo(1000));
        Assert.That(snapshot.HasLimit, Is.True);
        Assert.That(snapshot.UsagePercent, Is.EqualTo(35d));
    }

    [Test]
    public async Task PurgeToLimitAsync_WithNoLimit_DeletesNothing()
    {
        var store = new FakeCacheStore { SizeLimitBytes = null }
            .Add("AAAA1111-a.zip", 100, T0);
        var service = new CacheService(store);

        var result = await service.PurgeToLimitAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.DeletedCount, Is.EqualTo(0));
        Assert.That(store.DeleteOrder, Is.Empty, "未设上限时什么都不该删");
    }

    [Test]
    public async Task PurgeToLimitAsync_TreatsZeroLimitAsUnlimited()
    {
        var store = new FakeCacheStore { SizeLimitBytes = 0 }
            .Add("AAAA1111-a.zip", 100, T0)
            .Add("BBBB2222-b.zip", 100, T0.AddDays(1));
        var service = new CacheService(store);

        var result = await service.PurgeToLimitAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.DeletedCount, Is.EqualTo(0), "上限 0 视为不限制");
        Assert.That(store.Entries.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task PurgeToLimitAsync_DeletesOldestFirstUntilUnderLimit()
    {
        var store = new FakeCacheStore { SizeLimitBytes = 500 }
            .Add("CCCC3333-new.zip", 400, T0.AddDays(2))
            .Add("AAAA1111-old.zip", 400, T0)
            .Add("BBBB2222-mid.zip", 400, T0.AddDays(1));
        var service = new CacheService(store);

        var result = await service.PurgeToLimitAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var purge = result.Value!;
        Assert.That(purge.DeletedFileNames, Is.EqualTo(new[] { "AAAA1111-old.zip", "BBBB2222-mid.zip" }));
        Assert.That(store.DeleteOrder, Is.EqualTo(new[] { "AAAA1111-old.zip", "BBBB2222-mid.zip" }), "从最久未修改的开始删");
        Assert.That(purge.FreedBytes, Is.EqualTo(800));
        Assert.That(store.Entries.Sum(entry => entry.SizeBytes), Is.LessThanOrEqualTo(500), "清理后总量必须不超过上限");
    }

    [Test]
    public async Task PurgeToLimitAsync_SkipsReferencedEntriesAndReportsThem()
    {
        var store = new FakeCacheStore { SizeLimitBytes = 500 }
            .Add("AAAA1111-old.zip", 400, T0, referenced: true)
            .Add("BBBB2222-mid.zip", 400, T0.AddDays(1))
            .Add("CCCC3333-new.zip", 400, T0.AddDays(2));
        var service = new CacheService(store);

        var result = await service.PurgeToLimitAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var purge = result.Value!;
        Assert.That(purge.SkippedFileNames, Is.EqualTo(new[] { "AAAA1111-old.zip" }), "被引用的条目要报告出来");
        Assert.That(purge.DeletedFileNames, Is.EqualTo(new[] { "BBBB2222-mid.zip", "CCCC3333-new.zip" }));
        Assert.That(store.Entries.Select(entry => entry.FileName), Is.EqualTo(new[] { "AAAA1111-old.zip" }));
    }

    [Test]
    public async Task PurgeAllAsync_KeepsReferencedEntries()
    {
        var store = new FakeCacheStore { SizeLimitBytes = 1000 }
            .Add("AAAA1111-a.zip", 100, T0, referenced: true)
            .Add("BBBB2222-b.zip", 200, T0.AddDays(1))
            .Add("CCCC3333-c.zip", 300, T0.AddDays(2));
        var service = new CacheService(store);

        var result = await service.PurgeAllAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var purge = result.Value!;
        Assert.That(purge.DeletedCount, Is.EqualTo(2));
        Assert.That(purge.SkippedCount, Is.EqualTo(1));
        Assert.That(purge.FreedBytes, Is.EqualTo(500));
        Assert.That(store.Entries.Select(entry => entry.FileName), Is.EqualTo(new[] { "AAAA1111-a.zip" }));
    }

    [Test]
    public async Task ImportZipAsync_ReturnsNonRetryableFailureForBadZip()
    {
        var store = new FakeCacheStore
        {
            ImportHandler = _ => OperationResult<CacheFileFact>.Fail(
                "这个文件不是有效的 ZIP 压缩包，无法灌入下载缓存。",
                "NetModuleCache.ZipValid 报告：不是 zip",
                retryable: false),
        };
        var service = new CacheService(store);

        var result = await service.ImportZipAsync("C:/tmp/broken.zip");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False, "坏 ZIP 重试也没用");
        Assert.That(result.Message, Does.Contain("ZIP"));
    }

    [Test]
    public async Task ImportZipAsync_ReturnsEntryOnSuccess()
    {
        var fact = new CacheFileFact("AAAA1111-mod.zip", 1024, T0, "TestMod", "1.0.0");
        var store = new FakeCacheStore { ImportHandler = _ => OperationResult<CacheFileFact>.Ok(fact) };
        var service = new CacheService(store);

        var result = await service.ImportZipAsync("C:/tmp/mod.zip");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.FileName, Is.EqualTo("AAAA1111-mod.zip"));
        Assert.That(result.Value.ModuleLabel, Is.EqualTo("TestMod 1.0.0"));
    }

    [Test]
    public async Task DeleteEntryAsync_ReturnsRetryableFailureWhenEntryMissing()
    {
        var store = new FakeCacheStore();
        var service = new CacheService(store);

        var result = await service.DeleteEntryAsync("AAAA1111-gone.zip");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True, "文件可能是被外部删掉了，重试（刷新后）有意义");
        Assert.That(result.Message, Does.Contain("没有"));
    }

    [Test]
    public async Task DeleteEntryAsync_RefusesReferencedEntry()
    {
        var store = new FakeCacheStore().Add("AAAA1111-a.zip", 100, T0, referenced: true);
        var service = new CacheService(store);

        var result = await service.DeleteEntryAsync("AAAA1111-a.zip");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False, "使用中的条目要等卸载后再说，不是重试能解决的");
        Assert.That(store.Entries.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteEntryAsync_DeletesEntry()
    {
        var store = new FakeCacheStore().Add("AAAA1111-a.zip", 100, T0);
        var service = new CacheService(store);

        var result = await service.DeleteEntryAsync("aaaa1111-A.zip");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(store.Entries, Is.Empty, "文件名大小写不敏感");
    }
}

/// <summary>假的缓存存储：内存里放条目，记录删除顺序与引用状态。</summary>
internal sealed class FakeCacheStore : ICacheStore
{
    /// <summary>缓存目录（服务层不读它，仅供断言）。</summary>
    public string? CacheDirectory { get; set; } = "C:/fake/downloads";

    /// <summary>容量上限；<c>null</c> 表示不限制。</summary>
    public long? SizeLimitBytes { get; set; }

    /// <summary>当前条目。</summary>
    public List<CacheFileFact> Entries { get; } = new();

    /// <summary>被已安装模组引用的文件名。</summary>
    public HashSet<string> Referenced { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>删除顺序（断言「从最久未修改的开始删」）。</summary>
    public List<string> DeleteOrder { get; } = new();

    /// <summary>自定义灌入行为。</summary>
    public Func<string, OperationResult<CacheFileFact>>? ImportHandler { get; set; }

    /// <summary>加一个条目。</summary>
    public FakeCacheStore Add(string fileName, long size, DateTime modified, bool referenced = false)
    {
        Entries.Add(new CacheFileFact(fileName, size, modified, null, null));
        if (referenced)
        {
            Referenced.Add(fileName);
        }

        return this;
    }

    public IReadOnlyList<CacheFileFact> ListEntries() => Entries.ToList();

    public bool IsReferenced(string fileName) => Referenced.Contains(fileName);

    public bool Delete(string fileName)
    {
        var entry = Entries.FirstOrDefault(
            fact => string.Equals(fact.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return false;
        }

        Entries.Remove(entry);
        DeleteOrder.Add(entry.FileName);
        return true;
    }

    public OperationResult<CacheFileFact> Import(string sourceZipPath, CancellationToken ct)
        => ImportHandler?.Invoke(sourceZipPath)
           ?? OperationResult<CacheFileFact>.Fail("测试未配置灌入行为。", null, retryable: false);
}
