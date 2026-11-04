using System.Net;
using System.Text;
using CkanUI.Plugin.ModPackShare.Sources;
using CkanUI.Plugin.ModPackShare.UI;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;
using CkanUI.TestSupport;

namespace CkanUI.Plugin.ModPackShare.Tests;

/// <summary>
/// 页面 ViewModel 的行为契约。
/// 每条用例都从「服务返回什么」倒推「界面必须显示什么」：成功要落到配置档目录，失败要变成中文错误并且不留半成品。
/// </summary>
[TestFixture]
internal sealed class ModPackSharePageViewModelTests
{
    private static InstanceInfo Instance(string gameDir) => new(
        "主实例", gameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 12);

    private static InstanceProfileInfo Profile(
        string name,
        bool isActive = false,
        bool isLaunch = false,
        string? source = null,
        int modCount = 0,
        string? root = null)
    {
        var directory = Path.Combine(root ?? Path.GetTempPath(), name);
        return new InstanceProfileInfo(
            name,
            "主实例",
            isActive,
            directory,
            Path.Combine(directory, "GameData"),
            SizeBytes: 2048,
            FileCount: 3,
            LastUsedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        {
            IsLaunchProfile = isLaunch,
            Source = source,
            ModCount = modCount,
        };
    }

    private sealed class Harness : IDisposable
    {
        public Harness(TempWorkspace temp, bool withInstance = true, bool withProfiles = true, bool withModPacks = true)
        {
            Temp = temp;
            Instances = new FakeInstanceService();
            if (withInstance)
            {
                var gameDir = temp.Combine("game");
                Directory.CreateDirectory(gameDir);
                var instance = Instance(gameDir);
                Instances.Current = instance;
                Instances.Instances = [instance];
            }

            Profiles = new FakeInstanceProfileService { ProfileRoot = temp.Combine("CKANProfiles") };
            ModPacks = new FakeModPackService();
            Handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            HttpClient = new HttpClient(Handler);

            Vm = ModPackSharePageViewModel.CreateForTests(
                withProfiles ? Profiles : null,
                Instances,
                withModPacks ? ModPacks : null,
                HttpClient,
                temp.Combine("sources"),
                _ => OpenedPaths.Add(_));
        }

        public TempWorkspace Temp { get; }

        public FakeInstanceService Instances { get; }

        public FakeInstanceProfileService Profiles { get; }

        public FakeModPackService ModPacks { get; }

        public StubHttpHandler Handler { get; }

        public HttpClient HttpClient { get; }

        public ModPackSharePageViewModel Vm { get; }

        public List<string> OpenedPaths { get; } = [];

        public void Dispose()
        {
            Vm.Dispose();
            HttpClient.Dispose();
        }
    }

    [Test]
    public async Task Refresh_WithoutInstance_ShowsExplicitEmptyState()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp, withInstance: false);

