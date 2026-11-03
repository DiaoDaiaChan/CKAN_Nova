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
    public void Defaults_AreCollapsed()
    {
        var vm = CreateShell(out _);

        Assert.That(vm.IsNavCollapsed, Is.True, "启动时侧栏默认精简模式，悬停自动抽屉式弹开。");
        Assert.That(vm.IsNavExpanded, Is.False);
        Assert.That(vm.NavToggleGlyph, Is.EqualTo("›"), "精简态按钮提示往右放。");
    }

    [Test]
    public void ToggleCommand_FlipsCollapsedStateBothWays()
    {
        var vm = CreateShell(out _);

        // 默认精简态，触发后展开
        vm.ToggleNavCollapsedCommand.Execute(null);
        Assert.That(vm.IsNavCollapsed, Is.False);
        Assert.That(vm.IsNavExpanded, Is.True);

        // 再次触发收回到精简态
        vm.ToggleNavCollapsedCommand.Execute(null);
        Assert.That(vm.IsNavCollapsed, Is.True, "再次触发必须收回到精简态，命令不能是单向开关。");
    }

    [Test]
    public void CollapsedState_RaisesPropertyChangedForDerivedMembers()
    {
        var vm = CreateShell(out _);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsNavCollapsed = false;

        Assert.That(changed, Does.Contain(nameof(ShellViewModel.IsNavCollapsed)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.IsNavExpanded)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavToggleGlyph)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavToggleToolTip)));
    }

    [Test]
    public void ToggleToolTip_FollowsCollapsedState()
    {
        var vm = CreateShell(out _);
        Assert.That(vm.NavToggleToolTip, Is.EqualTo("T:Ui_NavRail_Expand"), "默认精简态下提示为「展开侧栏」。");

        vm.IsNavCollapsed = false;
        Assert.That(vm.NavToggleToolTip, Is.EqualTo("T:Ui_NavRail_Collapse"), "展开后提示变成「收起侧栏」。");

        vm.IsNavCollapsed = true;
        Assert.That(vm.NavToggleToolTip, Is.EqualTo("T:Ui_NavRail_Expand"));
    }

    [Test]
    public void CollapsedState_KeepsNavItemsAndSelection()
    {
        var vm = CreateShell(out _);
        vm.SelectedNavItem = vm.NavItems[3];

        vm.IsNavCollapsed = true;

        Assert.That(vm.NavItems.Count, Is.EqualTo(18), "收起侧栏不能丢导航项：展开后要原样回来。");
        Assert.That(vm.SelectedNavItem, Is.Not.Null);
        Assert.That(vm.SelectedNavItem!.Key, Is.EqualTo("repos"), "收起时选中项必须保持，否则图标栏就看不出当前页。");
    }

    [Test]
    public void NavToggleGlyph_ChangesWithState()
    {
        var vm = CreateShell(out _);
        vm.IsNavCollapsed = true;
        Assert.That(vm.NavToggleGlyph, Is.EqualTo("›"), "收起态按钮提示往右放。");

        vm.IsNavCollapsed = false;
        Assert.That(vm.NavToggleGlyph, Is.EqualTo("‹"), "展开态按钮提示往左收。");
    }

    [Test]
    public void ExpandFullSidebar_CanBeInvokedFromCommand()
    {
        var vm = CreateShell(out _);
        Assert.That(vm.IsNavCollapsed, Is.True, "初始应为精简态。");

        // 顶栏按钮、侧栏底部展开按钮或浮层展开按钮均调用此命令
        vm.ToggleNavCollapsedCommand.Execute(null);
        Assert.That(vm.IsNavCollapsed, Is.False, "点击后完整展开侧边栏。");
        Assert.That(vm.IsNavExpanded, Is.True);
    }

    [Test]
    public void NavHoverMode_DefaultsToTabPopout()
    {
        var vm = CreateShell(out _);
        Assert.That(vm.NavHoverMode, Is.EqualTo(NavHoverMode.TabPopout), "默认使用单Tab独立弹出模式。");
        Assert.That(vm.IsTabPopoutHoverMode, Is.True);
        Assert.That(vm.NavHoverModeGlyph, Is.EqualTo("📑"));
    }

    [Test]
    public void ToggleNavHoverModeCommand_FlipsBetweenTabPopoutAndDrawer()
    {
        var vm = CreateShell(out _);
        Assert.That(vm.NavHoverMode, Is.EqualTo(NavHoverMode.TabPopout));

        vm.ToggleNavHoverModeCommand.Execute(null);
        Assert.That(vm.NavHoverMode, Is.EqualTo(NavHoverMode.Drawer), "切换后为全侧栏抽屉模式。");
        Assert.That(vm.IsTabPopoutHoverMode, Is.False);
        Assert.That(vm.NavHoverModeGlyph, Is.EqualTo("🗂️"));

        vm.ToggleNavHoverModeCommand.Execute(null);
        Assert.That(vm.NavHoverMode, Is.EqualTo(NavHoverMode.TabPopout), "再次切换回单Tab弹出模式。");
        Assert.That(vm.IsTabPopoutHoverMode, Is.True);
        Assert.That(vm.NavHoverModeGlyph, Is.EqualTo("📑"));
    }

    [Test]
    public void NavHoverMode_RaisesPropertyChangedForDerivedProperties()
    {
        var vm = CreateShell(out _);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.NavHoverMode = NavHoverMode.Drawer;

        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavHoverMode)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.IsTabPopoutHoverMode)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavHoverModeTitle)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavHoverModeToolTip)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavHoverModeGlyph)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.NavHoverModePathData)));
    }
}
