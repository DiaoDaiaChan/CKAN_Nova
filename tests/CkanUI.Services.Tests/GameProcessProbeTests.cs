using CkanUI.Services.Instances;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 12 篇 F-33：游戏是否在运行的判定是纯函数，必须只靠路径前缀，不许抛异常。
/// </summary>
[TestFixture]
public sealed class GameProcessProbeTests
{
    [TestCase(@"C:\Games\KSP\KSP_x64.exe", @"C:\Games\KSP", true)]
    [TestCase(@"C:\Games\KSP\Launcher\KSP.exe", @"C:\Games\KSP", true)]
    [TestCase(@"C:\Games\KSP2\KSP2_x64.exe", @"C:\Games\KSP", false)]
    [TestCase(@"C:\Games\KSP_x64.exe", @"C:\Games\KSP", false)]
    public void IsInsideGameDir_ComparesPathPrefixes(string image, string gameDir, bool expected) =>
        Assert.That(GameProcessProbe.IsInsideGameDir(image, gameDir), Is.EqualTo(expected));

    [Test]
    public void IsInsideGameDir_IsCaseInsensitive() =>
        Assert.That(GameProcessProbe.IsInsideGameDir(@"c:/games/ksp/ksp.exe", @"C:\Games\KSP"), Is.True);

    [Test]
    public void IsInsideGameDir_AcceptsTrailingSeparatorOnGameDir() =>
        Assert.That(GameProcessProbe.IsInsideGameDir(@"C:\Games\KSP\bin\ksp.exe", @"C:\Games\KSP\\"), Is.True);

    [TestCase(null, @"C:\Games\KSP")]
    [TestCase("", @"C:\Games\KSP")]
    [TestCase(@"C:\Games\KSP\ksp.exe", null)]
    [TestCase(@"C:\Games\KSP\ksp.exe", "")]
    public void IsInsideGameDir_ReturnsFalseForMissingInput(string? image, string? gameDir) =>
        Assert.That(GameProcessProbe.IsInsideGameDir(image, gameDir), Is.False);

    [Test]
    public void IsInsideGameDir_DoesNotThrowOnInvalidPath() =>
        Assert.That(GameProcessProbe.IsInsideGameDir("C:\\a\\0\0::bad", "C:\\a"), Is.False);

    [Test]
    public void GetProcessImagePaths_ReusesScanWithinCacheWindow()
    {
        var now = DateTimeOffset.UnixEpoch;
        var scans = 0;
        var probe = new GameProcessProbe(
            () =>
            {
                scans++;
                return new[] { @"C:\Games\KSP\KSP_x64.exe" };
            },
            () => now);

        var first = probe.GetProcessImagePaths();
        now = now.AddMilliseconds(300);
        var second = probe.GetProcessImagePaths();

        Assert.That(scans, Is.EqualTo(1), "一秒内重复问不该再扫一遍进程表");
        Assert.That(ReferenceEquals(first, second), Is.True);
    }

    [Test]
    public void GetProcessImagePaths_RescansAfterCacheWindow()
    {
        var now = DateTimeOffset.UnixEpoch;
        var scans = 0;
        var probe = new GameProcessProbe(
            () =>
            {
                scans++;
                return Array.Empty<string>();
            },
            () => now);

        _ = probe.GetProcessImagePaths();
        now = now.Add(GameProcessProbe.CacheTtl);
        _ = probe.GetProcessImagePaths();

        Assert.That(scans, Is.EqualTo(2));
    }

    [Test]
    public void GetProcessImagePaths_NeverThrows()
    {
        var paths = new GameProcessProbe().GetProcessImagePaths();
        Assert.That(paths, Is.Not.Null);
        Assert.That(paths.All(p => !string.IsNullOrWhiteSpace(p)), Is.True);
    }
}
