using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Labels;
using CkanUI.TestSupport;
using CKAN;
using CKAN.Games.KerbalSpaceProgram;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// F-24 标签服务用例：全部走真实的 <see cref="GameInstance"/> + 真实的
/// <c>ModuleLabelList</c> 序列化，只把落盘路径换到仓库内临时目录，
/// 因此同时验证了"落盘交给 Core"这条约束。
/// </summary>
[TestFixture]
public sealed class LabelServiceTests
{
    [Test]
    public void Labels_WithoutFile_FallsBackToThreeGlobalDefaults()
    {
        using var f = new Fixture();

        var labels = f.Service.Labels;

        Assert.That(labels, Has.Count.EqualTo(3), "缺文件时应退化成 Favourites/Hidden/Held 三组默认值");
        Assert.That(labels.Count(l => l.HoldVersion), Is.EqualTo(1), "默认只有一组 HoldVersion");
        Assert.That(labels.Count(l => l.HideFromOtherFilters), Is.EqualTo(1), "默认只有一组隐藏标签");
        Assert.That(labels.All(l => l.IsGlobal), Is.True, "默认标签组不绑定实例");
        Assert.That(f.Service.HasActiveInstance, Is.True);
        Assert.That(f.Service.InstanceName, Is.EqualTo("测试实例"));
    }

