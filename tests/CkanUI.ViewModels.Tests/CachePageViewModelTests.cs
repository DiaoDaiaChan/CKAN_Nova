using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Cache;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 下载缓存页 VM（06 篇 UI-19）：快照加载、按钮可用性、清理后刷新、危险操作需确认、失败文案中文。
/// </summary>
[TestFixture]
public sealed class CachePageViewModelTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Activate_LoadsSnapshotAndEntries()
    {
        var service = new FakeCacheService { SizeLimitBytes = 1000 }
            .Add("AAAA1111-a.zip", 100, T0, "ModA", "1.0")
            .Add("BBBB2222-b.zip", 250, T0.AddDays(1), null, null);
        var vm = Create(service);

        vm.Activate();
        await WaitForAsync(() => vm.EntryCount == 2);

        Assert.That(vm.TotalBytes, Is.EqualTo(350));
        Assert.That(vm.TotalText, Is.EqualTo("350 B"));
        Assert.That(vm.LimitText, Is.EqualTo("1000 B"));
        Assert.That(vm.UsageText, Is.EqualTo("35%"));
        Assert.That(vm.Entries.Select(row => row.ModuleLabel), Is.EqualTo(new[] { "ModA 1.0", "未知模块" }));
        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.ShowEmptyState, Is.False);
    }

    [Test]
    public async Task PurgeButtons_DisabledWhenCacheIsEmpty()
    {
        var vm = Create(new FakeCacheService());
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.EntryCount, Is.EqualTo(0));
        Assert.That(vm.CanPurge, Is.False);
        Assert.That(vm.PurgeToLimitCommand.CanExecute(null), Is.False);
        Assert.That(vm.PurgeAllCommand.CanExecute(null), Is.False);
        Assert.That(vm.DeleteSelectedCommand.CanExecute(null), Is.False);
        Assert.That(vm.ImportZipCommand.CanExecute(null), Is.True, "空缓存也要能灌 ZIP");
        Assert.That(vm.ShowEmptyState, Is.True);
    }

    [Test]
    public async Task PurgeToLimit_RefreshesAfterClearing()
    {
        var service = new FakeCacheService { SizeLimitBytes = 500 }
            .Add("AAAA1111-a.zip", 400, T0)
            .Add("BBBB2222-b.zip", 400, T0.AddDays(1));
        service.PurgeResult = OperationResult<CachePurgeResult>.Ok(
            new CachePurgeResult(new[] { "AAAA1111-a.zip" }, 400, Array.Empty<string>()));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);
        var loadsBefore = service.SnapshotCalls;

        await vm.PurgeToLimitCommand.ExecuteAsync(null);

        Assert.That(service.PurgeToLimitCalls, Is.EqualTo(1));
        Assert.That(service.SnapshotCalls, Is.GreaterThan(loadsBefore), "清理后要重新读一遍统计");
        Assert.That(vm.StatusText, Does.Contain("已按上限清理"));
        Assert.That(vm.StatusText, Does.Contain("释放"));
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task PurgeAll_RequiresConfirmation()
    {
        var service = new FakeCacheService().Add("AAAA1111-a.zip", 400, T0);
        var dialogs = new FakeDialogService { ConfirmAnswer = false };
        var vm = Create(service, dialogs: dialogs);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.PurgeAllCommand.ExecuteAsync(null);

        Assert.That(dialogs.ConfirmTitles, Is.EqualTo(new[] { "清空下载缓存" }));
        Assert.That(service.PurgeAllCalls, Is.EqualTo(0), "用户取消后不能真的清空");
        Assert.That(vm.StatusText, Is.EqualTo("已取消。"));
        Assert.That(vm.EntryCount, Is.EqualTo(1));
    }

    [Test]
    public async Task PurgeAll_ReportsSkippedReferencedEntries()
    {
        var service = new FakeCacheService().Add("AAAA1111-a.zip", 400, T0);
        service.PurgeResult = OperationResult<CachePurgeResult>.Ok(
            new CachePurgeResult(Array.Empty<string>(), 0, new[] { "AAAA1111-a.zip" }));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.PurgeAllCommand.ExecuteAsync(null);

        Assert.That(service.PurgeAllCalls, Is.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("跳过 1 个"));
    }

    [Test]
    public async Task DeleteSelected_RequiresConfirmationAndDeletesRows()
    {
        var service = new FakeCacheService()
            .Add("AAAA1111-a.zip", 100, T0)
            .Add("BBBB2222-b.zip", 200, T0.AddDays(1));
        var dialogs = new FakeDialogService { ConfirmAnswer = true };
        var vm = Create(service, dialogs: dialogs);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Entries[0].IsSelected = true;
        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.DeleteSelectedCommand.CanExecute(null), Is.True);

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(dialogs.ConfirmTitles, Is.EqualTo(new[] { "删除缓存条目" }));
        Assert.That(service.DeletedNames, Is.EqualTo(new[] { "AAAA1111-a.zip" }));
        Assert.That(vm.StatusText, Does.Contain("已删除 1 个条目"));
    }

    [Test]
    public async Task PurgeFailure_ShowsChineseErrorText()
    {
        var service = new FakeCacheService().Add("AAAA1111-a.zip", 400, T0);
        service.PurgeResult = OperationResult<CachePurgeResult>.Fail(
            "按上限清理下载缓存失败。",
            "IOException: 磁盘被占用",
            retryable: true);
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.PurgeToLimitCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("清理"));
        Assert.That(vm.ErrorText, Does.Contain("可重试"));
        Assert.That(vm.ErrorDetail, Does.Contain("IOException"));
        vm.ClearErrorCommand.Execute(null);
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task ImportZip_WithoutPicker_ShowsChineseError()
    {
        var vm = Create(new FakeCacheService());
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.ImportZipCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("文件选择"));
    }

    [Test]
    public async Task ImportZip_ImportsAndRefreshes()
    {
        var service = new FakeCacheService();
        service.ImportResult = OperationResult<CacheEntryInfo>.Ok(
            new CacheEntryInfo("AAAA1111-mod.zip", 2048, T0, "TestMod", "1.0.0", false));
        var vm = Create(service);
        vm.ZipPicker = new FakeZipPicker("C:/tmp/mod.zip");
        await vm.RefreshCommand.ExecuteAsync(null);
        var loadsBefore = service.SnapshotCalls;

        await vm.ImportZipCommand.ExecuteAsync(null);

        Assert.That(service.ImportCalls, Is.EqualTo(1));
        Assert.That(service.LastImportedPath, Is.EqualTo("C:/tmp/mod.zip"));
        Assert.That(service.SnapshotCalls, Is.GreaterThan(loadsBefore));
        Assert.That(vm.StatusText, Does.Contain("已灌入"));
        Assert.That(vm.StatusText, Does.Contain("TestMod 1.0.0"));
    }

    [Test]
    public async Task ImportZip_NonRetryableFailure_ShowsNonRetryableText()
    {
        var service = new FakeCacheService();
        service.ImportResult = OperationResult<CacheEntryInfo>.Fail(
            "这个文件不是有效的 ZIP 压缩包，无法灌入下载缓存。",
            "不是 zip",
            retryable: false);
        var vm = Create(service);
        vm.ZipPicker = new FakeZipPicker("C:/tmp/broken.zip");
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.ImportZipCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("ZIP"));
        Assert.That(vm.ErrorText, Does.Contain("不可重试"));
    }

    [Test]
    public async Task ImportZip_CancelledPicker_KeepsCacheUntouched()
    {
        var service = new FakeCacheService();
        var vm = Create(service);
        vm.ZipPicker = new FakeZipPicker(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.ImportZipCommand.ExecuteAsync(null);

        Assert.That(service.ImportCalls, Is.EqualTo(0));
        Assert.That(vm.StatusText, Is.EqualTo("已取消。"));
    }

    private static CachePageViewModel Create(
        FakeCacheService service,
        FakeErrorReporter? errors = null,
        FakeDialogService? dialogs = null)
        => new(service, errors ?? new FakeErrorReporter(), dialogs ?? new FakeDialogService());

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("等待条件超时。");
    }
}

