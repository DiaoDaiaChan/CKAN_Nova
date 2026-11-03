using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CkanUI.App.Plugins;
using CkanUI.App.Tests.Automation;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Plugins;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 「插件激活后，导航侧栏必须真的长出新入口」这条契约的界面侧验证。
/// </summary>
/// <remarks>
/// <para>只断言视图模型是不够的：<c>ShellViewModel.NavItems</c> 里有 performance、而
/// <c>FANavigationView.MenuItems</c> 里没有，正好是插件页签"看起来不存在"的真实故障形态
/// （docs/design/plugins.md §11.7）。这条必须穿过 <see cref="MainWindow"/> 的 <c>RebuildNavigation</c>。</para>
/// <para>两条用例都刻意不 <c>Show()</c>、不泵 dispatcher：headless 宿主用的是真 Skia 渲染，
/// 强行驱动帧会把队列排不空（§11.7 记过一次挂死）。真机侧的渲染证据来自自动化枚举。</para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class PluginNavRailTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    /// <summary>
    /// 侧栏渲染的是 <c>RailItems</c>：只订阅 <c>NavItems</c> 时，插件激活后视图会用旧
    /// <c>RailItems</c> 重建一次，**之后没人再重建**——用户永远看不到「性能」。
    /// </summary>
    [Test]
    public void 侧栏跟随RailItems重建并给插件页补上扩展分组()
    {
        var shell = new ShellViewModel(new SilentNavigation());
        var window = new MainWindow { DataContext = shell };

        var navView = window.FindControl<FANavigationView>("NavView");
        Assert.That(navView, Is.Not.Null, "主窗口里必须找到 NavView");

        var before = MenuContents(navView!);
        Assert.That(before, Does.Contain("主页"), "初始导航必须有内置项；实际：" + string.Join(" / ", before));
        Assert.That(before, Does.Not.Contain("性能"), "插件没激活时不该有性能入口；实际：" + string.Join(" / ", before));

        // 真机上这条变化由 RefreshRailItems 在插件激活后触发（先改 NavItems、最后重填 RailItems），
        // 这里直接改同一个集合，钉住"视图订阅的是 RailItems"这条接线。
        shell.RailItems.Add(new NavItem("performance", "性能", "Puzzle"));

        var after = MenuContents(navView!);
        Assert.That(
            after,
            Does.Contain("性能"),
            "RailItems 变了侧栏就必须重建（只订阅 NavItems 的写法在这里会失败）；实际：" + string.Join(" / ", after));

        var headerIndex = after.IndexOf("[扩展]");
        Assert.That(headerIndex, Is.GreaterThanOrEqualTo(0), "插件自述页必须归入「扩展」分组；实际：" + string.Join(" / ", after));
        Assert.That(
            after.IndexOf("性能"),
            Is.EqualTo(headerIndex + 1),
            "「性能」必须紧跟在「扩展」标题之后，否则侧栏顺序会被悄悄打乱；实际：" + string.Join(" / ", after));
    }

    /// <summary>
    /// 侧栏有入口只是第一步：点进去必须真的拿到插件自带的页面视图，而不是占位视图。
    /// </summary>
    /// <remarks>
    /// 这条钉的是 <see cref="ViewLocator"/> 的插件分支：插件程序集加载在可回收 ALC 里，
    /// <c>Type.GetType</c> 按名字查不到，只有"在视图模型自己的程序集里找同名 View"这条路径能通。
    /// 一旦它断，用户看到的是 <c>PagePlaceholderView</c>——侧栏有「性能」、页面却是空白，
    /// 这正是"插件系统只是占位符"的旧故障形态。
    /// </remarks>
    [Test]
    public void 性能页的视图模型能被视图解析器渲染成插件自带视图()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        if (!System.IO.File.Exists(System.IO.Path.Combine(pluginDirectory, "CkanUI.Plugin.Performance.dll")))
        {
            Assert.Ignore("还没构建 CkanUI.Plugin.Performance，跳过视图解析验证。");
        }

        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ckanui-user-" + Guid.NewGuid().ToString("N"));
        var storePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ckanui-activation-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            // 插件加载放在线程池上跑、用阻塞等待收口：测试线程挂着 Avalonia 的 SynchronizationContext，
            // 用 await 收尾会等一个没人泵的 dispatcher（历史上就是这么挂死的）。
            var discovered = Task.Run(() => service.DiscoverAsync(activateBundled: true)).GetAwaiter().GetResult();
            Assert.That(discovered.IsSuccess, Is.True, discovered.Message);
            Assert.That(service.Active, Has.Count.EqualTo(1));

            var descriptor = service.Active[0].Pages[0];
            var viewModelType = PluginPageFactory.ResolveViewModelType(descriptor.ViewModelTypeName);
            Assert.That(viewModelType, Is.Not.Null, "插件页签声明的视图模型类型必须能跨 ALC 解析");
            var resolvedType = viewModelType!;
            Assert.That(typeof(IPluginPage).IsAssignableFrom(resolvedType), Is.True, "插件页的视图模型必须实现 IPluginPage");

            var viewType = ViewLocator.ResolveViewType(resolvedType);
            Assert.That(viewType, Is.Not.Null, "视图解析器必须能在插件程序集里找到同名 View");
            Assert.That(viewType!.FullName, Is.EqualTo("CkanUI.Plugin.Performance.PerformancePageView"));

            var viewModel = Activator.CreateInstance(resolvedType);
            Assert.That(viewModel, Is.Not.Null);
            var view = new ViewLocator().Build(viewModel);
            Assert.That(view, Is.Not.Null);
            Assert.That(
                view,
                Is.Not.InstanceOf<CkanUI.App.Views.PagePlaceholderView>(),
                "插件页必须有真视图；落到占位视图等于插件系统还是占位符");
            Assert.That(view!.GetType().FullName, Is.EqualTo("CkanUI.Plugin.Performance.PerformancePageView"));
            Assert.That(view.DataContext, Is.SameAs(viewModel));

            (viewModel as IDisposable)?.Dispose();
            var pluginId = service.Active[0].Id;
            Task.Run(() => service.UnloadAsync(pluginId)).GetAwaiter().GetResult();
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    private static List<string> MenuContents(FANavigationView navView)
    {
        var result = new List<string>();
        foreach (var item in navView.MenuItems)
        {
            switch (item)
            {
                case FANavigationViewItem nav when nav.Content is string text:
                    result.Add(text);
                    break;
                case FANavigationViewItemHeader header when header.Content is string text:
                    result.Add("[" + text + "]");
                    break;
            }
        }

        return result;
    }

    private static string StageBundledDirectory(string pluginDirectory)
    {
        var staged = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ckanui-bundled-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(staged);
        foreach (var extension in new[] { ".dll", ".pdb" })
        {
            var source = System.IO.Path.Combine(pluginDirectory, "CkanUI.Plugin.Performance" + extension);
            if (System.IO.File.Exists(source))
            {
                System.IO.File.Copy(source, System.IO.Path.Combine(staged, System.IO.Path.GetFileName(source)));
            }
        }

        return staged;
    }

    private static string ResolvePluginOutputDirectory()
    {
        var testDirectory = System.IO.Path.GetDirectoryName(typeof(PluginNavRailTests).Assembly.Location)!;
        var configuration = System.IO.Directory.GetParent(testDirectory)?.Name ?? "Debug";
        var repoRoot = FindRepoRoot(testDirectory);
        return System.IO.Path.Combine(repoRoot, "src", "CkanUI.Plugin.Performance", "bin", configuration, "net10.0");
    }

    private static string FindRepoRoot(string start)
    {
        var current = start;
        while (current is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current, "ckan-ui.sln")))
            {
                return current;
            }

            current = System.IO.Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("找不到 ckan-ui.sln 所在目录：" + start);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, recursive: true);
            }
            else if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (System.IO.IOException)
        {
        }
    }

}
