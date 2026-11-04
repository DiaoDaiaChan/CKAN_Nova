using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;

namespace CkanUI.Plugin.Storage.Tests;

[TestFixture]
public sealed class StoragePageViewModelTests
{
    private static InstanceStorageRow Row(
        string name,
        long gameData = 1024,
        long ckan = 512,
        long profiles = 0,
        long? free = 2048,
        IReadOnlyList<ProfileStorageRow>? profileRows = null)
        => new(
            name,
            $"/games/{name}",
            gameData,
            ckan,
            profiles,
            gameData + ckan + profiles,
            profileRows ?? Array.Empty<ProfileStorageRow>(),
            free,
            free is null ? null : free.Value + 1000000);

    private static ProfileStorageRow Profile(string name, long bytes, bool active, int files = 1)
        => new(name, active, bytes, files);

    private static InstanceStorageReport Report(params InstanceStorageRow[] rows)
        => new(
            rows,
            rows.Sum(r => r.TotalBytes),
            rows.Sum(r => r.ProfileBytes),
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Array.Empty<string>());

    private static FakeInstanceService TwoInstances() => new(
        new InstanceInfo("A", "/games/A", "1.12", true, false, 3),
        new InstanceInfo("B", "/games/B", "1.12", false, false, 2));

    [Test]
    public async Task ScanAsync_BuildsRowsAndFormatsSizes()
    {
        var storage = new FakeStorageService
        {
            Report = Report(
                Row("A", gameData: 1024L * 1024 * 1024, ckan: 1536, profiles: 4096, free: 512L * 1024 * 1024),
                Row("B", gameData: 0, ckan: 0, profiles: 0, free: null)),
        };
        var vm = StoragePageViewModel.CreateForTests(storage, TwoInstances());

        await vm.ScanAsync();

        Assert.That(storage.ScanCalls, Is.EqualTo(1));
        Assert.That(vm.Rows, Has.Count.EqualTo(2));
        Assert.That(vm.Rows[0].Name, Is.EqualTo("A"));
        Assert.That(vm.Rows[0].GameDataText, Is.EqualTo("1.0 GB"));
        Assert.That(vm.Rows[0].CkanText, Is.EqualTo("1.5 KB"));
        Assert.That(vm.Rows[0].ProfileText, Is.EqualTo("4.0 KB"));
        Assert.That(vm.Rows[0].TotalText, Is.EqualTo("1.0 GB"));
        Assert.That(vm.Rows[0].FreeText, Is.EqualTo("512.0 MB"));
        // 取不到剩余空间时是「—」不是 0：没量到不等于没空间。
        Assert.That(vm.Rows[1].FreeText, Is.EqualTo("—"));
        Assert.That(vm.TotalText, Is.EqualTo("1.0 GB"));
        Assert.That(vm.ShowContent, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
        Assert.That(vm.RowCountText, Is.EqualTo("2 个实例"));
        Assert.That(vm.SelectedRow?.Name, Is.EqualTo("A"));
    }

    [Test]
    public async Task ScanAsync_EmptyReport_ShowsNoInstanceEmptyState()
    {
        var storage = new FakeStorageService { Report = Report() };
        var vm = StoragePageViewModel.CreateForTests(storage, new FakeInstanceService());

        await vm.ScanAsync();

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.ShowContent, Is.False);
        Assert.That(vm.EmptyTitle, Is.EqualTo("还没有游戏实例"));
        Assert.That(vm.EmptyDetail, Does.Contain("实例"));
        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.TotalText, Is.EqualTo("—"));
    }

    [Test]
    public void MissingService_ShowsServiceUnavailableEmptyState()
    {
        var vm = StoragePageViewModel.CreateForTests(null, null);

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.ShowContent, Is.False);
        Assert.That(vm.EmptyTitle, Is.EqualTo("存储服务未就绪"));
        Assert.That(vm.EmptyDetail, Does.Contain("IInstanceStorageService"));
        Assert.That(vm.ScanCommand.CanExecute(null), Is.False);
        Assert.That(vm.DeduplicateCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task DeduplicateAsync_RunsDryRunAndEntersConfirmationWithoutWriting()
    {
        var storage = new FakeStorageService
        {
            Report = Report(Row("A", profileRows: new[] { Profile("main", 2048, true) })),
            DedupReport = new ProfileDedupReport("A", true, 3, 5, 1024 * 1024, Array.Empty<string>()),
        };
        var vm = StoragePageViewModel.CreateForTests(storage, TwoInstances());
        await vm.ScanAsync();

        await vm.DeduplicateAsync();

        Assert.That(storage.DedupDryRunCalls, Is.EqualTo(new[] { true }));
        Assert.That(storage.DedupInstanceCalls, Is.EqualTo(new[] { "A" }));
        Assert.That(vm.ShowDedupConfirmation, Is.True);
        Assert.That(vm.IsDedupConfirmationOpen, Is.True);
        Assert.That(vm.CanConfirmDedup, Is.True);
        Assert.That(vm.DedupConfirmText, Does.Contain("3 组"));
        Assert.That(vm.DedupConfirmText, Does.Contain("5 个文件"));
        Assert.That(vm.DedupReportText, Does.Contain("预估"));
        Assert.That(vm.HasDedupReport, Is.True);
    }

    [Test]
    public async Task ConfirmDedupAsync_CallsRealRunAndShowsReport()
    {
        var storage = new FakeStorageService
        {
            Report = Report(Row("A", profileRows: new[] { Profile("main", 2048, true) })),
            DedupReport = new ProfileDedupReport("A", true, 2, 4, 2048, Array.Empty<string>()),
        };
        var vm = StoragePageViewModel.CreateForTests(storage, TwoInstances());
        await vm.ScanAsync();
        await vm.DeduplicateAsync();

        await vm.ConfirmDedupAsync();

        Assert.That(storage.DedupDryRunCalls, Is.EqualTo(new[] { true, false }));
        Assert.That(vm.ShowDedupConfirmation, Is.False);
        Assert.That(vm.IsDedupConfirmationOpen, Is.False);
        Assert.That(vm.DedupReportText, Does.Contain("已合并"));
        Assert.That(vm.DedupReportText, Does.Contain("可释放 2.0 KB"));
        Assert.That(vm.StatusMessage, Does.Contain("已合并"));
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task CancelDedup_DoesNotCallServiceAgain()
    {
        var storage = new FakeStorageService
        {
            Report = Report(Row("A", profileRows: new[] { Profile("main", 2048, true) })),
        };
        var vm = StoragePageViewModel.CreateForTests(storage, TwoInstances());
        await vm.ScanAsync();
        await vm.DeduplicateAsync();

        vm.CancelDedup();

        Assert.That(storage.DedupDryRunCalls, Is.EqualTo(new[] { true }));
        Assert.That(vm.ShowDedupConfirmation, Is.False);
        Assert.That(vm.CanConfirmDedup, Is.False);
        // 取消只撤销“待确认”，不抹掉已经展示的预估报告——用户仍可看到可释放空间。
        Assert.That(vm.DedupReportText, Does.Contain("预估"));
        Assert.That(vm.HasDedupReport, Is.True);
    }

    [Test]
    public async Task ScanAsync_DiagnosticsAreExposed()
    {
        var storage = new FakeStorageService
        {
            Report = new InstanceStorageReport(
                new[] { Row("A") },
                1536,
                0,
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                new[] { "/games/A/GameData 读取失败：拒绝访问" }),
        };
        var vm = StoragePageViewModel.CreateForTests(storage, TwoInstances());

        await vm.ScanAsync();

        Assert.That(vm.HasDiagnostics, Is.True);
        Assert.That(vm.DiagnosticsText, Does.Contain("拒绝访问"));
    }

    [Test]
    public async Task Dispose_IsIdempotent()
    {
        var storage = new FakeStorageService { Report = Report(Row("A")) };
        var vm = StoragePageViewModel.CreateForTests(storage, TwoInstances());
        await vm.ScanAsync();

        Assert.DoesNotThrow(() => vm.Dispose());
        Assert.DoesNotThrow(() => vm.Dispose());
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void BytesText_HandlesBoundaries()
    {
        Assert.That(StoragePageViewModel.BytesText(0), Is.EqualTo("0 B"));
        Assert.That(StoragePageViewModel.BytesText(1023), Is.EqualTo("1023 B"));
        Assert.That(StoragePageViewModel.BytesText(1024), Is.EqualTo("1.0 KB"));
        Assert.That(StoragePageViewModel.BytesText(1048576), Is.EqualTo("1.0 MB"));
        Assert.That(StoragePageViewModel.BytesText(1073741824L), Is.EqualTo("1.0 GB"));
        Assert.That(StoragePageViewModel.BytesText(-1), Is.EqualTo("—"));
        Assert.That(StoragePageViewModel.BytesText((long?)null), Is.EqualTo("—"));
    }

    [Test]
    public void RowViewModel_ListsProfilesWithActiveMarker()
    {
        var vm = StoragePageViewModel.CreateForTests(
            new FakeStorageService { Report = Report(Row("A")) },
            TwoInstances());
        var row = new InstanceStorageRowViewModel(Row(
            "A",
            profileRows: new[]
            {
                Profile("main", 1024, active: true),
                Profile("pristine", 2048, active: false, files: 3),
            }));

        Assert.That(row.HasProfiles, Is.True);
        Assert.That(row.ProfileCount, Is.EqualTo(2));
        Assert.That(row.ProfilesText, Does.Contain("main 1.0 KB（使用中）"));
        Assert.That(row.ProfilesText, Does.Contain("pristine 2.0 KB"));
        Assert.That(row.Profiles[1].DetailText, Is.EqualTo("3 个文件"));

        var empty = new InstanceStorageRowViewModel(Row("B"));
        Assert.That(empty.HasProfiles, Is.False);
        Assert.That(empty.ProfilesText, Is.EqualTo("无配置档"));
    }
}
