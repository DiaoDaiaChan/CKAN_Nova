using System.Collections.Generic;
using System.Linq;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 外壳的页签与侧栏循环切换（UI-12）：Ctrl+Tab / Ctrl+Alt+↑↓ / Ctrl+1..9 与手柄肩键共用的入口。
/// 独立成文件而不是塞进 ShellViewModelTests —— 那边的用例在并发期被多人改，减少冲突面。
/// </summary>
[TestFixture]
public sealed class ShellNavigationCycleTests
{
    /// <summary>工厂必须给出非空页面：NavigationService 在页面为 null 时不算导航成功，也就不发 Navigated。</summary>
    private static ShellViewModel CreateShell(out List<string> requested)
    {
        var keys = new List<string>();
        requested = keys;
        var navigation = new NavigationService(key =>
        {
            keys.Add(key);
            return new object();
        });
        return new ShellViewModel(navigation, key => "T:" + key);
    }

    [Test]
    public void CycleSectionTab_Next_MovesToTheNextTabInTheSameSection()
    {
        var vm = CreateShell(out var requested);
        vm.NavigateCommand.Execute("mods");

        Assert.That(vm.CycleSectionTab(1), Is.True);

        Assert.That(requested[^1], Is.EqualTo("content"), "模组分节的页签顺序由槽位表定死，下一页必须是「内容」。");
        Assert.That(vm.SectionTabs.Count(tab => tab.IsActive), Is.EqualTo(1), "同一时刻只能有一个页签处于激活态。");
    }

    [Test]
    public void CycleSectionTab_Previous_WrapsAroundToTheLastTab()
    {
        var vm = CreateShell(out var requested);
        vm.NavigateCommand.Execute("mods");

        Assert.That(vm.CycleSectionTab(-1), Is.True);

        Assert.That(requested[^1], Is.EqualTo(vm.SectionTabs[^1].Key), "在第一个页签上往回切，要绕到最后一个。");
    }

    [Test]
    public void CycleSectionTab_Next_FromTheLastTab_WrapsToTheFirst()
    {
        var vm = CreateShell(out var requested);
        vm.NavigateCommand.Execute("mods");
        var last = vm.SectionTabs[^1].Key;
        vm.NavigateCommand.Execute(last);

        Assert.That(vm.CycleSectionTab(1), Is.True);

        Assert.That(requested[^1], Is.EqualTo(vm.SectionTabs[0].Key));
    }

    [Test]
    public void CycleSectionTab_OnASingleTabPage_IsANoOp()
    {
        var vm = CreateShell(out var requested);
        vm.NavigateCommand.Execute("home");
        var countBefore = requested.Count;

        Assert.That(vm.SectionTabs, Has.Count.EqualTo(1), "只有一页时不该出现页签条。");
        Assert.That(vm.CycleSectionTab(1), Is.False);
        Assert.That(requested, Has.Count.EqualTo(countBefore), "不能为了「切页签」把用户从当前页导航走。");
    }

    [Test]
    public void CycleSectionTab_ZeroDelta_IsANoOp()
    {
        var vm = CreateShell(out var requested);
        vm.NavigateCommand.Execute("mods");
        var countBefore = requested.Count;

        Assert.That(vm.CycleSectionTab(0), Is.False);
        Assert.That(requested, Has.Count.EqualTo(countBefore));
    }

    [Test]
    public void CycleRail_Next_And_Previous_MoveBetweenSidebarEntries()
    {
        var vm = CreateShell(out var requested);
        vm.NavigateCommand.Execute("mods");

        Assert.That(vm.CycleRail(1), Is.True);
        var afterNext = requested[^1];
        Assert.That(vm.RailItems.Any(item => item.Key == afterNext), Is.True, "切完之后必须落在某个侧栏入口上。");
        Assert.That(afterNext, Is.Not.EqualTo("mods"));

        Assert.That(vm.CycleRail(-1), Is.True);
        Assert.That(requested[^1], Is.EqualTo("mods"), "往回切一格必须回到刚才那个入口。");
    }

    [Test]
    public void CycleRail_Previous_FromTheFirstEntry_WrapsToTheLast()
    {
        var vm = CreateShell(out var requested);
        vm.NavigateCommand.Execute(vm.RailItems[0].Key);

        Assert.That(vm.CycleRail(-1), Is.True);

        Assert.That(requested[^1], Is.EqualTo(vm.RailItems[^1].Key));
    }

    [Test]
    public void NavigateRailIndex_IsOneBased()
    {
        var vm = CreateShell(out var requested);

        Assert.That(vm.NavigateRailIndex(1), Is.True);
        Assert.That(requested[^1], Is.EqualTo(vm.RailItems[0].Key), "Ctrl+1 对应侧栏第一项。");
    }

    [Test]
    public void NavigateRailIndex_OutOfRange_DoesNothing()
    {
        var vm = CreateShell(out var requested);
        var countBefore = requested.Count;

        Assert.That(vm.NavigateRailIndex(0), Is.False);
        Assert.That(vm.NavigateRailIndex(vm.RailItems.Count + 1), Is.False);
        Assert.That(requested, Has.Count.EqualTo(countBefore));
    }
}
