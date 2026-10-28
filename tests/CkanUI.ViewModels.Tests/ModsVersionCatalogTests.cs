using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Mods;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// F-30「全部版本」页签的数据接线：DTO 投影、面板回填、页面异步查询与取消。
/// </summary>
[TestFixture]
public sealed class ModsVersionCatalogTests
{
    private static ModVersionOption Option(
        string version,
        bool isInstalled = false,
        bool isCompatible = true,
        bool isUnstable = false)
        => new(
            Version: version,
            GameCompatibility: "KSP 1.12.3",
            ReleaseDate: new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero),
            DownloadSizeBytes: 2048,
            DownloadUrl: "https://example.invalid/ckan/mod-" + version + ".zip",
            IsInstalled: isInstalled,
            IsCompatible: isCompatible,
            IsUnstable: isUnstable);

    // ---- 投影：ModVersionOption → ModVersionItemDto ----

    [Test]
    public void BuildVersions_MapsEveryField()
    {
        var dto = ModDetailsSnapshotBuilder.BuildVersions([Option("1.2.0", isInstalled: true, isUnstable: true)]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version, Is.EqualTo("1.2.0"));
            Assert.That(dto.GameCompatibility, Is.EqualTo("KSP 1.12.3"));
            Assert.That(dto.ReleaseDate, Is.EqualTo(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero)));
            Assert.That(dto.DownloadSizeBytes, Is.EqualTo(2048));
            Assert.That(dto.DownloadUrl, Is.EqualTo("https://example.invalid/ckan/mod-1.2.0.zip"));
            Assert.That(dto.IsInstalled, Is.True);
            Assert.That(dto.IsCompatible, Is.True);
            Assert.That(dto.IsUnstable, Is.True);
        });
    }

    [Test]
    public void BuildVersions_KeepsCatalogOrder()
    {
        var versions = ModDetailsSnapshotBuilder.BuildVersions([Option("2.0.0"), Option("1.10.0"), Option("1.2.0")]);

        Assert.That(versions.Select(version => version.Version), Is.EqualTo(new[] { "2.0.0", "1.10.0", "1.2.0" }));
    }

    [Test]
    public void BuildVersions_NullInput_ReturnsEmpty()
    {
        Assert.That(ModDetailsSnapshotBuilder.BuildVersions(null), Is.Empty);
    }

    [Test]
    public void BuildVersions_SkipsEntriesWithoutVersion()
    {
        var versions = ModDetailsSnapshotBuilder.BuildVersions([Option("1.0.0"), new ModVersionOption("  "), null!]);

        Assert.That(versions.Select(version => version.Version), Is.EqualTo(new[] { "1.0.0" }));
    }

    // ---- 面板回填：ModDetailsViewModel.ApplyVersions ----

    [Test]
    public void ApplyVersions_WithoutSnapshot_DoesNothing()
    {
        var details = CreateDetails();

        Assert.DoesNotThrow(() => details.ApplyVersions([new ModVersionItemDto("1.0.0")]));
        Assert.That(details.HasVersions, Is.False);
    }

    [Test]
    public void ApplyVersions_FillsVersionsTab()
    {
        var details = CreateDetails();
        details.ShowDetails(Snapshot());

        details.ApplyVersions(ModDetailsSnapshotBuilder.BuildVersions([Option("1.0.0"), Option("2.0.0")]));

        Assert.Multiple(() =>
        {
            Assert.That(details.HasVersions, Is.True);
            Assert.That(details.VersionsTabHeaderText, Does.Contain("2"));
            Assert.That(details.VersionsCountText, Does.Contain("2"));
        });
    }

    [Test]
    public void ApplyVersions_EmptyList_ReturnsToEmptyState()
    {
        var details = CreateDetails();
        details.ShowDetails(Snapshot());
        details.ApplyVersions(ModDetailsSnapshotBuilder.BuildVersions([Option("1.0.0")]));

        details.ApplyVersions(Array.Empty<ModVersionItemDto>());

        Assert.That(details.HasVersions, Is.False);
    }

    // ---- 页面：选中行触发查询 ----

    [Test]
    public async Task SelectingRow_LoadsVersionsIntoDetails()
    {
        var catalog = new RecordingVersionCatalog(Option("1.0.0"), Option("1.2.0"));
        var page = CreatePage(catalog);

        Select(page, "Alpha");
        await PumpAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.Details!.HasVersions, Is.True);
            Assert.That(page.Details.Versions.Select(version => version.Version), Is.EqualTo(new[] { "1.0.0", "1.2.0" }));
            Assert.That(catalog.Requested, Is.EqualTo(new[] { "Alpha" }));
        });
    }

    [Test]
    public async Task WithoutCatalog_LeavesVersionsEmpty()
    {
        var page = CreatePage(catalog: null);

        Select(page, "Alpha");
        await PumpAsync();

        Assert.That(page.Details!.HasVersions, Is.False);
    }

    [Test]
    public async Task CatalogFailure_ReportsErrorAndStaysEmpty()
    {
        var catalog = new RecordingVersionCatalog(failure: "索引不可读。");
        var errors = new RecordingErrorReporter();
        var page = CreatePage(catalog, errors);

        Select(page, "Alpha");
        await PumpAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.Details!.HasVersions, Is.False);
            Assert.That(errors.Recent.Select(entry => entry.Context), Does.Contain("读取模组版本"));
        });
    }

    [Test]
    public async Task SelectingAnotherRow_DiscardsTheStaleResult()
    {
        var catalog = new DeferredVersionCatalog();
        var page = CreatePage(catalog);

        Select(page, "Alpha");
        Select(page, "Bravo");
        Assert.That(catalog.Calls.Select(call => call.Identifier), Is.EqualTo(new[] { "Alpha", "Bravo" }));

        // 先回来的却是上一个模组的结果：必须丢弃，不能贴到当前模组上。
        catalog.Complete(0, [Option("9.9.9")]);
        await PumpAsync();
        Assert.That(page.Details!.HasVersions, Is.False);

        catalog.Complete(1, [Option("2.0.0")]);
        await PumpAsync();

        Assert.That(page.Details.Versions.Select(version => version.Version), Is.EqualTo(new[] { "2.0.0" }));
    }

    [Test]
    public async Task DeselectingRow_ClearsVersions()
    {
        var catalog = new RecordingVersionCatalog(Option("1.0.0"));
        var page = CreatePage(catalog);
        Select(page, "Alpha");
        await PumpAsync();

        page.SelectedRow = null;
        await PumpAsync();

        Assert.That(page.Details!.HasVersions, Is.False);
    }

    private static ModDetailsViewModel CreateDetails()
        => new(new DialogsFakeRepositoryService(), new DialogsFakeRegistryService());

    private static ModDetailsSnapshot Snapshot(string identifier = "Alpha")
        => new(
            Identifier: identifier,
            Name: identifier,
            Author: "作者甲",
            LatestVersion: "1.0.0",
            Tags: Array.Empty<string>(),
            IsCompatible: true);

    private static ModsPageViewModel CreatePage(IModVersionCatalog? catalog, RecordingErrorReporter? errors = null)
    {
        var repositories = new FakeRepositoryService
        {
            Current = ModsRowFactory.Repository(
                ModsRowFactory.Create("Alpha", "Alpha", "zoe"),
                ModsRowFactory.Create("Bravo", "Bravo", "adam")),
        };
        var instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        var page = new ModsPageViewModel(
            repositories,
            new FakeRegistryService(),
            instances,
            errors ?? new RecordingErrorReporter())
        {
            Scheduler = new ImmediateDelayScheduler(),
            Details = CreateDetails(),
            VersionCatalog = catalog,
        };
        page.Activate();
        return page;
    }

    private static void Select(ModsPageViewModel page, string identifier)
        => page.SelectedRow = page.Rows.FirstOrDefault(row => row.Identifier == identifier);

    private static async Task PumpAsync() => await Task.Delay(20);

    /// <summary>立即返回固定结果的替身，同时记录查询过的标识符。</summary>
    private sealed class RecordingVersionCatalog : IModVersionCatalog
    {
        private readonly ModVersionOption[] _options;
        private readonly string? _failure;

        public RecordingVersionCatalog(params ModVersionOption[] options) => _options = options;

        public RecordingVersionCatalog(string failure) => (_options, _failure) = (Array.Empty<ModVersionOption>(), failure);

        public List<string> Requested { get; } = new();

        public Task<OperationResult<IReadOnlyList<ModVersionOption>>> GetVersionsAsync(
            string identifier,
            CancellationToken cancellationToken = default)
        {
            Requested.Add(identifier);
            return Task.FromResult(_failure is null
                ? OperationResult<IReadOnlyList<ModVersionOption>>.Ok(_options)
                : OperationResult<IReadOnlyList<ModVersionOption>>.Fail(_failure));
        }
    }

    /// <summary>由测试控制完成时机的替身，用来验证"后发先至"的取消守卫。</summary>
    private sealed class DeferredVersionCatalog : IModVersionCatalog
    {
        private readonly List<(string Identifier, TaskCompletionSource<OperationResult<IReadOnlyList<ModVersionOption>>> Source)> _calls = new();

        public IReadOnlyList<(string Identifier, TaskCompletionSource<OperationResult<IReadOnlyList<ModVersionOption>>> Source)> Calls
            => _calls;

        public Task<OperationResult<IReadOnlyList<ModVersionOption>>> GetVersionsAsync(
            string identifier,
            CancellationToken cancellationToken = default)
        {
            var source = new TaskCompletionSource<OperationResult<IReadOnlyList<ModVersionOption>>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _calls.Add((identifier, source));
            return source.Task;
        }

        public void Complete(int index, IReadOnlyList<ModVersionOption> options)
            => _calls[index].Source.SetResult(OperationResult<IReadOnlyList<ModVersionOption>>.Ok(options));
    }
}
