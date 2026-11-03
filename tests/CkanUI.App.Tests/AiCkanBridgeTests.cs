using CkanUI.App.Ai;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 宿主桥（AI ↔ CKAN）的行为验收：搜索排序、limit 夹取、详情兜底、已安装列表、页面表与导航失败语义。
/// 全部用手写替身，不建 Avalonia 窗口——桥本身不碰 UI，测试也就不该为它拉起一个 UI。
/// </summary>
[TestFixture]
public class AiCkanBridgeTests
{
    private static readonly DateTime SnapshotTime = new(2024, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    // ---- 搜索 ---------------------------------------------------------------

    [Test]
    public void SearchMods_EmptyQuery_OrdersByDownloadCountDescending()
    {
        var bridge = NewBridge(repository: Repository(
            Row("A", "Alpha", downloads: 10),
            Row("B", "Beta", downloads: 300),
            Row("C", "Gamma", downloads: 50)));

        var hits = bridge.SearchMods(null, limit: 2);

        Assert.That(hits.Select(hit => hit.Identifier), Is.EqualTo(new[] { "B", "C" }));
    }

    [Test]
    public void SearchMods_RanksNameExactThenPrefixThenContainsThenIdentifierThenTagThenAbstractThenAuthor()
    {
        var bridge = NewBridge(repository: Repository(
            Row("id-author", "Nothing Here", author: "engineer"),
            Row("id-abstract", "Nothing Here", facts: Facts(@abstract: "engineer friendly")),
            Row("id-tag", "Nothing Here", tags: ["Engineer"]),
            Row("EngineerPlus", "Nothing Here"),
            Row("id-contains", "Kerbal Engineer", downloads: 5_000),
            Row("id-prefix", "Engineer Tool"),
            Row("id-exact", "engineer")));

        var hits = bridge.SearchMods("engineer", limit: 10);

        Assert.That(
            hits.Select(hit => hit.Identifier),
            Is.EqualTo(new[]
            {
                "id-exact",      // 100 名称精确
                "id-prefix",     // 80  名称前缀
                "id-contains",   // 60 + 下载量微调 3
                "EngineerPlus",  // 50  标识符包含
                "id-tag",        // 30  标签
                "id-abstract",   // 20  摘要
                "id-author",     // 0   只命中作者
            }));
    }

    [Test]
    public void SearchMods_TrimsQueryAndIgnoresCase()
    {
        var bridge = NewBridge(repository: Repository(
            Row("KerbalX", "Kerbal X"),
            Row("Other", "Something Else")));

        var hits = bridge.SearchMods("  kErBaL  ", limit: 10);

        Assert.That(hits.Select(hit => hit.Identifier), Is.EqualTo(new[] { "KerbalX" }));
    }

    [Test]
    public void SearchMods_ClampsLimitToBounds()
    {
        var rows = Enumerable.Range(0, 60)
            .Select(index => Row($"Mod{index:D2}", $"Mod {index:D2}", downloads: 100 - index))
            .ToArray();
        var bridge = NewBridge(repository: Repository(rows));

        Assert.That(bridge.SearchMods(string.Empty, limit: 0), Has.Count.EqualTo(1));
        Assert.That(bridge.SearchMods(string.Empty, limit: -5), Has.Count.EqualTo(1));
        Assert.That(bridge.SearchMods(string.Empty, limit: 1_000), Has.Count.EqualTo(50));
    }

    [Test]
    public void SearchMods_TakesInstalledVersionFromRegistry()
    {
        var bridge = NewBridge(
            repository: Repository(Row("B9PartSwitch", "B9 Part Switch", installedVersion: "1.0.0", isInstalled: true)),
            registry: Registry(Installed("B9PartSwitch", "2.20.0")));

        var hit = bridge.SearchMods("b9", limit: 1).Single();

        Assert.That(hit.InstalledVersion, Is.EqualTo("2.20.0"));
        Assert.That(hit.IsInstalled, Is.True);
    }

    [Test]
    public void SearchMods_WhenRepositorySnapshotThrows_ReturnsEmpty()
    {
        var repository = new StubRepositoryService { ThrowOnRead = true };
        var bridge = new AiCkanBridge(repository);

        Assert.That(bridge.SearchMods("kerbal"), Is.Empty);
        Assert.That(bridge.GetModDetail("kerbal"), Is.Null);
    }

    // ---- 详情 ---------------------------------------------------------------

    [Test]
    public void GetModDetail_UnknownIdentifier_ReturnsNull()
    {
        var bridge = NewBridge(repository: Repository(Row("Known", "Known Mod")));

        Assert.That(bridge.GetModDetail("Nope"), Is.Null);
        Assert.That(bridge.GetModDetail("   "), Is.Null);
        Assert.That(bridge.GetModDetail(null!), Is.Null);
    }

    [Test]
    public void GetModDetail_ReadsFactsAndRegistryVersion()
    {
        var facts = Facts(
            @abstract: "short abstract",
            description: "long description",
            licenses: ["MIT", "CC-BY"],
            homepage: "https://example.invalid/b9",
            dependsOn: ["ModuleManager"],
            recommends: ["ToolbarController"],
            conflictsWith: ["OldB9"]);
        var bridge = NewBridge(
            repository: Repository(Row("B9PartSwitch", "B9 Part Switch", downloads: 42, facts: facts)),
            registry: Registry(Installed("B9PartSwitch", "2.20.0")));

        var detail = bridge.GetModDetail("b9partswitch");

        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.Name, Is.EqualTo("B9 Part Switch"));
        Assert.That(detail.Abstract, Is.EqualTo("short abstract"));
        Assert.That(detail.Description, Is.EqualTo("long description"));
        Assert.That(detail.Licenses, Is.EqualTo(new[] { "MIT", "CC-BY" }));
        Assert.That(detail.Homepage, Is.EqualTo("https://example.invalid/b9"));
        Assert.That(detail.InstalledVersion, Is.EqualTo("2.20.0"));
        Assert.That(detail.IsInstalled, Is.True);
        Assert.That(detail.DependsOn, Is.EqualTo(new[] { "ModuleManager" }));
        Assert.That(detail.Recommends, Is.EqualTo(new[] { "ToolbarController" }));
        Assert.That(detail.ConflictsWith, Is.EqualTo(new[] { "OldB9" }));
    }

