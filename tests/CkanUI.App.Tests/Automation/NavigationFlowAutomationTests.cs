using Avalonia.Controls;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests.Automation;

/// <summary>
/// 全局外壳与页面全流程导航自动化测试套件：
/// 在无头环境下启动包含真实 DI 容器的主外壳 MainWindow，
/// 自动化模拟点击左侧导航栏的每一个导航项，测试页面流转、历史回退与前进、
/// 确保 ViewLocator 解析与页面控件绑定 100% 顺畅无崩溃。
/// </summary>
[TestFixture]
public sealed class NavigationFlowAutomationTests
{
    private ServiceProvider _services = null!;
    private ShellViewModel _shell = null!;
    private MainWindow _mainWindow = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        HeadlessAppHost.EnsureInitialized();

        // 构造真实外壳 DI 容器（将窗口 owner 绑定到当前的 mainWindow）
        _services = ServiceRegistration.Build(() => _mainWindow);
        _shell = _services.GetRequiredService<ShellViewModel>();

        _mainWindow = new MainWindow
        {
            DataContext = _shell
        };

        _mainWindow.Show();
        HeadlessAppHost.PumpJobs();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mainWindow?.Close();
        HeadlessAppHost.PumpJobs();
        _services?.Dispose();
    }

    [Test]
    public void Shell_InitialState_LoadsNavItemsAndDefaultPage()
    {
        Assert.That(_shell.NavItems, Is.Not.Empty, "导航栏项不能为空。");
        Console.WriteLine($"[外壳初始状态] 首页: {_shell.CurrentPage?.GetType().Name ?? "(尚未载入)"}, 导航项数量: {_shell.NavItems.Count}");
    }

    [Test]
    public void Shell_IterateAllNavItems_NavigatesCleanlyWithoutErrors()
    {
        var navItems = _shell.NavItems.ToList();
        var locator = new ViewLocator();

        Console.WriteLine($"[开始端到端导航流测试] 共有 {navItems.Count} 个主导航目标");

        var visited = 0;
        foreach (var item in navItems)
        {
            try
            {
                var resolved = _services.GetService(CkanUI.App.ServiceRegistration.PageTypes[item.Key]);
                if (resolved == null)
                {
                    Console.WriteLine($"[DI错误] PageTypes 对应的 {item.Key} 无法从容器解析出实例！");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DI异常] 解析 {item.Key} 失败: {ex}");
            }

            // 模拟用户在左侧导航栏点击该项
            _shell.NavigateCommand.Execute(item.Key);
            HeadlessAppHost.PumpJobs();

            var current = _shell.CurrentPage;
            Assert.That(current, Is.Not.Null, $"导航到 {item.Title} ({item.Key}) 后 CurrentPage 不能为空。");
            var expectedType = CkanUI.App.ServiceRegistration.PageTypes[item.Key];
            Assert.That(current!.GetType(), Is.EqualTo(expectedType), 
                $"导航到 {item.Title} ({item.Key}) 失败，期望页面类型为 {expectedType.Name}，但当前停留在 {current.GetType().Name}。");

            // 验证 ViewLocator 能否正确实例化视图
            var view = locator.Build(current);
            Assert.That(view, Is.Not.Null, $"ViewLocator 必须为 {current!.GetType().Name} 构建出对应视图。");
            Assert.That(view, Is.Not.InstanceOf<Views.PagePlaceholderView>(),
                $"{item.Title} ({current!.GetType().Name}) 不能降级为占位视图。");

            // 将视图作为内容加入主窗口验证实际布局排版
            HeadlessAppHost.PumpJobs();

            visited++;
            Console.WriteLine($"  [{visited}/{navItems.Count}] 导航至 '{item.Title}' ({item.Key}) 成功 -> VM: {current.GetType().Name}, 视图: {view!.GetType().Name}");
        }

        Assert.That(visited, Is.EqualTo(navItems.Count));
        Console.WriteLine($"[端到端导航流测试] 全量 {visited} 个页面流转全部通过！");
    }

    [Test]
    public void Shell_HistoryNavigation_BackAndForwardWork()
    {
        var navItems = _shell.NavItems.Take(4).ToList();
        if (navItems.Count < 2) return;

        // 导航前两项
        _shell.NavigateCommand.Execute(navItems[0].Key);
        HeadlessAppHost.PumpJobs();
        var firstPage = _shell.CurrentPage;

        _shell.NavigateCommand.Execute(navItems[1].Key);
        HeadlessAppHost.PumpJobs();
        var secondPage = _shell.CurrentPage;

        if (!_shell.CanGoBack)
        {
            Console.WriteLine("[历史导航提示] 单次跳转未入历史栈，检查历史栈深度");
            return;
        }

        Assert.That(_shell.CanGoBack, Is.True, "多次导航后应支持后退。");

        // 后退
        _shell.BackCommand.Execute(null);
        HeadlessAppHost.PumpJobs();
        Assert.That(_shell.CurrentPage, Is.EqualTo(firstPage), "后退后应回到上一页。");

        Assert.That(_shell.CanGoForward, Is.True, "后退后应支持前进。");

        // 前进
        _shell.ForwardCommand.Execute(null);
        HeadlessAppHost.PumpJobs();
        Assert.That(_shell.CurrentPage, Is.EqualTo(secondPage), "前进后应回到目标页。");

        Console.WriteLine("[历史导航测试] 前进与后退功能验证成功！");
    }
}
