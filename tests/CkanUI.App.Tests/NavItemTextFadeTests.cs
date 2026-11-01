using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;
using System.Linq;

namespace CkanUI.App.Tests;

/// <summary>
/// 侧栏精简收起态（52px）防文字泄露与平滑淡入淡出测试。
/// 验证收起态下导航项文字不会在图标右边缘泄漏半截残影，且展开时能正常呈现。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class NavItemTextFadeTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void NavItems_InCollapsedState_HaveZeroTextOpacity()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var nav = window.FindControl<FANavigationView>("NavView");
            Assert.That(nav, Is.Not.Null);
            Assert.That(nav!.Classes.Contains("Collapsed"), Is.True, "默认应为 Collapsed 状态");

            var nviList = nav.MenuItems.Concat(nav.FooterMenuItems).OfType<FANavigationViewItem>().ToList();
            Assert.That(nviList.Count, Is.GreaterThan(0));

            foreach (var item in nviList)
            {
                var contentPresenter = item.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .FirstOrDefault(cp => cp.Name == "ContentPresenter");

                Assert.That(contentPresenter, Is.Not.Null);
                Assert.That(contentPresenter!.Opacity, Is.EqualTo(0),
                    $"收起精简模式下，项 [{item.Content}] 的文字 ContentPresenter 必须完全淡出（Opacity=0），杜绝边缘泄露残影。");
            }
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void NavItems_InExpandedState_HaveFullTextOpacity()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            // 切换为展开态
            vm.ToggleNavCollapsedCommand.Execute(null);
            var nav = window.FindControl<FANavigationView>("NavView");
            Assert.That(nav, Is.Not.Null);

            // 推进 Dispatcher 任务 + 渲染帧：透明度走 DoubleTransition，只有渲染时钟推进时才会从 0 走到 1
            for (var i = 0; i < 40; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                HeadlessAppHost.PumpJobs();
                System.Threading.Thread.Sleep(20);
                HeadlessAppHost.PumpJobs();
            }

            var nviList = nav!.MenuItems.Concat(nav.FooterMenuItems).OfType<FANavigationViewItem>().ToList();
            Assert.That(nviList.Count, Is.GreaterThan(0));

            Assert.That(nav.Classes.Contains("PaneOpen"), Is.True, "展开后应为 PaneOpen 状态");
            Assert.That(nviList.Count, Is.GreaterThan(0));

            foreach (var item in nviList)
            {
                var contentPresenter = item.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .FirstOrDefault(cp => cp.Name == "ContentPresenter");

                Assert.That(contentPresenter, Is.Not.Null);
                Assert.That(contentPresenter!.Opacity, Is.GreaterThan(0),
                    $"展开模式下，项 [{item.Content}] 的文字 ContentPresenter 必须处于可见或淡入状态（实测 Opacity = {contentPresenter.Opacity}）。");
            }
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