        await harness.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.HasInstance, Is.False);
            Assert.That(harness.Vm.ShowEmptyState, Is.True);
            Assert.That(harness.Vm.ShowContent, Is.False);
            Assert.That(harness.Vm.EmptyTitle, Is.EqualTo("还没有选择游戏实例"));
            Assert.That(harness.Vm.InstanceName, Is.EqualTo("未选择实例"));
            Assert.That(harness.Vm.ProfilesDirectoryText, Is.EqualTo("—"));
            Assert.That(harness.Vm.Profiles, Is.Empty);
        });
    }

    [Test]
    public async Task Refresh_WithoutProfiles_ShowsImportHintNotZero()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);

        await harness.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.HasInstance, Is.True);
            Assert.That(harness.Vm.Profiles, Is.Empty);
            Assert.That(harness.Vm.ShowEmptyState, Is.True);
            Assert.That(harness.Vm.ShowEmptyAction, Is.True, "空列表时应当引导用户导入或新建");
            Assert.That(harness.Vm.EmptyTitle, Is.EqualTo("这个实例还没有配置档"));
            Assert.That(harness.Vm.ProfileCountText, Is.EqualTo("还没有配置档"));
            Assert.That(harness.Vm.ProfilesDirectoryText, Does.EndWith("CKANProfiles"));
        });
    }

    [Test]
    public async Task Refresh_WithoutProfileService_ExplainsMissingService()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp, withProfiles: false);

        await harness.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.EmptyTitle, Is.EqualTo("配置档服务不可用"));
            Assert.That(harness.Vm.ShowEmptyState, Is.True);
            Assert.That(harness.Vm.Profiles, Is.Empty);
        });
    }

    [Test]
    public async Task Refresh_WithProfiles_SelectsActiveRowAndFillsCounts()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Profiles.Seed(
            Profile("原版", isActive: true, isLaunch: true, root: temp.Combine("CKANProfiles")),
            Profile("rp1", modCount: 42, source: "kOS-dev/packs/rp1.ckan@dev", root: temp.Combine("CKANProfiles")));

        await harness.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.ShowContent, Is.True);
            Assert.That(harness.Vm.ShowEmptyState, Is.False);
            Assert.That(harness.Vm.Profiles, Has.Count.EqualTo(2));
            Assert.That(harness.Vm.SelectedProfile!.Name, Is.EqualTo("原版"));
            Assert.That(harness.Vm.SelectedProfile.IsActive, Is.True);
            Assert.That(harness.Vm.LaunchProfileName, Is.EqualTo("原版"));
            Assert.That(harness.Vm.ProfileCountText, Is.EqualTo("2 个配置档"));
            Assert.That(harness.Vm.ActiveProfileText, Is.EqualTo("生效：原版"));
            Assert.That(harness.Vm.SwitchHintText, Is.EqualTo("当前已生效"));
            Assert.That(harness.Vm.Profiles[1].ModCountText, Is.EqualTo("42 个模组"));
        });
    }

    [Test]
    public async Task Import_LocalCkan_CreatesProfileWithPackageNameAndCopiesFile()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        var packPath = temp.Combine("下载", "rp1.ckan");
        Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);
        await File.WriteAllTextAsync(packPath, "{\"name\":\"RP-1 整合包\"}");
        harness.ModPacks.Document = new ModPackEditorDocument
        {
            Name = "RP-1 整合包",
            Relations =
            [
                new ModPackRelation { RelationType = ModPackRelationTypes.Depends, Name = "RealFuels" },
                new ModPackRelation { RelationType = ModPackRelationTypes.Depends, Name = "RO" },
                new ModPackRelation { RelationType = ModPackRelationTypes.Recommends, Name = "RP-0" },
                new ModPackRelation { RelationType = ModPackRelationTypes.Suggests, Name = "DistantObject" },
            ],
        };
        harness.Vm.SourceText = packPath;

        await harness.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.ErrorMessage, Is.Empty, harness.Vm.ErrorMessage);
            Assert.That(harness.Profiles.CreateCalls, Has.Count.EqualTo(1));
            var call = harness.Profiles.CreateCalls[0];
            Assert.That(call.InstanceName, Is.EqualTo("主实例"));
            Assert.That(call.ProfileName, Is.EqualTo("RP-1 整合包"), "配置档名应当取 .ckan 里的 name");
            Assert.That(call.AdoptCurrent, Is.False, "导入不应接管当前状态");
            Assert.That(call.Source, Is.EqualTo(packPath), "来源原文要原样记录下来");
            Assert.That(
                File.Exists(Path.Combine(harness.Profiles.ProfileRoot, "RP-1 整合包", "modpack.ckan")),
                Is.True,
                "下载的 .ckan 必须复制进配置档目录");
            Assert.That(harness.Vm.SelectedProfile!.Name, Is.EqualTo("RP-1 整合包"));
            Assert.That(harness.Vm.StatusMessage, Does.Contain("3 个模组"), "depends + recommends 才是模组数");
        });
    }

    [Test]
    public async Task Import_GitHubUrl_DownloadsThenCreatesProfile()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        var handler = new StubHttpHandler(request => request.RequestUri!.AbsoluteUri.Contains("/main/")
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"name\":\"CKAN 整合包\"}", Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var vm = ModPackSharePageViewModel.CreateForTests(
            harness.Profiles, harness.Instances, harness.ModPacks, client, temp.Combine("sources"), _ => { });
        using (vm)
        {
            vm.SourceText = "kOS-dev/packs";
            await vm.ImportCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(vm.ErrorMessage, Is.Empty, vm.ErrorMessage);
                Assert.That(handler.Requests, Has.Count.EqualTo(1), "main 命中后不应再试 master");
                Assert.That(harness.Profiles.CreateCalls, Has.Count.EqualTo(1));
                Assert.That(harness.Profiles.CreateCalls[0].Source, Is.EqualTo("kOS-dev/packs"));
            });
        }
    }

    [Test]
    public async Task Import_AllCandidates404_ShowsChineseErrorAndCreatesNothing()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Vm.SourceText = "kOS-dev/packs";

        await harness.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("导入失败"));
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("下载"));
            Assert.That(harness.Profiles.CreateCalls, Is.Empty, "下载失败不应创建配置档");
            Assert.That(harness.Vm.StatusMessage, Is.Empty);
            Assert.That(harness.Vm.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task Import_NonJsonBody_ShowsChineseErrorAndCreatesNothing()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html/>", Encoding.UTF8, "text/html"),
        });
        using var client = new HttpClient(handler);
        var vm = ModPackSharePageViewModel.CreateForTests(
            harness.Profiles, harness.Instances, harness.ModPacks, client, temp.Combine("sources"), _ => { });
        using (vm)
        {
            vm.SourceText = "https://example.com/packs/rp1.ckan";
            await vm.ImportCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(vm.ErrorMessage, Does.Contain("不是 .ckan 元数据包"));
                Assert.That(harness.Profiles.CreateCalls, Is.Empty);
            });
        }
    }

    [Test]
    public async Task Import_Over8MiB_ShowsLimitErrorAndCreatesNothing()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(GitHubModPackSource.MaxBytes + 4096),
        });
        using var client = new HttpClient(handler);
        var vm = ModPackSharePageViewModel.CreateForTests(
            harness.Profiles, harness.Instances, harness.ModPacks, client, temp.Combine("sources"), _ => { });
        using (vm)
        {
            vm.SourceText = "https://example.com/packs/rp1.ckan";
            await vm.ImportCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(vm.ErrorMessage, Does.Contain("8 MiB"));
                Assert.That(harness.Profiles.CreateCalls, Is.Empty);
            });
        }
    }

    [Test]
    public async Task Import_WhenGameRunning_IsRejectedBeforeDownload()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Instances.GameRunning = true;
        harness.Vm.SourceText = "kOS-dev/packs";

        await harness.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("游戏正在运行"));
            Assert.That(harness.Handler.Requests, Is.Empty);
            Assert.That(harness.Profiles.CreateCalls, Is.Empty);
        });
    }

    [Test]
    public async Task Import_WhenServiceRejects_ShowsServiceMessage()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Profiles.CreateResult =
            OperationResult<InstanceProfileInfo>.Fail("同名配置档已存在。");
        var packPath = temp.Combine("rp1.ckan");
        await File.WriteAllTextAsync(packPath, "{\"name\":\"rp1\"}");
        harness.Vm.SourceText = packPath;

        await harness.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("同名配置档已存在"));
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("导入失败"));
        });
    }

    [Test]
    public async Task Switch_CallsServiceWithSelectedNameAndRefreshes()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Profiles.Seed(
            Profile("原版", isActive: true, root: temp.Combine("CKANProfiles")),
            Profile("rp1", root: temp.Combine("CKANProfiles")));
        await harness.Vm.RefreshCommand.ExecuteAsync(null);
        var listCallsBefore = harness.Profiles.ListCallCount;
        harness.Vm.SelectedProfile = harness.Vm.Profiles[1];

        await harness.Vm.SwitchCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Profiles.SwitchCalls, Has.Count.EqualTo(1));
            Assert.That(harness.Profiles.SwitchCalls[0].InstanceName, Is.EqualTo("主实例"));
            Assert.That(harness.Profiles.SwitchCalls[0].ProfileName, Is.EqualTo("rp1"));
            Assert.That(harness.Vm.ErrorMessage, Is.Empty, harness.Vm.ErrorMessage);
            Assert.That(harness.Vm.StatusMessage, Does.Contain("已切到「rp1」"));
            Assert.That(harness.Vm.StatusMessage, Does.Contain("重新打开游戏"));
            Assert.That(harness.Profiles.ListCallCount, Is.GreaterThan(listCallsBefore), "切换后必须刷新列表");
            Assert.That(harness.Vm.SelectedProfile!.Name, Is.EqualTo("rp1"));
            Assert.That(harness.Vm.SelectedProfile.IsActive, Is.True);
        });
    }

    [Test]
    public async Task Switch_WhenServiceFails_ShowsChineseError()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Profiles.Seed(Profile("rp1", root: temp.Combine("CKANProfiles")));
        await harness.Vm.RefreshCommand.ExecuteAsync(null);
        harness.Profiles.SwitchResult = OperationResult<InstanceProfileSwitchResult>.Fail("目标目录被占用。");

        await harness.Vm.SwitchCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("切换失败"));
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("目标目录被占用"));
        });
    }

    [Test]
    public async Task Delete_WhenActiveProfileRejected_ShowsServiceReason()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Profiles.Seed(Profile("原版", isActive: true, root: temp.Combine("CKANProfiles")));
        await harness.Vm.RefreshCommand.ExecuteAsync(null);
        harness.Profiles.DeleteResult = OperationResult<bool>.Fail("不能删除当前生效的配置档，先切到别的档。");

        await harness.Vm.DeleteCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Profiles.DeleteCalls, Has.Count.EqualTo(1));
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("删除失败"));
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("当前生效"));
        });
    }

    [Test]
    public async Task Delete_Success_RemovesRowAndRefreshes()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Profiles.Seed(
            Profile("原版", isActive: true, root: temp.Combine("CKANProfiles")),
            Profile("rp1", root: temp.Combine("CKANProfiles")));
        await harness.Vm.RefreshCommand.ExecuteAsync(null);
        harness.Vm.SelectedProfile = harness.Vm.Profiles[1];

        await harness.Vm.DeleteCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.Profiles, Has.Count.EqualTo(1));
            Assert.That(harness.Vm.StatusMessage, Does.Contain("已删除「rp1」"));
        });
    }

    [Test]
    public async Task SetLaunch_PassesSelectedName_AndClearLaunchPassesNull()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Profiles.Seed(Profile("rp1", root: temp.Combine("CKANProfiles")));
        await harness.Vm.RefreshCommand.ExecuteAsync(null);

        await harness.Vm.SetLaunchCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Profiles.SetLaunchCalls, Has.Count.EqualTo(1));
            Assert.That(harness.Profiles.SetLaunchCalls[0].InstanceName, Is.EqualTo("主实例"));
            Assert.That(harness.Profiles.SetLaunchCalls[0].ProfileName, Is.EqualTo("rp1"));
            Assert.That(harness.Vm.LaunchProfileName, Is.EqualTo("rp1"));
            Assert.That(harness.Vm.HasLaunchProfile, Is.True);
        });

        await harness.Vm.ClearLaunchCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Profiles.SetLaunchCalls, Has.Count.EqualTo(2));
            Assert.That(harness.Profiles.SetLaunchCalls[1].ProfileName, Is.Null, "清除要传 null");
            Assert.That(harness.Vm.LaunchProfileName, Is.EqualTo("—"));
            Assert.That(harness.Vm.HasLaunchProfile, Is.False);
            Assert.That(harness.Vm.StatusMessage, Does.Contain("已清除启动配置档"));
        });
    }

    [Test]
    public async Task CreateFromCurrent_AdoptsCurrentStateWithGivenName()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);
        harness.Vm.NewProfileName = "原版";

        await harness.Vm.CreateFromCurrentCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Profiles.CreateCalls, Has.Count.EqualTo(1));
            Assert.That(harness.Profiles.CreateCalls[0].ProfileName, Is.EqualTo("原版"));
            Assert.That(harness.Profiles.CreateCalls[0].AdoptCurrent, Is.True);
            Assert.That(harness.Vm.Profiles, Has.Count.EqualTo(1));
            Assert.That(harness.Vm.StatusMessage, Does.Contain("接管当前 GameData"));
        });
    }

    [Test]
    public async Task OpenFolder_UsesProfilesDirectoryAndReportsMissingOne()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        using var harness = new Harness(temp);

        await harness.Vm.RefreshCommand.ExecuteAsync(null);
        harness.Vm.OpenFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Vm.ErrorMessage, Does.Contain("配置档目录还不存在"));
            Assert.That(harness.OpenedPaths, Is.Empty);
        });

        var directory = Path.Combine(harness.Instances.Current!.GameDir, "CKANProfiles");
        Directory.CreateDirectory(directory);
        harness.Vm.OpenFolderCommand.Execute(null);

        Assert.That(harness.OpenedPaths, Has.Count.EqualTo(1));
        Assert.That(harness.OpenedPaths[0], Is.EqualTo(directory));
    }

    [Test]
    public void Dispose_IsIdempotent_AndStopsReactingToInstanceEvents()
    {
        using var temp = new TempWorkspace("modpackshare-vm");
        var harness = new Harness(temp);
        var vm = harness.Vm;

        vm.Dispose();
        vm.Dispose();

        var listCalls = harness.Profiles.ListCallCount;
        harness.Instances.RaiseCurrentChanged(harness.Instances.Current);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Profiles.ListCallCount, Is.EqualTo(listCalls), "释放后不应再响应实例事件");
            Assert.That(vm.Profiles, Is.Empty);
        });

        vm.Refresh();
        vm.Start();
        Assert.That(harness.Profiles.ListCallCount, Is.EqualTo(listCalls));
    }
}
