using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Views;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public class ModsPageDensityAuditTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        HeadlessAppHost.EnsureInitialized();
    }

    [Test]
    public void ModsPage_DefaultToolbarIsSingleRow_CollapsibleFilterDrawerWorks()
    {
        var view = new ModsPageView();
        var vm = new ModsPageViewModel();
        view.DataContext = vm;
        var window = HeadlessAppHost.CreateTestWindow(view, 1280, 800);

        try
        {
            HeadlessAppHost.PumpJobs();

            // 验证默认状态：抽屉收起，不占用多行空间
            Assert.That(vm.IsFiltersExpanded, Is.False, "默认应折叠分类与标签抽屉以最大化模组表格面积");

            // 切换抽屉展开
            vm.ToggleFiltersExpandedCommand.Execute(null);
            Assert.That(vm.IsFiltersExpanded, Is.True, "展开抽屉后应为 true");

            // 再次切换抽屉收起
            vm.ToggleFiltersExpandedCommand.Execute(null);
            Assert.That(vm.IsFiltersExpanded, Is.False, "收起抽屉后应为 false");

            // 测试重置全部筛选
            vm.ResetFiltersCommand.Execute(null);
            Assert.That(vm.HasActiveFilters, Is.False, "重置后所有筛选应清除");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SystemicTypography_MatchesHighDensityStandard()
    {
        var view = new ModsPageView();
        var vm = new ModsPageViewModel();
        view.DataContext = vm;
        var window = HeadlessAppHost.CreateTestWindow(view, 1280, 800);

        try
        {
            HeadlessAppHost.PumpJobs();

            // 验证 Window 根字号为紧凑正文字号 (<= 13)
            Assert.That(window.FontSize, Is.LessThanOrEqualTo(13.0));

            // 验证搜索框字号也是紧凑正文字号 (<= 13)
            var searchBox = view.FindControl<TextBox>("SearchBox");
            Assert.That(searchBox, Is.Not.Null);
            Assert.That(searchBox!.FontSize, Is.LessThanOrEqualTo(13.0));

            // 验证主页面标题字号不超过 18px (原为 26px 虚胖)
            var pageTitles = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("CkanPageTitle")).ToList();
            Assert.That(pageTitles, Is.Not.Empty);
            foreach (var title in pageTitles)
            {
                Assert.That(title.FontSize, Is.LessThanOrEqualTo(18.0), "页面标题字号必须在紧凑标准 18px 之内");
            }
        }
        finally
        {
            window.Close();
        }
    }
}