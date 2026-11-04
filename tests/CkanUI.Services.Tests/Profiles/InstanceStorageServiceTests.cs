using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Dedup;
using CkanUI.Services.Profiles;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests.Profiles;

/// <summary>
/// 存储扫描与配置档去重：数字必须对得上真实文件，硬链接必须能用文件身份证明。
/// </summary>
[TestFixture]
public sealed class InstanceStorageServiceTests
{
    private const string InstanceName = "Test";
    private const int BigSize = 200 * 1024;

    private TempWorkspace _workspace = null!;
    private ProfileFakeInstanceService _instances = null!;
    private string _gameDir = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("storage");
        _gameDir = _workspace.Combine("game");
        Directory.CreateDirectory(_gameDir);
        var instance = new InstanceInfo(InstanceName, _gameDir, "1.12.5", true, false, 0);
        _instances = new ProfileFakeInstanceService(instance);
        _instances.Current = instance;
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── 扫描 ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Scan_统计live与各档大小且active不重复计算()
    {
        WriteFile(Path.Combine(_gameDir, "GameData", "stock.bin"), 1000);
        WriteFile(Path.Combine(_gameDir, "CKAN", "registry.json"), 200);
        WriteManifest("Base", "Base", "RP1");
        WriteFile(Path.Combine(_gameDir, "CKANProfiles", "RP1", "GameData", "a.bin"), 500);
        WriteFile(Path.Combine(_gameDir, "CKANProfiles", "RP1", "GameData", "b.bin"), 250);

        var result = await CreateService().ScanAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var report = result.Value!;
        Assert.That(report.Instances, Has.Count.EqualTo(1));
        var row = report.Instances[0];
        Assert.That(row.LiveGameDataBytes, Is.EqualTo(1000));
        Assert.That(row.CkanBytes, Is.EqualTo(200));
        Assert.That(row.ProfileBytes, Is.EqualTo(750), "active 档沿用 live 数字，不能再算一遍");
        Assert.That(row.TotalBytes, Is.EqualTo(1950));
        Assert.That(report.TotalBytes, Is.EqualTo(1950));
        Assert.That(report.TotalProfileBytes, Is.EqualTo(750));

        var active = row.Profiles.Single(item => item.ProfileName == "Base");
        var other = row.Profiles.Single(item => item.ProfileName == "RP1");
        Assert.That(active.IsActive, Is.True);
        Assert.That(active.Bytes, Is.EqualTo(1000));
        Assert.That(other.IsActive, Is.False);
        Assert.That(other.Bytes, Is.EqualTo(750));
        Assert.That(other.FileCount, Is.EqualTo(2));

        Assert.That(row.FreeBytes, Is.Not.Null);
        Assert.That(row.VolumeBytes, Is.Not.Null);
        Assert.That(row.FreeBytes!.Value, Is.GreaterThan(0));
    }

