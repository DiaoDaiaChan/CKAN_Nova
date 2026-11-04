using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Profiles;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests.Profiles;

/// <summary>
/// 实例配置档服务：目录换位、清单、回滚与安全边界。
/// 全部在真实文件系统上跑——换位是 rename，只有真目录才能证明内容真的换了回来。
/// </summary>
[TestFixture]
public sealed class InstanceProfileServiceTests
{
    private const string InstanceName = "Test";
    private const string Vanilla = "原版";
    private const string Rp1 = "RP-1";

    private TempWorkspace _workspace = null!;
    private ProfileFakeInstanceService _instances = null!;
    private ProfileFakeRegistryService _registry = null!;
    private InstanceProfileService _service = null!;
    private string _gameDir = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("profiles");
        _gameDir = _workspace.Combine("game");
        Directory.CreateDirectory(_gameDir);
        Directory.CreateDirectory(Path.Combine(_gameDir, "GameData"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "CKAN"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "CKANProfiles"));

        var instance = new InstanceInfo(InstanceName, _gameDir, "1.12.5", true, false, 0);
        _instances = new ProfileFakeInstanceService(instance);
        _instances.Current = instance;
        _registry = new ProfileFakeRegistryService();
        _service = new InstanceProfileService(_instances, _registry);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── 创建与接管 ──────────────────────────────────────────────────────────

    [Test]
    public async Task Create_接管当前状态_GameData与CKAN移进配置档且清单active正确()
    {
        WriteFile(Path.Combine(_gameDir, "GameData", "Squad.txt"), "vanilla");
        WriteFile(Path.Combine(_gameDir, "CKAN", "registry.json"), "{}");

        var result = await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var info = result.Value!;
        Assert.That(info.Name, Is.EqualTo(Vanilla));
        Assert.That(info.IsActive, Is.True);
        Assert.That(info.FileCount, Is.EqualTo(1));

        Assert.That(Directory.Exists(Path.Combine(_gameDir, "GameData")), Is.False, "live GameData 应已移走");
        Assert.That(File.Exists(Path.Combine(info.GameDataPath, "Squad.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(info.Directory, "CKAN", "registry.json")), Is.True);

        var manifest = ProfileManifest.Load(_gameDir);
        Assert.That(manifest.ActiveProfile, Is.EqualTo(Vanilla));
        Assert.That(manifest.Profiles.Select(entry => entry.Name), Is.EquivalentTo(new[] { Vanilla }));
    }

    [Test]
    public async Task Create_不接管只建空GameData目录()
    {
        var result = await _service.CreateAsync(InstanceName, Rp1);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(Directory.Exists(Path.Combine(result.Value!.Directory, "GameData")), Is.True);
        Assert.That(ProfileManifest.Load(_gameDir).ActiveProfile, Is.Null);
        Assert.That(Directory.Exists(Path.Combine(_gameDir, "GameData")), Is.True, "不接管时不能动 live 目录");
    }

    [Test]
    public async Task Create_重名被拒绝()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);

        var again = await _service.CreateAsync(InstanceName, Vanilla.ToLowerInvariant());

        Assert.That(again.IsSuccess, Is.False);
        Assert.That(again.Message, Does.Contain("已存在"));
    }

    [Test]
    public async Task Create_接管时游戏运行中被拒绝()
    {
        _instances.GameRunning = true;

        var result = await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("游戏"));
        Assert.That(Directory.Exists(Path.Combine(_gameDir, "GameData")), Is.True, "被拒绝时不能动目录");
    }

    [Test]
    public async Task Create_不接管时游戏运行不影响()
    {
        _instances.GameRunning = true;

        var result = await _service.CreateAsync(InstanceName, Rp1);

        Assert.That(result.IsSuccess, Is.True, result.Message);
    }

    // ── 切换 ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Switch_往返真的换回来且调用Close与Refresh()
    {
        WriteFile(Path.Combine(_gameDir, "GameData", "Squad.txt"), "vanilla");
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);

