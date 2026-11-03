using System.Text.Json;
using CkanUI.Services.Plugins;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="PluginActivationStore"/> 的口径测试（12 篇 F-17）：读坏当空表、写坏不影响调用方。
/// </summary>
/// <remarks>全部用仓库内的 <see cref="TempWorkspace"/> 造目录，不碰 <c>%TEMP%</c>（沙箱里不可写）。</remarks>
[TestFixture]
public sealed class PluginActivationStoreTests
{
    private static readonly DateTimeOffset FirstActivation = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondActivation = new(2025, 6, 7, 8, 9, 10, TimeSpan.Zero);

    private TempWorkspace? _workspace;
    private string _path = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("plugin-activation");
        _path = _workspace.Combine("state", "plugin-activation.json");
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    [Test]
    public void Load_MissingFile_ReturnsEmptyTable()
    {
        var store = CreateStore();

        var entries = store.Load();

        Assert.That(File.Exists(_path), Is.False, "读不该顺手把文件创建出来。");
        Assert.That(entries, Is.Empty);
        Assert.That(store.IsActivated("com.example.alpha"), Is.False);
    }

    [Test]
    public void Save_ThenLoad_RoundTripsStateAndTimestamp()
    {
        var store = CreateStore(FirstActivation);
        store.Save("com.example.alpha", true);

        Assert.That(store.IsActivated("com.example.alpha"), Is.True);
        Assert.That(store.Load()["com.example.alpha"].LastActivatedUtc, Is.EqualTo(FirstActivation));

        // 换一个实例（模拟进程重启）：状态必须从磁盘读回来。
        var reopened = CreateStore(SecondActivation);
        Assert.That(reopened.IsActivated("com.example.alpha"), Is.True);
        Assert.That(reopened.Load()["com.example.alpha"].LastActivatedUtc, Is.EqualTo(FirstActivation));
    }

    [Test]
    public void Save_WritesAgreedJsonShape()
    {
        var store = CreateStore(FirstActivation);
        store.Save("com.example.alpha", true);

        Assert.That(File.Exists(_path), Is.True);
        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        var entry = document.RootElement.GetProperty("com.example.alpha");
        Assert.That(entry.GetProperty("activated").GetBoolean(), Is.True);
        Assert.That(entry.GetProperty("lastActivatedUtc").ValueKind, Is.EqualTo(JsonValueKind.String));
    }

    [Test]
    public void Save_KeepsOtherEntries()
    {
        var store = CreateStore(FirstActivation);
        store.Save("com.example.alpha", true);
        store.Save("com.example.beta", true);
        store.Save("com.example.alpha", false);

        var entries = CreateStore().Load();

        Assert.That(entries.Keys, Is.EquivalentTo(new[] { "com.example.alpha", "com.example.beta" }));
        Assert.That(entries["com.example.alpha"].Activated, Is.False);
        Assert.That(entries["com.example.beta"].Activated, Is.True);
    }

    [Test]
    public void Save_Deactivate_KeepsLastActivatedTimestamp()
    {
        var store = CreateStore(FirstActivation);
        store.Save("com.example.alpha", true);

        var laterStore = CreateStore(SecondActivation);
        laterStore.Save("com.example.alpha", false);

        var entry = CreateStore().Load()["com.example.alpha"];
        Assert.That(entry.Activated, Is.False);
        Assert.That(entry.LastActivatedUtc, Is.EqualTo(FirstActivation), "停用不该清掉上次激活时间（排障要用）。");
    }

    [Test]
    public void IsActivated_IgnoresCase()
    {
        var store = CreateStore();
        store.Save("com.example.Alpha", true);

        Assert.That(store.IsActivated("COM.EXAMPLE.ALPHA"), Is.True);
        Assert.That(CreateStore().IsActivated("com.example.alpha"), Is.True);
    }

    [Test]
    public void Load_CorruptFile_ReturnsEmptyTableWithoutThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ 这不是 JSON");

        var store = CreateStore();

        Assert.That(store.Load(), Is.Empty);
        Assert.That(store.IsActivated("com.example.alpha"), Is.False);
    }

    [Test]
    public void Save_CorruptFile_OverwritesItWithValidJson()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "null");

        var store = CreateStore(FirstActivation);
        store.Save("com.example.alpha", true);

        Assert.That(CreateStore().IsActivated("com.example.alpha"), Is.True);
    }

    [Test]
    public void Save_UnwritableTarget_DoesNotThrowAndKeepsSessionState()
    {
        // 把目标路径的父目录做成一个「文件」：Directory.CreateDirectory 必然失败，模拟只读/被占。
        var blocked = _workspace!.Combine("blocked");
        File.WriteAllText(blocked, "我是文件，不是目录");
        var store = new PluginActivationStore(Path.Combine(blocked, "plugin-activation.json"), () => FirstActivation);

        Assert.DoesNotThrow(() => store.Save("com.example.alpha", true));

        Assert.That(store.IsActivated("com.example.alpha"), Is.True, "写盘失败不该让本次会话的状态查询失真。");
        Assert.That(File.Exists(Path.Combine(blocked, "plugin-activation.json")), Is.False);
    }

    [Test]
    public void Save_BlankPluginId_IsIgnored()
    {
        var store = CreateStore();

        Assert.DoesNotThrow(() => store.Save("   ", true));
        Assert.That(store.Load(), Is.Empty);
    }

    [Test]
    public void DefaultPath_IsUnderLocalApplicationDataCkan()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CKAN",
            "plugin-activation.json");

        Assert.That(PluginActivationStore.DefaultPath, Is.EqualTo(expected));
        Assert.That(new PluginActivationStore().FilePath, Is.EqualTo(expected));
    }

    private PluginActivationStore CreateStore(DateTimeOffset? now = null) =>
        new(_path, () => now ?? FirstActivation);
}
