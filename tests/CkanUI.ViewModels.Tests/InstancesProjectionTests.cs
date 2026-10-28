using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.InstanceDialogs;
using CkanUI.ViewModels.Pages.Instances;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 实例页纯函数层测试（04 篇 §2.1 列表投影 / 03 篇 §5 异常态 / 06 篇 UI-32 Steam 导入）。
/// </summary>
/// <remarks>
/// 这些用例全部 headless（07 篇）：不碰磁盘、不碰 Avalonia，只验证投影、去重、目标目录推导与表单校验，
/// 页面视图模型的行为测试在 <see cref="InstancesPageViewModelTests"/>。
/// </remarks>
[TestFixture]
public sealed class InstancesProjectionTests
{
    [Test]
    public void Project_空快照返回空列表()
    {
        var rows = InstancesProjection.Project(null, null);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void Project_当前实例置顶_锁定靠前_其余按名称升序()
    {
        var snapshot = new List<InstanceInfo>
        {
            new("Beta", @"D:\KSP-Beta", "1.12.5", false, false, 12),
            new("Alpha", @"D:\KSP-Alpha", null, false, false, null),
            new("Gamma", @"D:\KSP-Gamma", "1.11.2", false, true, 3),
            new("Current", @"D:\KSP-Current", "1.12.5", true, false, 88),
        };

        var rows = InstancesProjection.Project(snapshot, "Current");

        Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Current", "Gamma", "Alpha", "Beta" }));
        Assert.That(rows[0].IsCurrent, Is.True);
        Assert.That(rows[0].CurrentText, Is.EqualTo("当前实例"));
        Assert.That(rows[1].IsLocked, Is.True);
        Assert.That(rows[1].LockText, Is.EqualTo("⚠ 被其他进程占用"));
    }

    [Test]
    public void Project_版本与模组数缺失时给出可读占位()
    {
        var snapshot = new List<InstanceInfo> { new("Alpha", @"D:\KSP-Alpha", null, false, false, null) };

        var row = InstancesProjection.Project(snapshot, null).Single();

        Assert.That(row.VersionText, Is.EqualTo("版本未知"));
        Assert.That(row.ModuleCountText, Is.EqualTo("—"));
        Assert.That(row.DisplayName, Is.EqualTo("Alpha"));
        Assert.That(row.CurrentText, Is.EqualTo("未选中"));
    }

    [Test]
    public void Project_路径探测失败时标记目录不存在()
    {
        var snapshot = new List<InstanceInfo> { new("Alpha", @"D:\KSP-Alpha", "1.12.5", false, false, 1) };

        var rows = InstancesProjection.Project(snapshot, null, _ => false);

        Assert.That(rows.Single().IsPathMissing, Is.True);
        Assert.That(rows.Single().PathStatusText, Does.Contain("目录不存在"));
    }

    [Test]
    public void Project_探测函数抛异常时不应让整个列表消失()
    {
        var snapshot = new List<InstanceInfo> { new("Alpha", @"D:\KSP-Alpha", "1.12.5", false, false, 1) };

        var rows = InstancesProjection.Project(snapshot, null, _ => throw new IOException("磁盘离线"));

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].IsPathMissing, Is.False);
    }

    [Test]
    public void ContainsPath_尾分隔符与大小写视为同一目录()
    {
        var snapshot = new List<InstanceInfo> { new("Alpha", "D:\\KSP-Alpha\\", "1.12.5", false, false, 1) };

        Assert.That(InstancesProjection.ContainsPath(snapshot, @"d:\ksp-alpha"), Is.True);
        Assert.That(InstancesProjection.ContainsPath(snapshot, @"D:\KSP-Other"), Is.False);
    }

    [Test]
    public void FilterNewPaths_去掉已注册目录与重复项()
    {
        var registered = new List<InstanceInfo> { new("Alpha", @"D:\KSP-Alpha", "1.12.5", false, false, 1) };

        var result = InstancesProjection.FilterNewPaths(
            new[] { @"D:\KSP-Alpha", @"D:\KSP-Beta", @"d:\ksp-beta" },
            registered);

        Assert.That(result, Is.EqualTo(new[] { @"D:\KSP-Beta" }));
    }

    [Test]
    public void CloneDestination_为源目录的同级目录()
    {
        var destination = InstancesProjection.CloneDestination(@"D:\Games\Kerbal Space Program", "副本");

        Assert.That(destination, Is.EqualTo(Path.Combine(@"D:\Games", "副本")));
    }

    [Test]
    public void Validator_名称与路径的基本校验()
    {
        Assert.That(InstanceFormValidator.ValidateName(null), Is.EqualTo("实例名称不能为空。"));
        Assert.That(InstanceFormValidator.ValidateName(new string('a', 65)), Does.Contain("64"));
        Assert.That(InstanceFormValidator.ValidateName("a/b"), Does.Contain("不能包含"));
        Assert.That(InstanceFormValidator.ValidateName("Alpha", new[] { "alpha" }), Does.Contain("已存在同名实例"));
        Assert.That(InstanceFormValidator.ValidateName("Beta", new[] { "Alpha" }, "Alpha"), Is.Null);
        Assert.That(InstanceFormValidator.ValidateName("Alpha", new[] { "Alpha" }, "Alpha"), Is.EqualTo("新名称与原名相同，无需重命名。"));
        Assert.That(InstanceFormValidator.ValidatePath(null), Is.EqualTo("请选择 KSP 安装目录。"));
        Assert.That(InstanceFormValidator.ValidatePath(@"relative\path"), Does.Contain("绝对路径"));
        Assert.That(InstanceFormValidator.ValidatePath(@"D:\Games\KSP"), Is.Null);
    }

}