        var rp1Dir = ProfileManifest.ResolveProfileDirectory(_gameDir, Rp1);
        WriteFile(Path.Combine(rp1Dir, "GameData", "Realism.txt"), "rp1");
        WriteFile(Path.Combine(rp1Dir, "CKAN", "registry.json"), "rp1");

        var toRp1 = await _service.SwitchAsync(InstanceName, Rp1);

        Assert.That(toRp1.IsSuccess, Is.True, toRp1.Message);
        Assert.That(toRp1.Value!.PreviousProfile, Is.EqualTo(Vanilla));
        Assert.That(toRp1.Value!.Profile, Is.EqualTo(Rp1));
        Assert.That(File.Exists(Path.Combine(_gameDir, "GameData", "Realism.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(_gameDir, "GameData", "Squad.txt")), Is.False);
        Assert.That(ProfileManifest.Load(_gameDir).ActiveProfile, Is.EqualTo(Rp1));
        Assert.That(_registry.CloseCalls, Is.EqualTo(1));
        Assert.That(_registry.RefreshCalls, Is.EqualTo(1));

        var back = await _service.SwitchAsync(InstanceName, Vanilla);

        Assert.That(back.IsSuccess, Is.True, back.Message);
        Assert.That(back.Value!.PreviousProfile, Is.EqualTo(Rp1));
        Assert.That(File.Exists(Path.Combine(_gameDir, "GameData", "Squad.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(_gameDir, "GameData", "Realism.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(rp1Dir, "GameData", "Realism.txt")), Is.True, "旧内容应回到自己的槽位");
        Assert.That(ProfileManifest.Load(_gameDir).ActiveProfile, Is.EqualTo(Vanilla));
        Assert.That(_registry.CloseCalls, Is.EqualTo(2));
        Assert.That(_registry.RefreshCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task Switch_非当前实例不调用注册表()
    {
        var other = new InstanceInfo("Other", _workspace.Combine("other"), "1.12.5", false, false, 0);
        Directory.CreateDirectory(other.GameDir);
        Directory.CreateDirectory(Path.Combine(other.GameDir, "GameData"));
        Directory.CreateDirectory(Path.Combine(other.GameDir, "CKAN"));
        var instances = new ProfileFakeInstanceService(new InstanceInfo(InstanceName, _gameDir, "1.12.5", true, false, 0), other);
        instances.Current = instances.Instances[0];
        var service = new InstanceProfileService(instances, _registry);

        Assert.That((await service.CreateAsync("Other", Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await service.CreateAsync("Other", Rp1)).IsSuccess, Is.True);
        Assert.That((await service.SwitchAsync("Other", Rp1)).IsSuccess, Is.True);

        Assert.That(_registry.CloseCalls, Is.Zero);
        Assert.That(_registry.RefreshCalls, Is.Zero);
    }

    [Test]
    public async Task Switch_到不存在的档失败()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);

        var result = await _service.SwitchAsync(InstanceName, "没有这一档");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不存在"));
    }

    [Test]
    public async Task Switch_游戏运行中失败且不动目录()
    {
        WriteFile(Path.Combine(_gameDir, "GameData", "Squad.txt"), "vanilla");
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);

        _instances.GameRunning = true;
        var result = await _service.SwitchAsync(InstanceName, Rp1);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("关闭游戏"));
        Assert.That(Directory.Exists(Path.Combine(_gameDir, "GameData")), Is.False, "未切换：live 仍是空的");
        Assert.That(ProfileManifest.Load(_gameDir).ActiveProfile, Is.EqualTo(Vanilla));
    }

    [Test]
    public async Task Switch_已是当前档时是no_op()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);

        var result = await _service.SwitchAsync(InstanceName, Vanilla);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Profile, Is.EqualTo(Vanilla));
        Assert.That(result.Value!.PreviousProfile, Is.EqualTo(Vanilla));
        Assert.That(_registry.CloseCalls, Is.Zero);
        Assert.That(_registry.RefreshCalls, Is.Zero);
    }

    [Test]
    public async Task Switch_还没接管时失败()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);

        var result = await _service.SwitchAsync(InstanceName, Rp1);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("接管"));
    }

    [Test]
    public async Task Switch_释放注册表失败时不换位()
    {
        WriteFile(Path.Combine(_gameDir, "GameData", "Squad.txt"), "vanilla");
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);
        _registry.CloseSucceeds = false;

        var result = await _service.SwitchAsync(InstanceName, Rp1);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("注册表"));
        Assert.That(ProfileManifest.Load(_gameDir).ActiveProfile, Is.EqualTo(Vanilla));
    }

    [Test]
    public async Task Switch_刷新失败不影响切换结论()
    {
        WriteFile(Path.Combine(_gameDir, "GameData", "Squad.txt"), "vanilla");
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);
        _registry.RefreshSucceeds = false;

        var result = await _service.SwitchAsync(InstanceName, Rp1);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(ProfileManifest.Load(_gameDir).ActiveProfile, Is.EqualTo(Rp1));
        Assert.That(_registry.RefreshCalls, Is.EqualTo(1));
    }

    // ── 列表与删除 ──────────────────────────────────────────────────────────

    [Test]
    public async Task List_排序并标出active与launch且统计大小()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);
        WriteFile(Path.Combine(ProfileManifest.ResolveProfileDirectory(_gameDir, Rp1), "GameData", "a.bin"), new string('x', 10));
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.SetLaunchProfileAsync(InstanceName, Rp1)).IsSuccess, Is.True);

        var listed = await _service.ListAsync(InstanceName);

        Assert.That(listed.IsSuccess, Is.True, listed.Message);
        var rows = listed.Value!;
        Assert.That(rows.Select(row => row.Name), Is.EqualTo(new[] { Rp1, Vanilla }));
        Assert.That(rows.Single(row => row.Name == Vanilla).IsActive, Is.True);
        Assert.That(rows.Single(row => row.Name == Rp1).IsLaunchProfile, Is.True);
        Assert.That(rows.Single(row => row.Name == Rp1).SizeBytes, Is.EqualTo(10));
        Assert.That(rows.Single(row => row.Name == Rp1).FileCount, Is.EqualTo(1));
        Assert.That(rows.Single(row => row.Name == Rp1).GameDataPath,
            Is.EqualTo(Path.Combine(ProfileManifest.ResolveProfileDirectory(_gameDir, Rp1), "GameData")));
    }

    [Test]
    public async Task Delete_拒绝删除生效档()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);

        var result = await _service.DeleteAsync(InstanceName, Vanilla);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("生效"));
        Assert.That(Directory.Exists(ProfileManifest.ResolveProfileDirectory(_gameDir, Vanilla)), Is.True);
    }

    [Test]
    public async Task Delete_删除非生效档并移除清单条目()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);
        Assert.That((await _service.SetLaunchProfileAsync(InstanceName, Rp1)).IsSuccess, Is.True);

        var result = await _service.DeleteAsync(InstanceName, Rp1);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(Directory.Exists(ProfileManifest.ResolveProfileDirectory(_gameDir, Rp1)), Is.False);
        var manifest = ProfileManifest.Load(_gameDir);
        Assert.That(manifest.Profiles.Select(entry => entry.Name), Is.EquivalentTo(new[] { Vanilla }));
        Assert.That(manifest.LaunchProfile, Is.Null, "删掉启动档时要顺手清掉指向");
    }

    [Test]
    public async Task Delete_不存在的档失败()
    {
        var result = await _service.DeleteAsync(InstanceName, "没有这一档");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不存在"));
    }

    // ── 启动档 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task LaunchProfile_读写与Apply()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.CreateAsync(InstanceName, Rp1)).IsSuccess, Is.True);
        var rp1Dir = ProfileManifest.ResolveProfileDirectory(_gameDir, Rp1);
        WriteFile(Path.Combine(rp1Dir, "GameData", "Realism.txt"), "rp1");

        Assert.That((await _service.GetLaunchProfileAsync(InstanceName)).Value, Is.Null);
        Assert.That((await _service.SetLaunchProfileAsync(InstanceName, Rp1)).IsSuccess, Is.True);
        Assert.That((await _service.GetLaunchProfileAsync(InstanceName)).Value, Is.EqualTo(Rp1));

        var applied = await _service.ApplyLaunchProfileAsync(InstanceName);

        Assert.That(applied.IsSuccess, Is.True, applied.Message);
        Assert.That(ProfileManifest.Load(_gameDir).ActiveProfile, Is.EqualTo(Rp1));
        Assert.That(File.Exists(Path.Combine(_gameDir, "GameData", "Realism.txt")), Is.True);

        Assert.That((await _service.SetLaunchProfileAsync(InstanceName, null)).IsSuccess, Is.True);
        Assert.That((await _service.GetLaunchProfileAsync(InstanceName)).Value, Is.Null);
    }

    [Test]
    public async Task LaunchProfile_设成不存在的档失败()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);

        var result = await _service.SetLaunchProfileAsync(InstanceName, "没有这一档");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不存在"));
    }

    [Test]
    public async Task LaunchProfile_已是当前档时Apply不做任何事()
    {
        Assert.That((await _service.CreateAsync(InstanceName, Vanilla, adoptCurrent: true)).IsSuccess, Is.True);
        Assert.That((await _service.SetLaunchProfileAsync(InstanceName, Vanilla)).IsSuccess, Is.True);

        var applied = await _service.ApplyLaunchProfileAsync(InstanceName);

        Assert.That(applied.IsSuccess, Is.True);
        Assert.That(_registry.CloseCalls, Is.Zero);
    }

    [Test]
    public async Task LaunchProfile_未设置时Apply成功()
    {
        var applied = await _service.ApplyLaunchProfileAsync(InstanceName);

        Assert.That(applied.IsSuccess, Is.True);
        Assert.That(applied.Value, Is.True);
    }

    // ── 名字校验与实例解析 ──────────────────────────────────────────────────

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a/b")]
    [TestCase("a\\b")]
    [TestCase("..")]
    [TestCase("a..b")]
    [TestCase(" 原版")]
    [TestCase("原版 ")]
    [TestCase("a:b")]
    [TestCase("a*b")]
    [TestCase("a?b")]
    [TestCase("a\"b")]
    [TestCase("a<b")]
    [TestCase("a>b")]
    [TestCase("a|b")]
    [TestCase("0123456789012345678901234567890123456789012345678901234567890123x")]
    public async Task Create_非法名字被拒且给出原因(string name)
    {
        var result = await _service.CreateAsync(InstanceName, name);

        Assert.That(result.IsSuccess, Is.False, $"名字「{name}」应被拒绝");
        Assert.That(result.Message, Is.Not.Empty);
    }

    [Test]
    public async Task Create_合法名字被接受()
    {
        var result = await _service.CreateAsync(InstanceName, "RP-1 (1.12)");

        Assert.That(result.IsSuccess, Is.True, result.Message);
    }

    [Test]
    public async Task 找不到实例时所有方法都失败()
    {
        Assert.That((await _service.ListAsync("不存在的实例")).IsSuccess, Is.False);
        Assert.That((await _service.CreateAsync("不存在的实例", Vanilla)).IsSuccess, Is.False);
        Assert.That((await _service.SwitchAsync("不存在的实例", Vanilla)).IsSuccess, Is.False);
        Assert.That((await _service.DeleteAsync("不存在的实例", Vanilla)).IsSuccess, Is.False);
        Assert.That((await _service.GetLaunchProfileAsync("不存在的实例")).IsSuccess, Is.False);
        Assert.That((await _service.SetLaunchProfileAsync("不存在的实例", Vanilla)).IsSuccess, Is.False);
        Assert.That((await _service.ApplyLaunchProfileAsync("不存在的实例")).IsSuccess, Is.False);
    }

    [Test]
    public async Task 清单损坏时按空清单处理()
    {
        var path = ProfileManifest.ResolvePath(_gameDir);
        WriteFile(path, "{ this is not json ");

        var listed = await _service.ListAsync(InstanceName);

        Assert.That(listed.IsSuccess, Is.True);
        Assert.That(listed.Value!, Is.Empty);
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }
}
