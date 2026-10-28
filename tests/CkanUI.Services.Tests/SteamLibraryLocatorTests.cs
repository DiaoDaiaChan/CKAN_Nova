using CkanUI.Services.Platform;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// Steam 库定位（03 篇 §5 自动查找 / 06 篇 UI-32）：根来源顺序、vdf 的两个位置、失败与取消。
/// </summary>
/// <remarks>
/// 全部走注入的替身：不读注册表、不碰真实磁盘，只验「先看哪里、缺文件时退到哪、异常怎么收敛」。
/// </remarks>
[TestFixture]
public sealed class SteamLibraryLocatorTests
{
    private const string SteamRoot = @"C:\Program Files (x86)\Steam";
    private const string LibraryRoot = @"D:\SteamLibrary";
    private static readonly string GameDir = Path.Combine(LibraryRoot, "steamapps", "common", "Kerbal Space Program");

    [Test]
    public async Task DiscoverGameDirsAsync_ReadsTheModernConfigManifest()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(SteamRoot, "config", "libraryfolders.vdf")] = Manifest(LibraryRoot),
        };

        var locator = NewLocator(SteamRoot, files, new[] { GameDir });

        var result = await locator.DiscoverGameDirsAsync();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Is.EqualTo(new[] { GameDir }).AsCollection);
    }

    [Test]
    public async Task DiscoverGameDirsAsync_FallsBackToTheLegacyManifest()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf")] = Manifest(LibraryRoot),
        };

        var locator = NewLocator(SteamRoot, files, new[] { GameDir });

        var result = await locator.DiscoverGameDirsAsync();

        Assert.That(result.Value, Is.EqualTo(new[] { GameDir }).AsCollection, "旧版 Steam 只有 steamapps 下的清单");
    }

    [Test]
    public async Task DiscoverGameDirsAsync_WithoutAnyManifest_TreatsTheRootAsALibrary()
    {
        var rootGameDir = Path.Combine(SteamRoot, "steamapps", "common", "Kerbal Space Program");
        var locator = NewLocator(SteamRoot, new Dictionary<string, string>(), new[] { rootGameDir });

        var result = await locator.DiscoverGameDirsAsync();

        Assert.That(
            result.Value,
            Is.EqualTo(new[] { rootGameDir }).AsCollection,
            "默认安装的 KSP 就在 Steam 根自己的 steamapps 下");
    }

    [Test]
    public async Task DiscoverGameDirsAsync_DeduplicatesRootsListedTwice()
    {
        var manifest = Manifest(LibraryRoot);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(SteamRoot, "config", "libraryfolders.vdf")] = manifest,
            [Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf")] = manifest,
        };

        var locator = NewLocator(SteamRoot, files, new[] { GameDir });

        var result = await locator.DiscoverGameDirsAsync();

        Assert.That(result.Value, Is.EqualTo(new[] { GameDir }).AsCollection);
    }

    [Test]
    public void SteamRoots_PutsTheRegistryPathFirstAndNormalizesSlashes()
    {
        var locator = new SteamLibraryLocator(
            new SteamRegistryProbe((_, _) => "E:/Games/Steam", isWindows: true),
            _ => false,
            _ => null,
            () => new[] { SteamRoot, @"E:\Games\Steam" });

        Assert.That(
            locator.SteamRoots(),
            Is.EqualTo(new[] { @"E:\Games\Steam", SteamRoot }).AsCollection,
            "注册表优先；同一路径不同写法只留一份");
    }

    [Test]
    public async Task DiscoverGameDirsAsync_IoFailure_IsRetryable()
    {
        var locator = new SteamLibraryLocator(
            new SteamRegistryProbe((_, _) => SteamRoot, isWindows: false),
            _ => true,
            _ => throw new IOException("磁盘掉线了"),
            () => new[] { SteamRoot });

        var result = await locator.DiscoverGameDirsAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Message, Does.Contain("扫描 Steam 库失败"));
        Assert.That(result.Detail, Does.Contain("磁盘掉线了"));
    }

    [Test]
    public async Task DiscoverGameDirsAsync_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var locator = NewLocator(SteamRoot, new Dictionary<string, string>(), Array.Empty<string>());

        var result = await locator.DiscoverGameDirsAsync(cts.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("已取消"));
    }

    [Test]
    public void RegistryProbe_WithoutWindows_DoesNotTouchTheRegistry()
    {
        var called = false;
        var probe = new SteamRegistryProbe(
            (_, _) =>
            {
                called = true;
                return SteamRoot;
            },
            isWindows: false);

        Assert.That(probe.TryGetSteamPath(), Is.Null);
        Assert.That(called, Is.False);
    }

    [Test]
    public void RegistryProbe_ReadsAndNormalizesTheSteamPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("注册表读取只在 Windows 上会真的发生。");
        }

        var probe = new SteamRegistryProbe((key, value) =>
        {
            Assert.That(key, Is.EqualTo(SteamRegistryProbe.SteamKey));
            Assert.That(value, Is.EqualTo(SteamRegistryProbe.SteamPathValue));
            return "C:/Program Files (x86)/Steam";
        });

        Assert.That(probe.TryGetSteamPath(), Is.EqualTo(SteamRoot));
    }

    [Test]
    public void RegistryProbe_SwallowsAccessFailures()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("注册表读取只在 Windows 上会真的发生。");
        }

        var probe = new SteamRegistryProbe((_, _) => throw new UnauthorizedAccessException("拒绝访问"));

        Assert.That(probe.TryGetSteamPath(), Is.Null, "读不到等于没装，不该让添加实例整页报错");
    }

    private static SteamLibraryLocator NewLocator(
        string steamRoot,
        IDictionary<string, string> files,
        IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return new SteamLibraryLocator(
            // isWindows 传 true 才让注入的读值生效；读值本身是替身，不会真去碰注册表。
            new SteamRegistryProbe((_, _) => steamRoot, isWindows: true),
            set.Contains,
            path => files.TryGetValue(path, out var content) ? content : null,
            () => Array.Empty<string>());
    }

    private static string Manifest(string root) =>
        "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\" \"" + root.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"\n\t}\n}\n";
}
