using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 深链路由器用例（ARC-05）：ckan://mod|instance|repo 三种落点、退化路径与取消。
/// </summary>
/// <remarks>
/// 只测视图模型层的路由器：ckan:// 的解析在 Platform 层（CkanUri）由 App 层完成，
/// 这里从 <c>(kind, value)</c> 起步，正好覆盖「解析完之后的落点逻辑」。
/// </remarks>
[TestFixture]
public sealed class DeepLinkRouterTests
{
    private DeepLinkNavigation _navigation = null!;

    [SetUp]
    public void SetUp() => _navigation = new DeepLinkNavigation();

    private DeepLinkRouter CreateRouter(IInstanceService? instances = null, Func<string, object?>? pages = null) =>
        new(_navigation, instances, pages);

    [Test]
    public async Task Dispatch_KindIsNull_IsIgnoredWithChineseReason()
    {
        var outcome = await CreateRouter().DispatchAsync(null, "KerbalEngineer");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(outcome.Summary, Does.Contain("缺少命令类型"));
        Assert.That(_navigation.Keys, Is.Empty);
    }

    [Test]
    public async Task Dispatch_ValueIsBlank_IsIgnored()
    {
        var outcome = await CreateRouter().DispatchAsync("mod", "   ");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(outcome.Summary, Does.Contain("缺少取值"));
        Assert.That(_navigation.Keys, Is.Empty);
    }

    [Test]
    public async Task Dispatch_UnknownKind_IsIgnoredAndNamesTheKind()
    {
        var outcome = await CreateRouter().DispatchAsync("plugin", "KerbalEngineer");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(outcome.Summary, Does.Contain("plugin"));
        Assert.That(_navigation.Keys, Is.Empty);
    }

    [Test]
    public async Task Dispatch_Mod_TrimsAndLowerCasesKindAndNavigatesToMods()
    {
        var page = new ModsPageViewModel();

        var outcome = await CreateRouter(pages: key => key == "mods" ? page : null)
            .DispatchAsync("  MOD  ", "  KerbalEngineer  ");

        Assert.That(_navigation.Keys, Is.EqualTo(new[] { "mods" }));
        Assert.That(page.SearchText, Is.EqualTo("KerbalEngineer"), "未命中时标识符要落进搜索框。");
        Assert.That(outcome.Handled, Is.False, "空索引里定位不到具体行，只能算未落点。");
        Assert.That(outcome.Summary, Does.Contain("搜索框"));
    }

    [Test]
    public async Task Dispatch_Mod_WhenPageResolverMisses_StillNavigatesAndReportsNotReady()
    {
        var outcome = await CreateRouter().DispatchAsync("mod", "KerbalEngineer");

        Assert.That(_navigation.Keys, Is.EqualTo(new[] { "mods" }));
        Assert.That(outcome.Handled, Is.False);
        Assert.That(outcome.Summary, Does.Contain("尚未就绪"));
    }

    [Test]
    public async Task Dispatch_Mod_WhenResolverReturnsForeignType_DoesNotThrow()
    {
        var outcome = await CreateRouter(pages: _ => new object()).DispatchAsync("mod", "KerbalEngineer");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(outcome.Summary, Does.Contain("尚未就绪"));
    }

    [Test]
    public async Task Dispatch_Instance_MatchesNameIgnoringCaseAndNavigatesToInstances()
    {
        var instances = new InstancesFakeService();
        instances.Seed(new InstanceInfo("Default", "/game/a", "1.12.5", true, false, 3));

        var outcome = await CreateRouter(instances).DispatchAsync("instance", "default");

        Assert.That(outcome.Handled, Is.True);
        Assert.That(instances.LastSwitchName, Is.EqualTo("Default"));
        Assert.That(_navigation.Keys, Is.EqualTo(new[] { "instances" }));
        Assert.That(outcome.Summary, Does.Contain("Default"));
    }

    [Test]
    public async Task Dispatch_Instance_NotFoundDoesNotSwitchNorNavigate()
    {
        var instances = new InstancesFakeService();
        instances.Seed(new InstanceInfo("Default", "/game/a", "1.12.5", true, false, 3));

        var outcome = await CreateRouter(instances).DispatchAsync("instance", "第二实例");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(instances.SwitchCalls, Is.Zero);
        Assert.That(_navigation.Keys, Is.Empty);
        Assert.That(outcome.Summary, Does.Contain("找不到"));
    }