    [Test]
    public void GetModDetail_FallsBackToInstalledRegistryEntry()
    {
        var bridge = NewBridge(
            repository: Repository(),
            registry: Registry(Installed("HandInstalled", "1.3.1", facts: Facts(
                description: "installed but no longer in the index",
                licenses: ["GPL-3.0"],
                dependsOn: ["ModuleManager"]))));

        var detail = bridge.GetModDetail("handinstalled");

        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.Identifier, Is.EqualTo("HandInstalled"));
        // 注册表投影没有名称，沿用 ModListProjection 的口径：用标识符兜底，不编假名字。
        Assert.That(detail.Name, Is.EqualTo("HandInstalled"));
        Assert.That(detail.InstalledVersion, Is.EqualTo("1.3.1"));
        Assert.That(detail.Description, Is.EqualTo("installed but no longer in the index"));
        Assert.That(detail.Licenses, Is.EqualTo(new[] { "GPL-3.0" }));
        Assert.That(detail.DependsOn, Is.EqualTo(new[] { "ModuleManager" }));
        Assert.That(detail.LatestVersion, Is.Null);
        Assert.That(detail.IsInstalled, Is.True);
        // 没事实时给空集合，不给 null：模型拿到 null 会写出 "null"。
        Assert.That(detail.Recommends, Is.Empty);
        Assert.That(detail.ConflictsWith, Is.Empty);
        Assert.That(detail.Tags, Is.Empty);
    }

    // ---- 已安装 -------------------------------------------------------------

    [Test]
    public void ListInstalled_SortsByIdentifierAndClampsLimit()
    {
        var bridge = NewBridge(registry: Registry(
            Installed("Zeta", "1.0"),
            Installed("alpha", "2.0"),
            Installed("Mike", "3.0")));

        Assert.That(
            bridge.ListInstalled(limit: 2).Select(mod => mod.Identifier),
            Is.EqualTo(new[] { "alpha", "Mike" }));
        Assert.That(bridge.ListInstalled(limit: 0), Has.Count.EqualTo(1));
    }

    [Test]
    public void ListInstalled_ClampsUpperLimitTo500()
    {
        var installed = Enumerable.Range(0, 505)
            .Select(index => Installed($"Mod{index:D3}", "1.0.0"))
            .ToArray();
        var bridge = NewBridge(registry: Registry(installed));

        Assert.That(bridge.ListInstalled(limit: 10_000), Has.Count.EqualTo(500));
    }

    // ---- 页面表 / 导航 -------------------------------------------------------

    [Test]
    public void ListPages_ContainsCoreHostPagesWithTitles()
    {
        var pages = new AiCkanBridge().ListPages();

        var required = new[] { "home", "mods", "instances", "cache", "plugins", "logs", "settings" };
        Assert.That(pages.Select(page => page.Key), Is.SupersetOf(required));
        Assert.That(pages, Has.All.Matches<AiPageRef>(page => !string.IsNullOrWhiteSpace(page.Title)));
    }

    [Test]
    public void ListPages_KeysMatchNavigationTable()
    {
        // 不变量：页面表里每个键都必须真的能解析出页面（页工厂按 ServiceRegistration.PageTypes 查表）。
        // 新增宿主页却忘了登记到桥上时，这条会红，而不是等 AI 说「打开 XX 页」时才静默失败。
        var keys = new AiCkanBridge().ListPages().Select(page => page.Key).ToArray();

        Assert.That(keys, Is.EquivalentTo(ServiceRegistration.PageTypes.Keys));
    }

    [Test]
    public void ListPages_UsesTranslateDelegateWhenAvailable()
    {
        var bridge = new AiCkanBridge(translate: key => key == "Ui_NavRail_Cache" ? "下载缓存（译）" : key);

        var cache = bridge.ListPages().Single(page => page.Key == "cache");

        Assert.That(cache.Title, Is.EqualTo("下载缓存（译）"));
    }

    [Test]
    public async Task NavigateAsync_UnknownKey_FailsWithoutTouchingNavigation()
    {
        var navigation = new RecordingNavigation();
        var bridge = new AiCkanBridge(navigation: navigation);

        var unknown = await bridge.NavigateAsync("no-such-page");
        var blank = await bridge.NavigateAsync("   ");
        var missing = await bridge.NavigateAsync(null!);

        Assert.That(unknown.IsSuccess, Is.False);
        Assert.That(blank.IsSuccess, Is.False);
        Assert.That(missing.IsSuccess, Is.False);
        Assert.That(unknown.Message, Does.Contain("no-such-page"));
        // 关键：不把未知键丢给导航服务——它对未知键的行为不统一（有的抛、有的落空白页）。
        Assert.That(navigation.Calls, Is.Empty);
    }

    [Test]
    public async Task NavigateAsync_KnownKey_NavigatesWithCanonicalKey()
    {
        var navigation = new RecordingNavigation();
        var bridge = new AiCkanBridge(navigation: navigation);

        var result = await bridge.NavigateAsync("HOME");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.True);
        Assert.That(navigation.Calls, Is.EqualTo(new[] { "home" }));
    }

    [Test]
    public async Task NavigateAsync_NavigationReportsFalse_ReturnsFailure()
    {
        var bridge = new AiCkanBridge(navigation: new RecordingNavigation { Result = false });

        var result = await bridge.NavigateAsync("mods");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("mods"));
    }

    [Test]
    public async Task NavigateAsync_NavigationThrows_ReturnsFailureInsteadOfThrowing()
    {
        var bridge = new AiCkanBridge(
            navigation: new RecordingNavigation { Failure = new InvalidOperationException("页面工厂炸了") });

        var result = await bridge.NavigateAsync("mods");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Detail, Does.Contain("页面工厂炸了"));
    }

    [Test]
    public async Task NavigateAsync_WithoutNavigationService_ReturnsFailure()
    {
        var bridge = new AiCkanBridge();

        var result = await bridge.NavigateAsync("mods");

        Assert.That(result.IsSuccess, Is.False);
    }

    // ---- 防御式：服务缺失 ------------------------------------------------

    [Test]
    public async Task NullServices_DegradeToEmptyResultsWithoutThrowing()
    {
        var bridge = new AiCkanBridge();

        var context = bridge.GetContext();
        Assert.That(context.InstanceName, Is.Empty);
        Assert.That(context.GameDir, Is.Null);
        Assert.That(context.KspVersion, Is.Null);
        Assert.That(context.InstalledCount, Is.Zero);
        Assert.That(context.AvailableCount, Is.Zero);
        Assert.That(context.RepositoryUpdatedAt, Is.Null);
        Assert.That(context.RepositoryFromCache, Is.True);

        Assert.That(bridge.SearchMods("kerbal"), Is.Empty);
        Assert.That(bridge.GetModDetail("kerbal"), Is.Null);
        Assert.That(bridge.ListInstalled(), Is.Empty);
        Assert.That(bridge.ListPages(), Is.Not.Empty);
        Assert.That((await bridge.NavigateAsync("mods")).IsSuccess, Is.False);
    }

    [Test]
    public void GetContext_ReportsInstanceAndSnapshotFacts()
    {
        var bridge = NewBridge(
            repository: new RepositorySnapshot(
                [Row("A", "Alpha")],
                ["default"],
                SnapshotTime,
                IsFromCache: true),
            registry: Registry(Installed("A", "1.0"), Installed("B", "2.0")),
            instance: new InstanceInfo("JNSQ", @"D:\KSP", "1.12.5", IsDefault: false, IsLocked: false, ModuleCount: 2));

        var context = bridge.GetContext();

        Assert.That(context.InstanceName, Is.EqualTo("JNSQ"));
        Assert.That(context.GameDir, Is.EqualTo(@"D:\KSP"));
        Assert.That(context.KspVersion, Is.EqualTo("1.12.5"));
        Assert.That(context.InstalledCount, Is.EqualTo(2));
        Assert.That(context.AvailableCount, Is.EqualTo(1));
        Assert.That(context.RepositoryUpdatedAt, Is.EqualTo(SnapshotTime));
        Assert.That(context.RepositoryFromCache, Is.True);
    }

    // ---- 组装 ---------------------------------------------------------------

    private static AiCkanBridge NewBridge(
        RepositorySnapshot? repository = null,
        RegistrySnapshot? registry = null,
        InstanceInfo? instance = null,
        INavigationService? navigation = null,
        Func<string, string>? translate = null) =>
        new(
            repository is null ? null : new StubRepositoryService { Snapshot = repository },
            registry is null ? null : new StubRegistryService { Snapshot = registry },
            instance is null ? null : new StubInstanceService { Instance = instance },
            navigation,
            translate);

    private static RepositorySnapshot Repository(params ModRowData[] rows) =>
        new(rows, ["default"], SnapshotTime, IsFromCache: false);

    private static RegistrySnapshot Registry(params InstalledModuleInfo[] installed) =>
        new(
            "TestInstance",
            "registry.json",
            installed.ToDictionary(info => info.Identifier, info => info, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SnapshotTime);

    private static ModRowData Row(
        string identifier,
        string name,
        long downloads = 0,
        string? author = null,
        string[]? tags = null,
        string? installedVersion = null,
        bool isInstalled = false,
        ModSearchFacts facts = default) =>
        new(
            Identifier: identifier,
            Name: name,
            Author: author,
            InstalledVersion: installedVersion,
            LatestVersion: null,
            DownloadCount: downloads,
            Tags: tags ?? Array.Empty<string>(),
            SizeBytes: null,
            IsInstalled: isInstalled,
            HasUpdate: false,
            IsCompatible: true,
            IsAutoInstalled: false,
            IsSelected: false,
            IsImplicit: false)
        {
            Facts = facts,
        };

    private static InstalledModuleInfo Installed(string identifier, string version, ModSearchFacts facts = default) =>
        new(identifier, version, IsAutoInstalled: false, SourceRepository: "default", InstallTime: null) { Facts = facts };

    private static ModSearchFacts Facts(
        string? @abstract = null,
        string? description = null,
        string[]? licenses = null,
        string? homepage = null,
        string[]? dependsOn = null,
        string[]? recommends = null,
        string[]? conflictsWith = null) =>
        new()
        {
            Abstract = @abstract,
            Description = description,
            Licenses = licenses,
            Homepage = homepage,
            DependsOn = dependsOn,
            Recommends = recommends,
            ConflictsWith = conflictsWith,
        };

    // ---- 替身 ---------------------------------------------------------------

    private sealed class StubRepositoryService : IRepositoryService
    {
        public RepositorySnapshot Snapshot { get; init; } = RepositorySnapshot.Empty;

        public bool ThrowOnRead { get; init; }

        public RepositorySnapshot Current => ThrowOnRead
            ? throw new InvalidOperationException("仓库快照尚未加载。")
            : Snapshot;

        public event Action<RepositorySnapshot>? Updated
        {
            add { }
            remove { }
        }

        public event Action<OperationProgress>? RefreshProgress
        {
            add { }
            remove { }
        }

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Snapshot));

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Snapshot));
    }

    private sealed class StubRegistryService : IRegistryService
    {
        public RegistrySnapshot Snapshot { get; init; } = RegistrySnapshot.Empty();

        public RegistrySnapshot Current => Snapshot;

        public event Action<RegistrySnapshot>? Changed
        {
            add { }
            remove { }
        }

        public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Snapshot));

        public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Snapshot));
    }

    private sealed class StubInstanceService : IInstanceService
    {
        public InstanceInfo? Instance { get; init; }

        public IReadOnlyList<InstanceInfo> Instances => Instance is null ? Array.Empty<InstanceInfo>() : [Instance];

        public InstanceInfo? Current => Instance;

        public event Action? InstancesChanged
        {
            add { }
            remove { }
        }

        public event Action<InstanceInfo?>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持切换实例。"));

        public Task<OperationResult<InstanceInfo>> AddAsync(
            string path,
            string? name = null,
            CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持新增实例。"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(
            string sourceName,
            string newName,
            CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持克隆实例。"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Fail("测试替身不支持删除实例。"));

        public Task<OperationResult<InstanceInfo>> RenameAsync(
            string instanceName,
            string newName,
            CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持重命名实例。"));
    }

    private sealed class RecordingNavigation : INavigationService
    {
        public List<string> Calls { get; } = [];

        public bool Result { get; init; } = true;

        public Exception? Failure { get; init; }

        public object? CurrentPage => null;

        public string CurrentKey => Calls.Count == 0 ? string.Empty : Calls[^1];

        public event Action<object?, string>? Navigated
        {
            add { }
            remove { }
        }

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            Calls.Add(pageKey);
            return Failure is null ? Task.FromResult(Result) : Task.FromException<bool>(Failure);
        }
    }
}