/// <summary>假的缓存服务：内存条目 + 可编排的失败路径 + 调用计数。</summary>
internal sealed class FakeCacheService : ICacheService
{
    /// <summary>当前条目。</summary>
    public List<CacheEntryInfo> Entries { get; } = new();

    /// <summary>容量上限。</summary>
    public long? SizeLimitBytes { get; set; }

    /// <summary>覆盖快照结果（验证失败路径）。</summary>
    public OperationResult<CacheSnapshot>? SnapshotResult { get; set; }

    /// <summary>清理结果（两个清理命令共用）。</summary>
    public OperationResult<CachePurgeResult>? PurgeResult { get; set; }

    /// <summary>灌入结果。</summary>
    public OperationResult<CacheEntryInfo>? ImportResult { get; set; }

    /// <summary>删除结果；<c>null</c> 表示成功。</summary>
    public OperationResult? DeleteResult { get; set; }

    /// <summary>GetSnapshotAsync 调用次数。</summary>
    public int SnapshotCalls { get; private set; }

    /// <summary>PurgeToLimitAsync 调用次数。</summary>
    public int PurgeToLimitCalls { get; private set; }

    /// <summary>PurgeAllAsync 调用次数。</summary>
    public int PurgeAllCalls { get; private set; }

    /// <summary>ImportZipAsync 调用次数。</summary>
    public int ImportCalls { get; private set; }