    [Test]
    public async Task Dispatch_Instance_LockedInstanceReportsSwitchFailure()
    {
        var instances = new InstancesFakeService();
        instances.Seed(new InstanceInfo("Default", "/game/a", "1.12.5", true, true, 3));

        var outcome = await CreateRouter(instances).DispatchAsync("instance", "Default");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(instances.SwitchCalls, Is.EqualTo(1));
        Assert.That(_navigation.Keys, Is.Empty, "切换失败就不该落到实例页。");
        Assert.That(outcome.Summary, Does.Contain("锁定"));
    }

    [Test]
    public async Task Dispatch_Instance_WithoutInstanceServiceIsIgnored()
    {
        var outcome = await CreateRouter(instances: null).DispatchAsync("instance", "Default");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(outcome.Summary, Does.Contain("实例服务"));
        Assert.That(_navigation.Keys, Is.Empty);
    }

    [Test]
    public async Task Dispatch_Repo_SelectsRowByName()
    {
        var page = CreateReposPage(new RepositoryEntryInfo(
            "default", "https://example.invalid/ckan/index.tar.gz", 0, null, false, false, true, false));

        var outcome = await CreateRouter(pages: key => key == "repos" ? page : null)
            .DispatchAsync("repo", "DEFAULT");

        Assert.That(_navigation.Keys, Is.EqualTo(new[] { "repos" }));
        Assert.That(page.SelectedRow?.Name, Is.EqualTo("default"));
        Assert.That(outcome.Handled, Is.True);
    }

    [Test]
    public async Task Dispatch_Repo_SelectsRowByUri()
    {
        const string Uri = "https://example.invalid/ckan/index.tar.gz";
        var page = CreateReposPage(new RepositoryEntryInfo(
            "default", Uri, 0, null, false, false, true, false));

        var outcome = await CreateRouter(pages: key => key == "repos" ? page : null)
            .DispatchAsync("repo", Uri);

        Assert.That(page.SelectedRow?.Uri, Is.EqualTo(Uri));
        Assert.That(outcome.Handled, Is.True);
    }

    [Test]
    public async Task Dispatch_Repo_WithoutMatchIsIgnored()
    {
        var page = CreateReposPage();

        var outcome = await CreateRouter(pages: key => key == "repos" ? page : null)
            .DispatchAsync("repo", "missing");

        Assert.That(outcome.Handled, Is.False);
        Assert.That(page.SelectedRow, Is.Null);
        Assert.That(outcome.Summary, Does.Contain("没有匹配"));
    }

    [Test]
    public async Task Dispatch_CanceledTokenIsIgnoredInsteadOfThrowing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var page = new ModsPageViewModel();
        var outcome = await CreateRouter(pages: key => key == "mods" ? page : null)
            .DispatchAsync("mod", "KerbalEngineer", cts.Token);

        Assert.That(outcome.Handled, Is.False);
        Assert.That(outcome.Summary, Does.Contain("已取消"));
    }

    [Test]
    public void OutcomeFactories_CarryHandledFlag()
    {
        Assert.That(DeepLinkOutcome.Done("ok").Handled, Is.True);
        Assert.That(DeepLinkOutcome.Ignored("nope").Handled, Is.False);
    }

    private static ReposPageViewModel CreateReposPage(params RepositoryEntryInfo[] rows)
    {
        var repositories = new ReposFakeRepositoryService();
        repositories.Rows.AddRange(rows);
        return new ReposPageViewModel(repositories, new ReposFakeErrorReporter(), new ReposFakeDialogService());
    }

    /// <summary>记录导航调用顺序的替身；页面键即本用例唯一关心的东西。</summary>
    private sealed class DeepLinkNavigation : INavigationService
    {
        private readonly List<string> _keys = new();

        public IReadOnlyList<string> Keys => _keys;

        public object? CurrentPage => null;

        public string CurrentKey => string.Empty;

        public event Action<object?, string>? Navigated
        {
            add { }
            remove { }
        }

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _keys.Add(pageKey);
            return Task.FromResult(true);
        }
    }
}
