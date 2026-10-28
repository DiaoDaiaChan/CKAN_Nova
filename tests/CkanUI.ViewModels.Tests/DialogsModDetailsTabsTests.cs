using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 详情面板四页签（元数据与简介 / 依赖关系 / 全部版本 / 文件与内容）：
/// 版本列表投影与徽章（F-30）、已装文件列表（F-26）、内容预览跳转（F-29）、
/// 指定版本安装经 <see cref="IChangeSetService"/> 的 <see cref="ChangeRequest.TargetVersion"/> 发起。
/// 对应 docs/design/mod-details-tabs.md 的测试落点。
/// </summary>
[TestFixture]
public sealed class DialogsModDetailsTabsTests
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

    /// <summary>带版本列表与已装文件清单的快照（页签 3、4 的数据来源）。</summary>
    private static ModDetailsSnapshot Details(
        string identifier = "ExampleMod",
        IReadOnlyList<ModVersionItemDto>? versions = null,
        IReadOnlyList<string>? installedFiles = null,
        IReadOnlyList<ModRelationItem>? dependencies = null,
        IReadOnlyList<ModRelationItem>? recommendations = null,
        IReadOnlyList<ModRelationItem>? conflicts = null) => new(
        Identifier: identifier,
        Name: "示例模组",
        Author: "作者乙",
        Abstract: "一句话摘要",
        Description: "详细描述正文",
        License: "MIT",
        Homepage: "https://example.invalid/mod",
        LatestVersion: "2.0.0",
        SizeBytes: 2048,
        Tags: ["图形"],
        Dependencies: dependencies ?? [new ModRelationItem("DepMod", "依赖模组", ModRelationKind.Dependency, IsSatisfied: true)],
        Recommendations: recommendations ?? [new ModRelationItem("RecMod", "推荐模组", ModRelationKind.Recommendation)],
        Conflicts: conflicts ?? [new ModRelationItem("BadMod", "冲突模组", ModRelationKind.Conflict)],
        IsCompatible: false,
        Versions: versions,
        InstalledFiles: installedFiles);

    private static ModVersionItemDto Version(
        string version,
        bool isInstalled = false,
        bool isCompatible = true,
        bool isUnstable = false,
        DateTimeOffset? releaseDate = null,
        long? downloadSizeBytes = null) =>
        new(version, "1.12", releaseDate, downloadSizeBytes, "https://example.invalid/download", isInstalled, isCompatible, isUnstable);

    private static ChangeSetPreview Preview(params ChangeItemDto[] items) =>
        new(items, Array.Empty<string>(), 5_000_000, Array.Empty<string>());

    private static ChangeItemDto PreviewItem(string version = "1.1.0") => new(
        "ExampleMod",
        "示例模组",
        ChangeAction.Install,
        FromVersion: null,
        ToVersion: version,
        ChangeOrigin.UserRequested,
        DownloadBytes: 5_000_000);

    private static ModDetailsViewModel CreateViewModel(
        IChangeSetService? changeSets = null,
        IChangeRequestSink? sink = null,
        INavigationService? navigation = null,
        params ModRowData[] rows)
    {
        var repository = new DialogsFakeRepositoryService { Current = Repository(rows) };
        var registry = new DialogsFakeRegistryService();
        return new ModDetailsViewModel(repository, registry, changeSets, sink, navigation);
    }

    // ---------- 页签标题与索引 ----------

    [Test]
    public void ModDetailsTabs_页签标题带计数角标()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(
            versions: [Version("1.0.0"), Version("1.1.0"), Version("2.0.0")],
            installedFiles: ["GameData/Mod/Mod.dll", "readme.md"]));

        Assert.That(vm.MetadataTabHeaderText, Is.EqualTo("元数据与简介"), "元数据页签标题不带计数");
        Assert.That(vm.RelationsTabHeaderText, Is.EqualTo("依赖关系 (3)"), "依赖 + 推荐 + 冲突共 3 项");
        Assert.That(vm.VersionsTabHeaderText, Is.EqualTo("全部版本 (3)"), "版本计数角标");
        Assert.That(vm.ContentsTabHeaderText, Is.EqualTo("文件与内容 (2)"), "已装文件计数角标");
    }

    [Test]
    public void ModDetailsTabs_没有可计数内容时页签标题不带角标()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(
            versions: [],
            installedFiles: [],
            dependencies: [],
            recommendations: [],
            conflicts: []));

        Assert.That(vm.RelationsTabHeaderText, Is.EqualTo("依赖关系"), "无关系时不带角标");
        Assert.That(vm.VersionsTabHeaderText, Is.EqualTo("全部版本"), "无版本时不带角标");
        Assert.That(vm.ContentsTabHeaderText, Is.EqualTo("文件与内容"), "无文件时不带角标");
        Assert.That(vm.HasRelations, Is.False);
    }

    [Test]
    public void ModDetailsTabs_页签索引切换与越界收敛()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details());

        Assert.That(vm.SelectedTabIndex, Is.EqualTo(0), "默认停在第一个页签");

        vm.SelectedTabIndex = 2;
        Assert.That(vm.SelectedTabIndex, Is.EqualTo(2), "可以切到全部版本页");

        vm.SelectedTabIndex = 9;
        Assert.That(vm.SelectedTabIndex, Is.EqualTo(ModDetailsViewModel.TabCount - 1), "越界上界收敛到最后一页");

        vm.SelectedTabIndex = -3;
        Assert.That(vm.SelectedTabIndex, Is.EqualTo(0), "越界下界收敛到第一页");
    }

    [Test]
    public void ModDetailsTabs_换模组重置页签与安装反馈()
    {
        var changeSets = new ModTabsFakeChangeSetService
        {
            NextResult = OperationResult<ChangeSetPreview>.Ok(Preview(PreviewItem())),
        };
        var vm = CreateViewModel(changeSets, rows: Row());
        vm.ShowDetails(Details());
        vm.SelectedTabIndex = 2;
        vm.InstallVersionCommand.ExecuteAsync(Version("1.1.0")).GetAwaiter().GetResult();

        Assert.That(vm.HasInstallStatusText, Is.True, "先制造一条安装反馈");
        Assert.That(vm.HasLastChangeSet, Is.True);

        vm.ShowDetails(Details(identifier: "OtherMod"));

        Assert.That(vm.SelectedTabIndex, Is.EqualTo(0), "换模组回到元数据页");
        Assert.That(vm.InstallStatusText, Is.Empty, "上一轮的安装反馈必须清空");
        Assert.That(vm.HasLastChangeSet, Is.False, "上一轮的变更集预览必须清空");
    }

    [Test]
    public void ModDetailsTabs_清空选中回到第一页并重置状态()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.0.0")]));
        vm.SelectedTabIndex = 3;

        vm.Clear();

        Assert.That(vm.SelectedTabIndex, Is.EqualTo(0), "清空后回到元数据页");
        Assert.That(vm.HasVersions, Is.False);
        Assert.That(vm.HasInstalledFiles, Is.False);
        Assert.That(vm.HasInstallStatusText, Is.False);
    }

    // ---------- 版本列表投影 ----------

    [Test]
    public void ModDetailsTabs_版本列表投影与已装标记补齐()
    {
        var repository = new DialogsFakeRepositoryService
        {
            Current = Repository(Row(installedVersion: "1.1.0", isInstalled: true)),
        };
        var registry = new DialogsFakeRegistryService
        {
            Current = Registry(new InstalledModuleInfo("ExampleMod", "1.1.0", false, null, null)),
        };
        var vm = new ModDetailsViewModel(repository, registry);
        vm.ShowDetails(Details(versions: [Version("1.0.0"), Version("1.1.0"), Version("2.0.0")]));

        Assert.That(vm.HasVersions, Is.True);
        Assert.That(vm.Versions.Count, Is.EqualTo(3), "三个版本全部投影");
        Assert.That(vm.VersionsCountText, Is.EqualTo("共 3 个版本"));
        Assert.That(vm.Versions[1].IsInstalled, Is.True, "已装版本必须打上标记");
        Assert.That(vm.Versions[1].CompatibilityBadgeText, Is.EqualTo("当前已装"));
        Assert.That(vm.Versions[0].IsInstalled, Is.False, "其它版本不能误标");
        Assert.That(vm.Versions[2].IsInstalled, Is.False);
    }

    [Test]
    public void ModDetailsTabs_快照已标记已装时保留原标记()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.0.0", isInstalled: true)]));

        Assert.That(vm.Versions[0].IsInstalled, Is.True, "没有已装版本回查结果时也不能抹掉快照自己的标记");
    }

    [Test]
    public void ModDetailsTabs_空版本列表防空()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(versions: null));

        Assert.That(vm.HasVersions, Is.False);
        Assert.That(vm.Versions, Is.Empty);
        Assert.That(vm.VersionsCountText, Is.EqualTo("暂无版本记录"));
        Assert.That(vm.VersionsEmptyText, Is.EqualTo("该模组暂无可用版本记录。"), "版本页空状态文案");
    }

    [Test]
    public void ModDetailsTabs_版本行徽章与日期体积文案()
    {
        var release = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

        Assert.That(Version("1.2.3", isCompatible: true).CompatibilityBadgeText, Is.EqualTo("兼容"));
        Assert.That(Version("1.2.3", isCompatible: false).CompatibilityBadgeText, Is.EqualTo("不兼容"));
        Assert.That(Version("1.2.3", isUnstable: true).StabilityBadgeText, Is.EqualTo("不稳定/测试版"));
        Assert.That(Version("1.2.3").StabilityBadgeText, Is.EqualTo("稳定版"));

        var row = Version("1.2.3", releaseDate: release, downloadSizeBytes: 5L * 1024 * 1024);
        Assert.That(row.VersionText, Is.EqualTo("1.2.3"));
        Assert.That(row.ReleaseDateText, Is.EqualTo("2024-05-06"), "发布日期按 yyyy-MM-dd 投影");
        Assert.That(row.DownloadSizeText, Is.EqualTo("5 MB"), "下载体积走人读格式");
        Assert.That(Version("1.2.3").ReleaseDateText, Is.EqualTo("未知"), "缺发布日期给占位");

        var installed = Version("1.2.3", isInstalled: true);
        Assert.That(installed.CompatibilityBadgeText, Is.EqualTo("当前已装"), "已装优先显示当前已装");
    }

    // ---------- 已装文件投影 ----------

    [Test]
    public void ModDetailsTabs_已装文件列表投影与路径拆分()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(installedFiles: ["GameData/Mod/Mod.dll", "GameData\\Mod\\Plugins\\a.dll", "readme.md"]));

        Assert.That(vm.HasInstalledFiles, Is.True);
        Assert.That(vm.InstalledFiles.Count, Is.EqualTo(3), "InstalledFiles 保留原始路径列表");
        Assert.That(vm.InstalledFilesCountText, Is.EqualTo("共 3 个文件"));
        Assert.That(vm.InstalledFileRows.Count, Is.EqualTo(3));

        Assert.That(vm.InstalledFileRows[0].FileName, Is.EqualTo("Mod.dll"));
        Assert.That(vm.InstalledFileRows[0].DirectoryText, Is.EqualTo("GameData/Mod"));
        Assert.That(vm.InstalledFileRows[0].HasDirectory, Is.True);

        Assert.That(vm.InstalledFileRows[1].FileName, Is.EqualTo("a.dll"), "反斜杠路径同样要拆对");
        Assert.That(vm.InstalledFileRows[1].DirectoryText, Is.EqualTo("GameData\\Mod\\Plugins"));

        Assert.That(vm.InstalledFileRows[2].FileName, Is.EqualTo("readme.md"));
        Assert.That(vm.InstalledFileRows[2].DirectoryText, Is.Empty, "根目录文件没有目录行");
        Assert.That(vm.InstalledFileRows[2].HasDirectory, Is.False);
    }

    [Test]
    public void ModDetailsTabs_空文件清单防空()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(installedFiles: null));

        Assert.That(vm.HasInstalledFiles, Is.False);
        Assert.That(vm.InstalledFileRows, Is.Empty);
        Assert.That(vm.InstalledFilesCountText, Is.EqualTo("暂无文件记录"));
        Assert.That(vm.ContentsEmptyText, Is.EqualTo("暂无已装文件记录。"), "内容页空状态文案");
    }

    [Test]
    public void ModDetailsTabs_内容页文案随是否已装变化()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details());
        Assert.That(vm.ContentPreviewHintText, Does.Contain("尚未安装"), "未装时提示去看压缩包结构");

        var installed = new DialogsFakeRepositoryService
        {
            Current = Repository(Row(installedVersion: "2.0.0", isInstalled: true)),
        };
        var registry = new DialogsFakeRegistryService
        {
            Current = Registry(new InstalledModuleInfo("ExampleMod", "2.0.0", false, null, null)),
        };
        var vm2 = new ModDetailsViewModel(installed, registry);
        vm2.ShowDetails(Details());

        Assert.That(vm2.ContentPreviewHintText, Does.Contain("已装文件"), "已装时提示先看本实例文件");
        Assert.That(vm2.ContentPreviewButtonText, Is.EqualTo("打开内容预览"));
    }

    // ---------- 内容预览跳转 ----------

    [Test]
    public void ModDetailsTabs_内容预览跳转走导航服务并带上模组标识()
    {
        var navigation = new ModTabsFakeNavigationService();
        var vm = CreateViewModel(navigation: navigation, rows: Row());
        vm.ShowDetails(Details());

        vm.NavigateToContentPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(navigation.Calls.Count, Is.EqualTo(1), "只导航一次");
        Assert.That(navigation.Calls[0].Key, Is.EqualTo(ModDetailsViewModel.ContentPreviewPageKey), "目标页键是全局内容预览页");
        Assert.That(navigation.Calls[0].Parameter, Is.EqualTo("ExampleMod"), "把当前模组标识作为导航参数");
        Assert.That(vm.HasContentNavigationStatusText, Is.False, "跳转成功后不该留提示");
    }

    [Test]
    public void ModDetailsTabs_导航失败时回落到外部跳转()
    {
        var navigation = new ModTabsFakeNavigationService { Result = false };
        string? external = null;
        var vm = CreateViewModel(navigation: navigation, rows: Row());
        vm.ShowDetails(Details());
        vm.ExternalNavigateAction = identifier => external = identifier;

        vm.NavigateToContentPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(navigation.Calls.Count, Is.EqualTo(1), "先试过导航服务");
        Assert.That(external, Is.EqualTo("ExampleMod"), "导航服务拒绝后由宿主接管");
        Assert.That(vm.HasContentNavigationStatusText, Is.False);
    }

    [Test]
    public void ModDetailsTabs_导航抛异常时给出提示且不再回落()
    {
        var navigation = new ModTabsFakeNavigationService
        {
            ThrowOnNavigate = new InvalidOperationException("页面未注册"),
        };
        var externalCalled = false;
        var vm = CreateViewModel(navigation: navigation, rows: Row());
        vm.ShowDetails(Details());
        vm.ExternalNavigateAction = _ => externalCalled = true;

        vm.NavigateToContentPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(externalCalled, Is.False, "异常说明导航链路坏了，不应静默换一条路");
        Assert.That(vm.ContentNavigationStatusText, Does.Contain("打开内容预览页失败：页面未注册"));
    }

    [Test]
    public void ModDetailsTabs_没有导航服务时使用外部跳转()
    {
        string? external = null;
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details());
        vm.ExternalNavigateAction = identifier => external = identifier;

        vm.NavigateToContentPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(external, Is.EqualTo("ExampleMod"));
    }

    [Test]
    public void ModDetailsTabs_没有可用导航时给出提示()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details());

        vm.NavigateToContentPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(vm.HasContentNavigationStatusText, Is.True);
        Assert.That(vm.ContentNavigationStatusText, Is.EqualTo("当前没有可用的导航服务，无法打开内容预览。"));
    }

    [Test]
    public void ModDetailsTabs_未选中模组时内容预览不跳转()
    {
        var navigation = new ModTabsFakeNavigationService();
        var vm = CreateViewModel(navigation: navigation, rows: Row());

        vm.NavigateToContentPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(navigation.Calls, Is.Empty, "没选中模组不该导航");
        Assert.That(vm.ContentNavigationStatusText, Is.EqualTo("未选中模组，无法打开内容预览。"));
    }

    // ---------- 指定版本安装 ----------

    [Test]
    public void ModDetailsTabs_安装指定版本把目标版本号交给变更集服务()
    {
        var changeSets = new ModTabsFakeChangeSetService
        {
            NextResult = OperationResult<ChangeSetPreview>.Ok(Preview(PreviewItem("1.1.0"))),
        };
        var sink = new ModTabsFakeChangeRequestSink();
        var vm = CreateViewModel(changeSets, sink, rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.1.0")]));

        vm.InstallVersionCommand.ExecuteAsync(vm.Versions[0]).GetAwaiter().GetResult();

        Assert.That(changeSets.ReceivedRequests.Count, Is.EqualTo(1), "发起一次变更集计算");
        var request = changeSets.ReceivedRequests[0].Single();
        Assert.That(request.Identifier, Is.EqualTo("ExampleMod"));
        Assert.That(request.Action, Is.EqualTo(ChangeAction.Install), "安装动作");
        Assert.That(request.TargetVersion, Is.EqualTo("1.1.0"), "目标版本号必须放进 ChangeRequest.TargetVersion（F-30 精确版本）");

        Assert.That(sink.LastRequests, Is.Not.Null, "生成的请求要交给变更集页确认");
        Assert.That(sink.LastRequests!.Single().TargetVersion, Is.EqualTo("1.1.0"));
        Assert.That(vm.HasLastChangeSet, Is.True, "预览要留给宿主展示");
        Assert.That(vm.LastChangeSet!.Items.Count, Is.EqualTo(1));
        Assert.That(vm.InstallStatusText, Does.Contain("已生成变更集"), "给出成功反馈");
        Assert.That(vm.InstallStatusIsError, Is.False);
        Assert.That(vm.IsInstalling, Is.False, "结束后必须复位忙碌标记");
    }

    [Test]
    public void ModDetailsTabs_安装空变更集时给出无需安装反馈()
    {
        var changeSets = new ModTabsFakeChangeSetService
        {
            NextResult = OperationResult<ChangeSetPreview>.Ok(Preview()),
        };
        var vm = CreateViewModel(changeSets, rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.1.0")]));

        vm.InstallVersionCommand.ExecuteAsync(vm.Versions[0]).GetAwaiter().GetResult();

        Assert.That(vm.HasLastChangeSet, Is.True);
        Assert.That(vm.InstallStatusText, Does.Contain("没有需要安装的内容"), "空预览要说明目标版本已装或不可用");
        Assert.That(vm.InstallStatusIsError, Is.False);
    }

    [Test]
    public void ModDetailsTabs_安装失败时给出错误反馈并清空预览()
    {
        var changeSets = new ModTabsFakeChangeSetService
        {
            NextResult = OperationResult<ChangeSetPreview>.Fail("索引里没有该版本"),
        };
        var sink = new ModTabsFakeChangeRequestSink();
        var vm = CreateViewModel(changeSets, sink, rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.1.0")]));

        vm.InstallVersionCommand.ExecuteAsync(vm.Versions[0]).GetAwaiter().GetResult();

        Assert.That(vm.InstallStatusText, Is.EqualTo("生成变更集失败：索引里没有该版本"));
        Assert.That(vm.InstallStatusIsError, Is.True, "失败要走危险色横幅");
        Assert.That(vm.HasLastChangeSet, Is.False, "失败必须清掉预览");
        Assert.That(sink.LastRequests, Is.Null, "失败要同时清掉变更集页的落点");
        Assert.That(sink.SetCount, Is.EqualTo(1));
    }

    [Test]
    public void ModDetailsTabs_安装抛异常时复位忙碌标记并给出提示()
    {
        var changeSets = new ModTabsFakeChangeSetService
        {
            ThrowOnBuild = new InvalidOperationException("索引损坏"),
        };
        var vm = CreateViewModel(changeSets, rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.1.0")]));

        vm.InstallVersionCommand.ExecuteAsync(vm.Versions[0]).GetAwaiter().GetResult();

        Assert.That(vm.InstallStatusText, Is.EqualTo("生成变更集失败：索引损坏"));
        Assert.That(vm.InstallStatusIsError, Is.True);
        Assert.That(vm.IsInstalling, Is.False, "异常路径也要复位忙碌标记");
        Assert.That(vm.InstallVersionCommand.CanExecute(vm.Versions[0]), Is.True, "异常后按钮要能再点");
    }

    [Test]
    public void ModDetailsTabs_忙碌时禁用安装命令防止连点()
    {
        var changeSets = new ModTabsFakeChangeSetService { Gate = new TaskCompletionSource<bool>() };
        var vm = CreateViewModel(changeSets, rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.1.0")]));
        var version = vm.Versions[0];

        var running = vm.InstallVersionCommand.ExecuteAsync(version);

        Assert.That(vm.IsInstalling, Is.True, "计算期间处于忙碌态");
        Assert.That(vm.InstallVersionCommand.CanExecute(version), Is.False, "忙碌期间按钮禁用");

        changeSets.Gate!.SetResult(true);
        running.GetAwaiter().GetResult();

        Assert.That(vm.IsInstalling, Is.False);
        Assert.That(vm.InstallVersionCommand.CanExecute(version), Is.True);
    }

    [Test]
    public void ModDetailsTabs_没有变更集服务时安装只提示()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.1.0")]));

        vm.InstallVersionCommand.ExecuteAsync(vm.Versions[0]).GetAwaiter().GetResult();

        Assert.That(vm.InstallStatusText, Is.EqualTo("当前没有可用的变更集服务，无法发起安装。"));
        Assert.That(vm.InstallStatusIsError, Is.True);
        Assert.That(vm.HasLastChangeSet, Is.False);
    }

    [Test]
    public void ModDetailsTabs_未选中模组时安装被拦截()
    {
        var changeSets = new ModTabsFakeChangeSetService();
        var vm = CreateViewModel(changeSets, rows: Row());

        vm.InstallVersionCommand.ExecuteAsync(Version("1.1.0")).GetAwaiter().GetResult();

        Assert.That(changeSets.ReceivedRequests, Is.Empty, "没选中模组不该算变更集");
        Assert.That(vm.InstallStatusText, Is.EqualTo("未选中模组，无法发起安装。"));
    }

    [Test]
    public void ModDetailsTabs_版本为null时安装命令直接返回()
    {
        var changeSets = new ModTabsFakeChangeSetService();
        var vm = CreateViewModel(changeSets, rows: Row());
        vm.ShowDetails(Details());

        vm.InstallVersionCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(changeSets.ReceivedRequests, Is.Empty);
        Assert.That(vm.HasInstallStatusText, Is.False, "空参数不该产生任何反馈");
    }

    [Test]
    public void ModDetailsTabs_null快照不影响页签()
    {
        var vm = CreateViewModel(rows: Row());
        vm.ShowDetails(Details(versions: [Version("1.0.0")], installedFiles: ["a.dll"]));
        Assert.That(vm.HasVersions, Is.True);

        vm.ShowDetails(null);

        Assert.That(vm.HasVersions, Is.False, "null 快照要清掉版本列表");
        Assert.That(vm.HasInstalledFiles, Is.False, "null 快照要清掉文件列表");
        Assert.That(vm.SelectedTabIndex, Is.EqualTo(0), "null 快照回到第一个页签");
    }
}