    [Test]
    public async Task Scan_多实例按名字排序且合计正确()
    {
        var second = _workspace.Combine("second");
        Directory.CreateDirectory(second);
        WriteFile(Path.Combine(second, "GameData", "x.bin"), 300);
        var instances = new ProfileFakeInstanceService(
            new InstanceInfo("Zulu", _gameDir, "1.12.5", true, false, 0),
            new InstanceInfo("Alpha", second, "1.12.5", false, false, 0));
        var service = new InstanceStorageService(instances, NativeDedupLinker.Instance);

        var result = await service.ScanAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Instances.Select(row => row.InstanceName), Is.EqualTo(new[] { "Alpha", "Zulu" }));
        Assert.That(result.Value!.TotalBytes, Is.EqualTo(300));
    }

    [Test]
    public async Task Scan_没有实例时返回空报告()
    {
        var service = new InstanceStorageService(new ProfileFakeInstanceService(), NativeDedupLinker.Instance);

        var result = await service.ScanAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsEmpty, Is.True);
        Assert.That(result.Value!.TotalBytes, Is.Zero);
    }

    [Test]
    public async Task Scan_目录不存在时给零不报错()
    {
        var result = await CreateService().ScanAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Instances[0].TotalBytes, Is.Zero);
        Assert.That(result.Value!.Instances[0].FreeBytes, Is.Not.Null);
    }

    // ── 去重 ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Dedup_dryRun只估算不写盘()
    {
        var (linker, left, right) = PrepareIdenticalProfiles();

        var result = await CreateService(linker).DeduplicateProfilesAsync(InstanceName, dryRun: true);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var report = result.Value!;
        Assert.That(report.DryRun, Is.True);
        Assert.That(report.GroupCount, Is.EqualTo(1));
        Assert.That(report.DuplicateFileCount, Is.EqualTo(1));
        Assert.That(report.ReclaimableBytes, Is.EqualTo(BigSize));
        Assert.That(linker.CreateCalls, Is.Zero);
        Assert.That(IdentitiesDiffer(left, right), Is.True, "dryRun 不能改变文件身份");
    }

    [Test]
    public async Task Dedup_实跑后两个路径共享同一硬链接()
    {
        if (NativeDedupLinker.Instance.Support != DedupPlatformSupport.Supported)
        {
            Assert.Ignore("当前平台不支持硬链接。");
        }

        var (linker, left, right) = PrepareIdenticalProfiles();
        Assert.That(IdentitiesDiffer(left, right), Is.True, "前置条件：两个文件本来各占一份");

        var result = await CreateService(linker).DeduplicateProfilesAsync(InstanceName, dryRun: false);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var report = result.Value!;
        Assert.That(report.DryRun, Is.False);
        Assert.That(report.GroupCount, Is.EqualTo(1));
        Assert.That(report.DuplicateFileCount, Is.EqualTo(1));
        Assert.That(report.ReclaimableBytes, Is.EqualTo(BigSize));
        Assert.That(linker.CreateCalls, Is.EqualTo(1));
        Assert.That(linker.TryGetIdentity(left, out var leftId), Is.True);
        Assert.That(linker.TryGetIdentity(right, out var rightId), Is.True);
        Assert.That(leftId.SameAs(rightId), Is.True, "实跑后应指向同一份数据");
        Assert.That(File.ReadAllBytes(right).Length, Is.EqualTo(BigSize), "内容不能变");
    }

    [Test]
    public async Task Dedup_小于阈值的文件不参与()
    {
        WriteManifest(null, "Base", "RP1");
        WriteFile(Path.Combine(_gameDir, "CKANProfiles", "Base", "GameData", "small.txt"), 1024);
        WriteFile(Path.Combine(_gameDir, "CKANProfiles", "RP1", "GameData", "small.txt"), 1024);

        var result = await CreateService().DeduplicateProfilesAsync(InstanceName, dryRun: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.GroupCount, Is.Zero);
        Assert.That(result.Value!.DuplicateFileCount, Is.Zero);
    }

    [Test]
    public async Task Dedup_没有配置档目录时返回Ok报告()
    {
        var result = await CreateService().DeduplicateProfilesAsync(InstanceName, dryRun: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.DuplicateFileCount, Is.Zero);
        Assert.That(result.Value!.Warnings, Is.Not.Empty);
    }

    [Test]
    public async Task Dedup_实例不存在时失败()
    {
        var result = await CreateService().DeduplicateProfilesAsync("没有这个实例", dryRun: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有这个实例"));
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────

    private InstanceStorageService CreateService(IDedupLinker? linker = null)
        => new(_instances, linker ?? NativeDedupLinker.Instance);

    private (ProfileCountingLinker Linker, string Left, string Right) PrepareIdenticalProfiles()
    {
        WriteManifest(null, "Base", "RP1");
        var left = Path.Combine(_gameDir, "CKANProfiles", "Base", "GameData", "big.bin");
        var right = Path.Combine(_gameDir, "CKANProfiles", "RP1", "GameData", "big.bin");
        var payload = new byte[BigSize];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 251);
        }

        WriteFile(left, payload);
        WriteFile(right, payload);
        return (new ProfileCountingLinker(NativeDedupLinker.Instance), left, right);
    }

    private static bool IdentitiesDiffer(string left, string right)
    {
        if (!NativeDedupLinker.Instance.TryGetIdentity(left, out var leftId)
            || !NativeDedupLinker.Instance.TryGetIdentity(right, out var rightId))
        {
            return true;
        }

        return !leftId.SameAs(rightId);
    }

    private void WriteManifest(string? active, params string[] profiles)
    {
        var manifest = new ProfileManifest { ActiveProfile = active };
        foreach (var name in profiles)
        {
            manifest.Profiles.Add(new ProfileManifestEntry { Name = name });
        }

        manifest.Save(_gameDir);
    }

    private static void WriteFile(string path, int length)
    {
        var payload = new byte[length];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 241);
        }

        WriteFile(path, payload);
    }

    private static void WriteFile(string path, byte[] payload)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, payload);
    }
}