    [Test]
    public void SetLabelsAsync_UnknownName_FailsAndKeepsMembership()
    {
        using var f = new Fixture();

        var result = f.Service.SetLabelsAsync("ModA", new[] { "根本没有的标签组" }).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False, "未知标签组不应静默丢弃");
        Assert.That(result.Message, Does.Contain("根本没有的标签组"));
        Assert.That(f.Service.GetLabelsFor("ModA"), Is.Empty);
    }

    [Test]
    public void SetLabelsAsync_AssignsAndPersistsThroughCore()
    {
        using var f = new Fixture();
        var created = f.Service.CreateLabelAsync(new LabelEditRequest("自建组")).GetAwaiter().GetResult();
        Assert.That(created.IsSuccess, Is.True, created.Message);

        var assigned = f.Service.SetLabelsAsync("ModA", new[] { "自建组" }).GetAwaiter().GetResult();
        Assert.That(assigned.IsSuccess, Is.True, assigned.Message);
        Assert.That(f.Service.GetLabelsFor("ModA"), Is.EqualTo(new[] { "自建组" }));

        // 落盘由 Core 负责：同路径新开一个服务应能读回成员关系
        using var reloaded = new Fixture(f.LabelsPath);
        Assert.That(reloaded.Service.GetLabelsFor("ModA"), Does.Contain("自建组"));
        Assert.That(reloaded.Service.Labels.Any(l => l.Name == "自建组"), Is.True);
    }

    [Test]
    public void SetLabelsAsync_EmptyCollection_RemovesMembership()
    {
        using var f = new Fixture();
        f.Service.CreateLabelAsync(new LabelEditRequest("临时组")).GetAwaiter().GetResult();
        f.Service.SetLabelsAsync("ModA", new[] { "临时组" }).GetAwaiter().GetResult();
        Assert.That(f.Service.GetLabelsFor("ModA"), Is.EqualTo(new[] { "临时组" }));

        var cleared = f.Service.SetLabelsAsync("ModA", Array.Empty<string>()).GetAwaiter().GetResult();

        Assert.That(cleared.IsSuccess, Is.True, cleared.Message);
        Assert.That(f.Service.GetLabelsFor("ModA"), Is.Empty);
    }

    [Test]
    public void SetFavouriteAsync_TogglesBothWays()
    {
        using var f = new Fixture();
        Assert.That(f.Service.IsFavourite("ModA"), Is.False);

        var on = f.Service.SetFavouriteAsync("ModA", true).GetAwaiter().GetResult();
        Assert.That(on.IsSuccess, Is.True, on.Message);
        Assert.That(f.Service.IsFavourite("ModA"), Is.True);
        var names = f.Service.GetLabelsFor("ModA");
        Assert.That(names, Has.Count.EqualTo(1), "收藏只落在一个标签组里");
        Assert.That(names[0], Is.Not.Empty, "默认收藏组名来自上游资源，不硬编码");

        var off = f.Service.SetFavouriteAsync("ModA", false).GetAwaiter().GetResult();
        Assert.That(off.IsSuccess, Is.True, off.Message);
        Assert.That(f.Service.IsFavourite("ModA"), Is.False);
    }

    [Test]
    public void SetHiddenAsync_MarksHiddenAndKeepsSingleHiddenGroup()
    {
        using var f = new Fixture();

        var on = f.Service.SetHiddenAsync("ModA", true).GetAwaiter().GetResult();

        Assert.That(on.IsSuccess, Is.True, on.Message);
        Assert.That(f.Service.IsHidden("ModA"), Is.True);
        Assert.That(f.Service.Labels.Count(l => l.HideFromOtherFilters), Is.EqualTo(1), "重复标记不应新建第二个隐藏组");
    }

    [Test]
    public void SetHeldAsync_FeedsHeldIdentifiers()
    {
        using var f = new Fixture();

        var on = f.Service.SetHeldAsync("ModA", true).GetAwaiter().GetResult();

        Assert.That(on.IsSuccess, Is.True, on.Message);
        Assert.That(f.Service.IsHeld("ModA"), Is.True);
        Assert.That(f.Service.HeldIdentifiers, Does.Contain("ModA"), "升级器要跳过锁定版本的模组");
    }

    [Test]
    public void CreateLabelAsync_RejectsDuplicateName()
    {
        using var f = new Fixture();
        var first = f.Service.CreateLabelAsync(new LabelEditRequest("组A") { ColorHex = "#123456", HoldVersion = true }).GetAwaiter().GetResult();
        Assert.That(first.IsSuccess, Is.True, first.Message);
        Assert.That(first.Value!.ColorHex, Is.EqualTo("#123456"));
        Assert.That(first.Value!.HoldVersion, Is.True);

        var count = f.Service.Labels.Count;
        var second = f.Service.CreateLabelAsync(new LabelEditRequest("组a")).GetAwaiter().GetResult();

        Assert.That(second.IsSuccess, Is.False, "同名（大小写不敏感）不该新建");
        Assert.That(f.Service.Labels, Has.Count.EqualTo(count));
    }

    [Test]
    public void UpdateLabelAsync_ChangesFlagsAndNormalisesColor()
    {
        using var f = new Fixture();
        f.Service.CreateLabelAsync(new LabelEditRequest("组B")).GetAwaiter().GetResult();

        var updated = f.Service.UpdateLabelAsync(new LabelEditRequest("组B")
        {
            ColorHex = "abcdef",
            HideFromOtherFilters = true,
            IgnoreMissingFiles = true,
        }).GetAwaiter().GetResult();

        Assert.That(updated.IsSuccess, Is.True, updated.Message);
        Assert.That(updated.Value!.ColorHex, Is.EqualTo("#ABCDEF"), "颜色统一规范成 #RRGGBB");
        Assert.That(updated.Value!.HideFromOtherFilters, Is.True);
        Assert.That(updated.Value!.IgnoreMissingFiles, Is.True);

        var missing = f.Service.UpdateLabelAsync(new LabelEditRequest("不存在")).GetAwaiter().GetResult();
        Assert.That(missing.IsSuccess, Is.False);
    }

    [Test]
    public void DeleteLabelAsync_RemovesGroupAndSecondCallFails()
    {
        using var f = new Fixture();
        f.Service.CreateLabelAsync(new LabelEditRequest("组C")).GetAwaiter().GetResult();
        f.Service.SetLabelsAsync("ModA", new[] { "组C" }).GetAwaiter().GetResult();

        var deleted = f.Service.DeleteLabelAsync("组C").GetAwaiter().GetResult();
        Assert.That(deleted.IsSuccess, Is.True, deleted.Message);
        Assert.That(f.Service.Labels.Any(l => l.Name == "组C"), Is.False);
        Assert.That(f.Service.GetLabelsFor("ModA"), Is.Empty, "成员关系应随标签组一起消失");

        var again = f.Service.DeleteLabelAsync("组C").GetAwaiter().GetResult();
        Assert.That(again.IsSuccess, Is.False);
    }

    [Test]
    public void LabelsChanged_FiresOncePerMutation()
    {
        using var f = new Fixture();
        var snapshots = new List<IReadOnlyList<LabelInfo>>();
        f.Service.LabelsChanged += snapshots.Add;

        f.Service.CreateLabelAsync(new LabelEditRequest("组D")).GetAwaiter().GetResult();
        f.Service.SetLabelsAsync("ModA", new[] { "组D" }).GetAwaiter().GetResult();

        f.Dispatcher.PumpPosts();
        Assert.That(snapshots, Has.Count.EqualTo(2), "每次变更只推一次整表快照");
        Assert.That(snapshots[0], Is.Not.Empty);
    }

    [Test]
    public void IgnoreMissingIdentifiers_ReflectsFlag()
    {
        using var f = new Fixture();
        f.Service.CreateLabelAsync(new LabelEditRequest("缺文件组") { IgnoreMissingFiles = true }).GetAwaiter().GetResult();
        f.Service.SetLabelsAsync("ModX", new[] { "缺文件组" }).GetAwaiter().GetResult();

        Assert.That(f.Service.IgnoreMissingIdentifiers, Does.Contain("ModX"));
        Assert.That(f.Service.HeldIdentifiers, Is.Empty);
    }

    [Test]
    public void RefreshAsync_ReturnsWholeSnapshot()
    {
        using var f = new Fixture();
        f.Service.CreateLabelAsync(new LabelEditRequest("组E")).GetAwaiter().GetResult();

        var result = f.Service.RefreshAsync().GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!, Has.Count.EqualTo(f.Service.Labels.Count));
        Assert.That(result.Value!.Any(l => l.Name == "组E"), Is.True, "刷新后自建组仍在（来自磁盘）");
    }

    [Test]
    public void Mutations_WithoutInstance_FailInsteadOfPretending()
    {
        using var f = new Fixture(withInstance: false);

        var result = f.Service.SetFavouriteAsync("ModA", true).GetAwaiter().GetResult();

        Assert.That(f.Service.HasActiveInstance, Is.False);
        Assert.That(f.Service.InstanceName, Is.Null);
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("游戏实例"));
    }

    /// <summary>每个用例独占的临时实例 + 临时标签文件。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly TempWorkspace _temp;

        public Fixture(string? labelsPath = null, bool withInstance = true)
        {
            _temp = new TempWorkspace("label-service");
            Manager = new GameInstanceManager(new FakeUser(), new FakeConfiguration());
            var gameDir = _temp.Combine("game");
            Directory.CreateDirectory(gameDir);
            Instance = new GameInstance(new KerbalSpaceProgram(), gameDir, "测试实例");
            if (withInstance)
            {
                Manager.SetCurrentInstance(Instance);
            }

            Dispatcher = new FakeDispatcher();
            LabelsPath = labelsPath ?? _temp.Combine("labels.json");
            Service = new LabelService(Manager, Dispatcher, LabelsPath);
        }

        public GameInstanceManager Manager { get; }

        public GameInstance Instance { get; }

        public FakeDispatcher Dispatcher { get; }

        public string LabelsPath { get; }

        public LabelService Service { get; }

        public void Dispose() => _temp.Dispose();
    }
}
