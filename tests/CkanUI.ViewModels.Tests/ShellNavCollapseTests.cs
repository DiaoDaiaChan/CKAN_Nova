using System.Collections.Generic;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 外壳侧栏折叠（本轮交互升级）：收起只是呈现状态，导航语义与选中项都不能变。
/// 独立成文件而不是塞进 ShellViewModelTests —— 那边的用例在并发期被多人改，减少冲突面。
/// </summary>
[TestFixture]
public sealed class ShellNavCollapseTests
{
    private static ShellViewModel CreateShell(out List<string> requested)
    {
        var keys = new List<string>();
        requested = keys;
        var navigation = new NavigationService(key =>
        {
            keys.Add(key);
            return null;
        });
        return new ShellViewModel(navigation, key => "T:" + key);
    }

    [Test]
    public void Defaults_AreExpanded()
    {
        var vm = CreateShell(out _);

        Assert.That(vm.IsNavCollapsed, Is.False, "启动时侧栏必须展开，新用户不该先看到图标栏。");
        Assert.That(vm.IsNavExpanded, Is.True);
        Assert.That(vm.NavToggleGlyph, Is.EqualTo("‹"), "展开态按钮提示往左收。");
    }

    [Test]
    public void ToggleCommand_FlipsCollapsedStateBothWays()
    {
        var vm = CreateShell(out _);

        vm.ToggleNavCollapsedCommand.Execute(null);
        Assert.That(vm.IsNavCollapsed, Is.True);
        Assert.That(vm.IsNavExpanded, Is.False);

        vm.ToggleNavCollapsedCommand.Execute(null);
        Assert.That(vm.IsNavCollapsed, Is.False, "再次触发必须收回到展开态，命令不能是单向开关。");
    }

    [Test]
    public void CollapsedState_RaisesPropertyChangedForDerivedMembers()
    {
        var vm = CreateShell(out _);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsNavCollapsed = true;

        Assert.That(changed, Does.Contain(nameof(ShellViewModel.IsNavCollapsed)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.IsNavExpanded)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavToggleGlyph)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavToggleToolTip)));
    }

    [Test]
    public void ToggleToolTip_FollowsCollapsedState()
    {
        var vm = CreateShell(out _);
        Assert.That(vm.NavToggleToolTip, Is.EqualTo("T:Ui_NavRail_Collapse"));

        vm.IsNavCollapsed = true;
        Assert.That(vm.NavToggleToolTip, Is.EqualTo("T:Ui_NavRail_Expand"), "收起后提示要变成「展开侧栏」。");

        vm.IsNavCollapsed = false;
        Assert.That(vm.NavToggleToolTip, Is.EqualTo("T:Ui_NavRail_Collapse"));
    }

    [Test]
    public void CollapsedState_KeepsNavItemsAndSelection()
    {
        var vm = CreateShell(out _);
        vm.SelectedNavItem = vm.NavItems[3];

        vm.IsNavCollapsed = true;

        Assert.That(vm.NavItems.Count, Is.EqualTo(19), "收起侧栏不能丢导航项：展开后要原样回来。");
        Assert.That(vm.SelectedNavItem, Is.Not.Null);
        Assert.That(vm.SelectedNavItem!.Key, Is.EqualTo("repos"), "收起时选中项必须保持，否则图标栏就看不出当前页。");
    }

    [Test]
    public void NavToggleGlyph_ChangesWithState()
    {
        var vm = CreateShell(out _);
        vm.IsNavCollapsed = true;
        Assert.That(vm.NavToggleGlyph, Is.EqualTo("›"), "收起态按钮提示往右放。");
    }
}