    /// <summary>最后一次灌入的源路径。</summary>
    public string? LastImportedPath { get; private set; }

    /// <summary>被删除的文件名（按调用顺序）。</summary>
    public List<string> DeletedNames { get; } = new();

    /// <summary>加一个条目。</summary>
    public FakeCacheService Add(
        string fileName,
        long size,
        DateTime modified,
        string? identifier = null,
        string? version = null)
    {
        Entries.Add(new CacheEntryInfo(fileName, size, modified, identifier, version, false));
        return this;
    }

    public Task<OperationResult<CacheSnapshot>> GetSnapshotAsync(CancellationToken ct = default)
    {
        SnapshotCalls++;
        return Task.FromResult(SnapshotResult ?? OperationResult<CacheSnapshot>.Ok(
            new CacheSnapshot(Entries.Count, Entries.Sum(entry => entry.SizeBytes), SizeLimitBytes)));
    }

    public Task<OperationResult<IReadOnlyList<CacheEntryInfo>>> ListEntriesAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<CacheEntryInfo>>.Ok(Entries.ToList()));

    public Task<OperationResult<CachePurgeResult>> PurgeToLimitAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        PurgeToLimitCalls++;
        if (PurgeResult is { IsSuccess: true } ok)
        {
            Entries.RemoveAll(entry => ok.Value!.DeletedFileNames.Contains(entry.FileName));
        }

        return Task.FromResult(PurgeResult ?? OperationResult<CachePurgeResult>.Ok(
            new CachePurgeResult(Array.Empty<string>(), 0, Array.Empty<string>())));
    }

    public Task<OperationResult<CachePurgeResult>> PurgeAllAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        PurgeAllCalls++;
        if (PurgeResult is { IsSuccess: true } ok)
        {
            Entries.RemoveAll(entry => ok.Value!.DeletedFileNames.Contains(entry.FileName));
        }

        return Task.FromResult(PurgeResult ?? OperationResult<CachePurgeResult>.Ok(
            new CachePurgeResult(Array.Empty<string>(), 0, Array.Empty<string>())));
    }

    public Task<OperationResult<CacheEntryInfo>> ImportZipAsync(string sourceZipPath, CancellationToken ct = default)
    {
        ImportCalls++;
        LastImportedPath = sourceZipPath;
        return Task.FromResult(ImportResult ?? OperationResult<CacheEntryInfo>.Fail(
            "测试未配置灌入结果。", null, retryable: false));
    }

    public Task<OperationResult> DeleteEntryAsync(string fileName, CancellationToken ct = default)
    {
        DeletedNames.Add(fileName);
        if (DeleteResult is { IsSuccess: false } failed)
        {
            return Task.FromResult(failed);
        }

        Entries.RemoveAll(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(DeleteResult ?? OperationResult.Ok());
    }
}

/// <summary>假的对话框服务：记录确认标题，答案可编排。</summary>
internal sealed class FakeDialogService : IDialogService
{
    /// <summary>ConfirmAsync 的答案。</summary>
    public bool ConfirmAnswer { get; set; } = true;

    /// <summary>确认过的标题（按调用顺序）。</summary>
    public List<string> ConfirmTitles { get; } = new();

    public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
    {
        ConfirmTitles.Add(title);
        return Task.FromResult(ConfirmAnswer);
    }

    public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
        => Task.FromResult(defaultIndex);

    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

    public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

    public Task<OperationResult<bool>> RunWithProgressAsync(string title, Func<IProgress<double>, CancellationToken, Task> work)
        => Task.FromResult(OperationResult<bool>.Ok(true));
}

/// <summary>假的错误上报：只记录，不抛。</summary>
internal sealed class FakeErrorReporter : IErrorReporter
{
    /// <summary>上报记录。</summary>
    public List<string> Contexts { get; } = new();

    public event Action<ErrorEntry>? Reported
    {
        add { }
        remove { }
    }

    public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();

    public void Report(Exception exception, string context, string? userMessage = null) => Contexts.Add(context);

    public void Report(string context, string userMessage, string? detail = null) => Contexts.Add(context);
}

/// <summary>假的 ZIP 选择器：直接返回预设路径。</summary>
internal sealed class FakeZipPicker : ICacheZipPicker
{
    private readonly string? _path;

    public FakeZipPicker(string? path) => _path = path;

    public Task<string?> PickZipFileAsync(CancellationToken ct = default) => Task.FromResult(_path);
}
