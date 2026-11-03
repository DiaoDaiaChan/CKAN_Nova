using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 详情面板：空状态、按 Identifier 回查快照、富字段渲染、依赖树着色数据（04 篇 §2/§6，03 篇 §3.1，06 篇 UI-03）。
/// </summary>
[TestFixture]
public sealed class DialogsModDetailsTests
{
    private const string Url = "https://example.invalid/ckan";

    private static ModRowData Row(
        string identifier = "ExampleMod",
        string name = "示例模组",
        string? author = "作者甲",
        string? installedVersion = null,
        string? latestVersion = "2.0.0",
        long? sizeBytes = 1572864,
        bool isInstalled = false,
        bool hasUpdate = false,
        bool isCompatible = true,
        IReadOnlyList<string>? tags = null) =>
        new(
            identifier,
            name,
            author,
            installedVersion,
            latestVersion,
            1234,
            tags ?? Array.Empty<string>(),
            sizeBytes,
            isInstalled,
            hasUpdate,
            isCompatible,
            false,
            false,
            false);

    private static RepositorySnapshot Repository(params ModRowData[] rows) =>
        new(rows, [Url], DateTime.UtcNow, false);

    private static RegistrySnapshot Registry(params InstalledModuleInfo[] installed)
    {
        var map = new Dictionary<string, InstalledModuleInfo>(StringComparer.Ordinal);
        foreach (var info in installed)
        {
            map[info.Identifier] = info;
        }

        return new RegistrySnapshot("默认", "1.34.6", map, new HashSet<string>(), DateTime.UtcNow);
    }

    private static ModDetailsSnapshot Details() => new(
        Identifier: "ExampleMod",
        Name: "示例模组",
        Author: "作者乙",
        Abstract: "一句话摘要",
        Description: "详细描述正文",
        License: "MIT",
        Homepage: "https://example.invalid/mod",
        LatestVersion: "2.0.0",
        SizeBytes: 2048,
        Tags: ["图形"],
        Dependencies:
        [
            new ModRelationItem("DepMod", "依赖模组", ModRelationKind.Dependency, IsSatisfied: true),
            new ModRelationItem("MissingMod"),
        ],
        Recommendations: [new ModRelationItem("RecMod", "推荐模组", ModRelationKind.Recommendation)],
        Conflicts: [new ModRelationItem("BadMod", "冲突模组", ModRelationKind.Conflict)],
        IsCompatible: false);

    [Test]
    public void ModDetails_未选中时是空状态()
    {
        var vm = new ModDetailsViewModel(new DialogsFakeRepositoryService(), new DialogsFakeRegistryService());

        Assert.That(vm.HasSelection, Is.False);
        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.EmptyText, Is.EqualTo("未选中模组"));
        Assert.That(vm.Name, Is.Empty);
        Assert.That(vm.DescriptionText, Is.EqualTo("暂无描述"));
        Assert.That(vm.HasDependencies, Is.False);
        Assert.That(vm.DependencySummaryText, Is.EqualTo("无依赖"));
        Assert.That(vm.SizeText, Is.EqualTo("未知"));
    }

    [Test]
    public void ModDetails_按标识回查列表行与已安装版本()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var registry = new DialogsFakeRegistryService
        {
            Current = Registry(new InstalledModuleInfo("ExampleMod", "1.9.0", false, null, null)),
        };
        var vm = new ModDetailsViewModel(repository, registry);

        vm.Select("ExampleMod");

        Assert.That(vm.HasSelection, Is.True);
        Assert.That(vm.IsEmpty, Is.False);
        Assert.That(vm.Name, Is.EqualTo("示例模组"));
        Assert.That(vm.AuthorText, Is.EqualTo("作者甲"));
        Assert.That(vm.LatestVersionText, Is.EqualTo("2.0.0"));
        Assert.That(vm.InstalledVersionText, Is.EqualTo("1.9.0"));
        Assert.That(vm.SizeText, Is.EqualTo("1.5 MB"));
        Assert.That(vm.IsInstalled, Is.True);
        Assert.That(vm.HasUpdate, Is.False);
        Assert.That(vm.CompatibilityText, Is.EqualTo("兼容当前游戏版本"));
    }

    [Test]
    public void ModDetails_未安装与未知体积的占位()
    {
        var repository = new DialogsFakeRepositoryService
        {
            Current = Repository(Row(sizeBytes: null, latestVersion: null)),
        };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());

        vm.Select("ExampleMod");

        Assert.That(vm.IsInstalled, Is.False);
        Assert.That(vm.InstalledVersionText, Is.EqualTo("未安装"));
        Assert.That(vm.LatestVersionText, Is.EqualTo("—"));
        Assert.That(vm.SizeText, Is.EqualTo("未知"));
        Assert.That(vm.LicenseText, Is.EqualTo("未标注许可证"));
        Assert.That(vm.HasHomepage, Is.False);
        Assert.That(vm.HasTags, Is.False);
        Assert.That(vm.HasAbstract, Is.False);
    }

    [Test]
    public void ModDetails_富字段由提供者注入并渲染()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        string? requested = null;
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = identifier =>
            {
                requested = identifier;
                return Details();
            },
        };

        vm.Select("ExampleMod");

        Assert.That(requested, Is.EqualTo("ExampleMod"));
        Assert.That(vm.AbstractText, Is.EqualTo("一句话摘要"));
        Assert.That(vm.HasAbstract, Is.True);
        Assert.That(vm.DescriptionText, Is.EqualTo("详细描述正文"));
        Assert.That(vm.LicenseText, Is.EqualTo("MIT"));
        Assert.That(vm.HomepageText, Is.EqualTo("https://example.invalid/mod"));
        Assert.That(vm.HasHomepage, Is.True);
        Assert.That(vm.Tags, Is.EqualTo(new[] { "图形" }));
        Assert.That(vm.HasTags, Is.True);
        Assert.That(vm.CompatibilityText, Is.EqualTo("不兼容当前游戏版本"));
    }

    [Test]
    public void ModDetails_依赖树给出满足状态与缺失计数()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
        };

        vm.Select("ExampleMod");

        Assert.That(vm.HasDependencies, Is.True);
        Assert.That(vm.Dependencies, Has.Count.EqualTo(2));
        Assert.That(vm.Dependencies[0].StatusText, Is.EqualTo("已满足"));
        Assert.That(vm.Dependencies[1].StatusText, Is.EqualTo("缺失"));
        Assert.That(vm.MissingDependencyCount, Is.EqualTo(1));
        Assert.That(vm.DependencySummaryText, Is.EqualTo("依赖 2 项，其中 1 项缺失"));
        Assert.That(vm.Dependencies[0].DisplayText, Is.EqualTo("依赖模组 (DepMod)"));
        Assert.That(vm.HasRecommendations, Is.True);
        Assert.That(vm.HasConflicts, Is.True);
    }

    [Test]
    public void ModDetails_元数据键值行包含主页与标签()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
        };

        vm.Select("ExampleMod");

        var names = vm.MetadataItems.Select(item => item.Name).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "作者", "最新版本", "已安装", "体积", "许可证", "主页", "标签" }));
        Assert.That(vm.MetadataItems.First(item => item.Name == "体积").Value, Is.EqualTo("2 KB"));
    }

    [Test]
    public void ModDetails_快照替换后自动重取()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());
        vm.Activate();
        vm.Select("ExampleMod");

        repository.RaiseUpdated(Repository(Row(name: "改名后的模组")));

        Assert.That(vm.Name, Is.EqualTo("改名后的模组"), "快照整体替换后应按 Identifier 重新回查");
        vm.Deactivate();
    }

    [Test]
    public void ModDetails_未激活时不响应快照事件()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());
        vm.Select("ExampleMod");

        repository.RaiseUpdated(Repository(Row(name: "改名后的模组")));

        Assert.That(vm.Name, Is.EqualTo("示例模组"));
    }

    [Test]
    public void ModDetails_停用后不再响应事件且激活幂等()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());

        vm.Activate();
        vm.Activate();
        vm.Select("ExampleMod");
        vm.Deactivate();
        vm.Deactivate();

        repository.RaiseUpdated(Repository(Row(name: "改名后的模组")));

        Assert.That(vm.IsActive, Is.False);
        Assert.That(vm.Name, Is.EqualTo("示例模组"));
    }

    [Test]
    public void ModDetails_切换选中项时旧富字段作废()
    {
        var repository = new DialogsFakeRepositoryService
        {
            Current = Repository(Row(), Row(identifier: "OtherMod", name: "另一个模组", latestVersion: "1.0.0")),
        };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
        };

        vm.Select("ExampleMod");
        Assert.That(vm.DescriptionText, Is.EqualTo("详细描述正文"));

        vm.DetailsProvider = null;
        vm.Select("OtherMod");

        Assert.That(vm.Identifier, Is.EqualTo("OtherMod"));
        Assert.That(vm.Name, Is.EqualTo("另一个模组"));
        Assert.That(vm.DescriptionText, Is.EqualTo("暂无描述"), "换了选中项就不能再显示上一项的富字段");
        Assert.That(vm.HasDependencies, Is.False);
    }

    [Test]
    public void ModDetails_清空选中回到空状态()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());
        vm.Select("ExampleMod");

        vm.Clear();

        Assert.That(vm.Identifier, Is.Null);
        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.Name, Is.Empty);
    }

    [Test]
    public void ModDetails_直接注入详情投影()
    {
        var vm = new ModDetailsViewModel(new DialogsFakeRepositoryService(), new DialogsFakeRegistryService());

        vm.ShowDetails(Details());

        Assert.That(vm.Identifier, Is.EqualTo("ExampleMod"));
        Assert.That(vm.DescriptionText, Is.EqualTo("详细描述正文"));
        Assert.That(vm.MissingDependencyCount, Is.EqualTo(1));
    }

    [Test]
    public void ModDetails_StabilityToleranceOptions_ContainsFourChoices()
    {
        var vm = new ModDetailsViewModel(new DialogsFakeRepositoryService(), new DialogsFakeRegistryService());

        Assert.That(vm.StabilityToleranceOptions, Has.Count.EqualTo(4));
        Assert.That(vm.StabilityToleranceOptions[0].Tolerance, Is.Null);
        Assert.That(vm.StabilityToleranceOptions[0].Label, Does.Contain("继承全局默认"));
        Assert.That(vm.StabilityToleranceOptions[1].Tolerance, Is.EqualTo("stable"));
        Assert.That(vm.StabilityToleranceOptions[2].Tolerance, Is.EqualTo("testing"));
        Assert.That(vm.StabilityToleranceOptions[3].Tolerance, Is.EqualTo("development"));
    }

    [Test]
    public void ModDetails_Select_LoadsModToleranceOverride()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        string? queriedMod = null;
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            ToleranceOverrideProvider = id =>
            {
                queriedMod = id;
                return "testing";
            },
        };

        vm.Select("ExampleMod");

        Assert.That(queriedMod, Is.EqualTo("ExampleMod"));
        Assert.That(vm.SelectedStabilityToleranceOverride?.Tolerance, Is.EqualTo("testing"));
    }

    [Test]
    public async Task ModDetails_ChangeSelectedTolerance_InvokesSetterAndNotifier()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        string? savedMod = null;
        string? savedTolerance = null;
        string? notifiedMod = null;

        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            ToleranceOverrideProvider = _ => null,
            ToleranceOverrideSetter = (id, tol) =>
            {
                savedMod = id;
                savedTolerance = tol;
            },
            NotifyModRefreshed = id => { notifiedMod = id; },
        };

        vm.Select("ExampleMod");
        Assert.That(vm.SelectedStabilityToleranceOverride?.Value, Is.Null);

        // 切换为 testing
        var testingOption = vm.StabilityToleranceOptions.First(o => o.Value == "testing");
        vm.SelectedStabilityToleranceOverride = testingOption;

        // 等待异步 setter
        await Task.Delay(50);

        Assert.That(savedMod, Is.EqualTo("ExampleMod"));
        Assert.That(savedTolerance, Is.EqualTo("testing"));
        Assert.That(notifiedMod, Is.EqualTo("ExampleMod"));

        // 切换回 继承全局默认 (null)
        var defaultOption = vm.StabilityToleranceOptions.First(o => o.Value == null);
        vm.SelectedStabilityToleranceOverride = defaultOption;

        await Task.Delay(50);

        Assert.That(savedMod, Is.EqualTo("ExampleMod"));
        Assert.That(savedTolerance, Is.Null);
    }

    private sealed class FakeSpacedockImageService : ISpacedockImageService
    {
        public Func<string, Task<OperationResult<byte[]>>> Handler { get; set; } =
            _ => Task.FromResult(OperationResult<byte[]>.Ok(new byte[] { 1, 2, 3 }));

        public Task<OperationResult<byte[]>> FetchScreenshotAsync(string spacedockUrl, CancellationToken cancellationToken = default) =>
            Handler(spacedockUrl);
    }

    private sealed class FakeModDetailsShellLauncher : IModDetailsShellLauncher
    {
        public string? OpenedUrl { get; private set; }

        public Task<OperationResult> OpenBrowserAsync(string url)
        {
            OpenedUrl = url;
            return Task.FromResult(OperationResult.Ok());
        }
    }

    [Test]
    public void ModDetails_SpacedockUrl_PopulatedFromSnapshot()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var detailsWithSpacedock = new ModDetailsSnapshot(
            Identifier: "ExampleMod",
            Name: "示例模组",
            Author: "作者乙",
            Abstract: "一句话摘要",
            Description: "详细描述正文",
            License: "MIT",
            Homepage: "https://example.invalid/mod",
            LatestVersion: "2.0.0",
            SizeBytes: 2048,
            Tags: ["图形"],
            Dependencies: [],
            Recommendations: [],
            Conflicts: [],
            IsCompatible: true,
            SpacedockUrl: "https://spacedock.info/mod/123/ExampleMod");

        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => detailsWithSpacedock
        };

        vm.Select("ExampleMod");

        Assert.That(vm.HasSpacedockUrl, Is.True);
        Assert.That(vm.SpacedockUrl, Is.EqualTo("https://spacedock.info/mod/123/ExampleMod"));
        Assert.That(vm.HasSpacedockImage, Is.False, "默认不加载 SpaceDock 图片");
        Assert.That(vm.IsLoadingSpacedockImage, Is.False);
    }

    [Test]
    public async Task ModDetails_SpacedockImage_LazyLoadSuccess()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var expectedBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var fakeImgService = new FakeSpacedockImageService
        {
            Handler = url => Task.FromResult(OperationResult<byte[]>.Ok(expectedBytes))
        };

        var details = new ModDetailsSnapshot(
            Identifier: "ExampleMod",
            Name: "示例模组",
            Author: "作者乙",
            Abstract: "一句话摘要",
            Description: "详细描述正文",
            License: "MIT",
            Homepage: null,
            LatestVersion: "2.0.0",
            SizeBytes: 2048,
            Tags: [],
            Dependencies: [],
            Recommendations: [],
            Conflicts: [],
            IsCompatible: true,
            SpacedockUrl: "https://spacedock.info/mod/456/ExampleMod");

        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService(), spacedockImageService: fakeImgService)
        {
            DetailsProvider = _ => details
        };

        vm.Select("ExampleMod");
        Assert.That(vm.HasSpacedockImage, Is.False);

        await vm.LoadSpacedockImageCommand.ExecuteAsync(null);

        Assert.That(vm.HasSpacedockImage, Is.True);
        Assert.That(vm.SpacedockImageBytes, Is.EqualTo(expectedBytes));
        Assert.That(vm.HasSpacedockImageError, Is.False);
    }

    [Test]
    public async Task ModDetails_SpacedockImage_LazyLoadFailure_ShowsError()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var fakeImgService = new FakeSpacedockImageService
        {
            Handler = url => Task.FromResult(OperationResult<byte[]>.Fail("网络超时或模组无背景图"))
        };

        var details = new ModDetailsSnapshot(
            Identifier: "ExampleMod",
            Name: "示例模组",
            Author: "作者乙",
            Abstract: "一句话摘要",
            Description: "详细描述正文",
            License: "MIT",
            Homepage: null,
            LatestVersion: "2.0.0",
            SizeBytes: 2048,
            Tags: [],
            Dependencies: [],
            Recommendations: [],
            Conflicts: [],
            IsCompatible: true,
            SpacedockUrl: "https://spacedock.info/mod/789/ErrorMod");

        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService(), spacedockImageService: fakeImgService)
        {
            DetailsProvider = _ => details
        };

        vm.Select("ExampleMod");
        await vm.LoadSpacedockImageCommand.ExecuteAsync(null);

        Assert.That(vm.HasSpacedockImage, Is.False);
        Assert.That(vm.HasSpacedockImageError, Is.True);
        Assert.That(vm.SpacedockImageError, Does.Contain("网络超时或模组无背景图"));
    }

    [Test]
    public async Task ModDetails_Spacedock_OpenBrowserAsync()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var launcher = new FakeModDetailsShellLauncher();
        var details = new ModDetailsSnapshot(
            Identifier: "ExampleMod",
            Name: "示例模组",
            Author: "作者乙",
            Abstract: "一句话摘要",
            Description: "详细描述正文",
            License: "MIT",
            Homepage: null,
            LatestVersion: "2.0.0",
            SizeBytes: 2048,
            Tags: [],
            Dependencies: [],
            Recommendations: [],
            Conflicts: [],
            IsCompatible: true,
            SpacedockUrl: "https://spacedock.info/mod/555/CoolMod");

        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => details,
            ShellLauncher = launcher
        };

        vm.Select("ExampleMod");
        await vm.OpenSpacedockUrlCommand.ExecuteAsync(null);

        Assert.That(launcher.OpenedUrl, Is.EqualTo("https://spacedock.info/mod/555/CoolMod"));
    }

    [Test]
    public async Task ModDetails_Spacedock_StateResetOnClear()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var fakeImgService = new FakeSpacedockImageService();
        var details = new ModDetailsSnapshot(
            Identifier: "ExampleMod",
            Name: "示例模组",
            Author: "作者乙",
            Abstract: "一句话摘要",
            Description: "详细描述正文",
            License: "MIT",
            Homepage: null,
            LatestVersion: "2.0.0",
            SizeBytes: 2048,
            Tags: [],
            Dependencies: [],
            Recommendations: [],
            Conflicts: [],
            IsCompatible: true,
            SpacedockUrl: "https://spacedock.info/mod/111/Test");

        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService(), spacedockImageService: fakeImgService)
        {
            DetailsProvider = _ => details
        };

        vm.Select("ExampleMod");
        await vm.LoadSpacedockImageCommand.ExecuteAsync(null);
        Assert.That(vm.HasSpacedockImage, Is.True);

        vm.Clear();

        Assert.That(vm.HasSpacedockUrl, Is.False);
        Assert.That(string.IsNullOrWhiteSpace(vm.SpacedockUrl), Is.True);
        Assert.That(vm.HasSpacedockImage, Is.False);
        Assert.That(vm.SpacedockImageBytes, Is.Null);
        Assert.That(vm.HasSpacedockImageError, Is.False);
    }

    // ---- 模组元数据译文（IModTextProvider 接线）--------------------------------------------
    // 语义铁律：原始英文 → Find(Identifier, "abstract"/"description", 原始英文) → 命中用译文，
    // 否则回落原文；provider/identifier 为 null 时必须与未接入译文前逐字节一致。

    [Test]
    public void ModDetails_摘要与描述命中译文时替换显示并标记已译()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var provider = new RecordingModTextProvider();
        provider.Add("abstract", "一句话摘要", "一句话摘要（译）");
        provider.Add("description", "详细描述正文", "详细描述正文（译）");
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
            TextProvider = provider,
        };

        vm.Select("ExampleMod");

        Assert.That(vm.AbstractText, Is.EqualTo("一句话摘要（译）"));
        Assert.That(vm.DescriptionText, Is.EqualTo("详细描述正文（译）"));
        Assert.That(vm.IsAbstractTranslated, Is.True);
        Assert.That(vm.IsDescriptionTranslated, Is.True);
        Assert.That(vm.HasAbstract, Is.True);
        Assert.That(vm.HasStaleTranslation, Is.False);
        Assert.That(vm.StaleTranslationNotice, Is.Empty);
    }

    [Test]
    public void ModDetails_未命中译文时回落原文且不报已译()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
            TextProvider = new RecordingModTextProvider(),
        };

        vm.Select("ExampleMod");

        Assert.That(vm.AbstractText, Is.EqualTo("一句话摘要"));
        Assert.That(vm.DescriptionText, Is.EqualTo("详细描述正文"));
        Assert.That(vm.IsAbstractTranslated, Is.False);
        Assert.That(vm.IsDescriptionTranslated, Is.False);
        Assert.That(vm.HasStaleTranslation, Is.False);
        Assert.That(vm.StaleTranslationNotice, Is.Empty);
    }

    [Test]
    public void ModDetails_译文过期时给出可见的过期提示()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var provider = new RecordingModTextProvider();
        provider.Add("abstract", "一句话摘要", "旧版译文", stale: true);
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
            TextProvider = provider,
        };

        vm.Select("ExampleMod");

        Assert.That(vm.IsAbstractStale, Is.True);
        Assert.That(vm.IsDescriptionStale, Is.False);
        Assert.That(vm.HasStaleTranslation, Is.True);
        Assert.That(vm.StaleTranslationNotice, Does.Contain("旧版"));
        Assert.That(vm.AbstractText, Is.EqualTo("旧版译文"));
    }

    [Test]
    public void ModDetails_无译文提供者时与旧行为逐字节一致()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());
        Assert.That(vm.TextProvider, Is.Null);

        vm.ShowDetails(Details());

        Assert.That(vm.AbstractText, Is.EqualTo("一句话摘要"));
        Assert.That(vm.DescriptionText, Is.EqualTo("详细描述正文"));
        Assert.That(vm.IsAbstractTranslated, Is.False);
        Assert.That(vm.IsDescriptionTranslated, Is.False);

        // 空描述仍走占位文案，空摘要仍为空串（与改动前一致）。
        vm.ShowDetails(new ModDetailsSnapshot(Identifier: "ExampleMod", Name: "示例模组"));

        Assert.That(vm.AbstractText, Is.Empty);
        Assert.That(vm.DescriptionText, Is.EqualTo("暂无描述"));
        Assert.That(vm.HasAbstract, Is.False);
        Assert.That(vm.HasStaleTranslation, Is.False);
    }

    [Test]
    public void ModDetails_Find收到当前标识与原始英文字段()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var provider = new RecordingModTextProvider();
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
            TextProvider = provider,
        };

        vm.Select("ExampleMod");
        _ = vm.AbstractText;
        _ = vm.DescriptionText;

        Assert.That(
            provider.Calls,
            Has.Some.Matches<(string Identifier, string Field, string SourceText)>(
                c => c.Identifier == "ExampleMod" && c.Field == "abstract" && c.SourceText == "一句话摘要"));
        Assert.That(
            provider.Calls,
            Has.Some.Matches<(string Identifier, string Field, string SourceText)>(
                c => c.Identifier == "ExampleMod" && c.Field == "description" && c.SourceText == "详细描述正文"));
        Assert.That(provider.Calls, Is.Not.Empty);
        Assert.That(provider.Calls.TrueForAll(c => c.Identifier == "ExampleMod"), Is.True);
    }

    [Test]
    public void ModDetails_换绑提供者时通知所有派生属性重算()
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(Row()) };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService())
        {
            DetailsProvider = _ => Details(),
        };
        vm.Select("ExampleMod");
        Assert.That(vm.IsAbstractTranslated, Is.False);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var provider = new RecordingModTextProvider();
        provider.Add("abstract", "一句话摘要", "一句话摘要（译）");
        vm.TextProvider = provider;

        // 空属性名 = 全体属性失效（Refresh 的既有口径），派生属性必须跟着重算。
        Assert.That(changed, Does.Contain(string.Empty));
        Assert.That(vm.IsAbstractTranslated, Is.True);
        Assert.That(vm.AbstractText, Is.EqualTo("一句话摘要（译）"));
    }

    /// <summary>译文 provider 替身：按 (字段, 原文) 命中，并记录每次 Find 的入参。</summary>
    private sealed class RecordingModTextProvider : CkanUI.ModText.IModTextProvider
    {
        private readonly Dictionary<(string Field, string Text), CkanUI.ModText.ModText> _entries = new();

        /// <summary>每次 Find 收到的 (identifier, field, sourceText)。</summary>
        public List<(string Identifier, string Field, string SourceText)> Calls { get; } = new();

        /// <summary>登记一条译文（默认人工来源、不过期）。</summary>
        public void Add(string field, string sourceText, string translation, bool stale = false) =>
            _entries[(field, sourceText)] = new(
                translation,
                CkanUI.ModText.ModTextSource.Human,
                stale);

        /// <inheritdoc />
        public CkanUI.ModText.ModText? Find(string identifier, string field, string sourceText)
        {
            Calls.Add((identifier, field, sourceText));
            return _entries.TryGetValue((field, sourceText), out var hit) ? hit : null;
        }
    }
}
