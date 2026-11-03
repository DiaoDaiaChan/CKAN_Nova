using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Plugins;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 内置（首方）插件目录 + 启动期引导的测试（12 篇 F-17）。
/// </summary>
/// <remarks>
/// <para>覆盖三条不能松的口径：内置插件也默认 Dormant；同 Id 时内置优先且用户目录那份要留痕；
/// 只有「用户显式激活过」或「提供方 activateBundled:true」才会激活。</para>
/// <para>引导流程额外验证「永不抛异常」：目录读不到、插件初始化炸、激活状态文件坏、令牌已取消，都不该冒泡。</para>
/// </remarks>
[TestFixture]
public sealed class PluginBundledDiscoveryTests
{
    private TempWorkspace? _workspace;
    private string _bundled = string.Empty;
    private string _user = string.Empty;
    private string _statePath = string.Empty;
    private PluginActivationStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("plugin-bundled");
        _bundled = _workspace.Combine("bundled");
        _user = _workspace.Combine("user");
        Directory.CreateDirectory(_bundled);
        Directory.CreateDirectory(_user);
        _statePath = _workspace.Combine("state", "plugin-activation.json");
        _store = new PluginActivationStore(_statePath);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    // ---------- 目录与默认值 ----------

    [Test]
    public void BundledDirectory_DefaultsToAppBasePlugins()
    {
        var service = new PluginService(_user, new FakePluginLoader(), null);

        Assert.That(service.BundledDirectory, Is.EqualTo(Path.Combine(AppContext.BaseDirectory, "plugins")));
        Assert.That(service.BundledDirectory, Is.EqualTo(PluginService.DefaultBundledDirectory()));
        Assert.That(service.Bundled, Is.Empty);
    }

    [Test]
    public void PluginInfo_IsBundled_DefaultsToFalse()
    {
        var info = new PluginInfo("com.example.x", "x", "1.0", "作者", "描述", string.Empty, "x.dll", PluginState.Dormant);

        Assert.That(info.IsBundled, Is.False);
    }

    [Test]
    public async Task DiscoverAsync_BundledPlugin_DefaultsToDormant()
    {
        WriteDll(_bundled, "bundled.dll");
        var service = CreateService();

        await service.DiscoverAsync();

        Assert.That(service.Active, Is.Empty, "内置插件也必须默认不启用。");
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
        Assert.That(service.Dormant[0].IsBundled, Is.True);
        Assert.That(service.Bundled, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DiscoverAsync_UserPlugin_IsNotBundled()
    {
        WriteDll(_user, "user.dll");
        var service = CreateService();

        await service.DiscoverAsync();

        Assert.That(service.Dormant, Has.Count.EqualTo(1));
        Assert.That(service.Dormant[0].IsBundled, Is.False);
        Assert.That(service.Bundled, Is.Empty);
    }

    [Test]
    public async Task DiscoverAsync_ActivateBundled_ActivatesOnlyBundledPlugins()
    {
        WriteDll(_bundled, "bundled.dll");
        WriteDll(_user, "user.dll");
        var created = new List<FakePlugin>();
        var loader = LoaderCapturing(created);
        var service = CreateService(loader);

        await service.DiscoverAsync(activateBundled: true);

        Assert.That(service.Active, Has.Count.EqualTo(1));
        Assert.That(service.Active[0].Id, Is.EqualTo("com.example.bundled"));
        Assert.That(service.Active[0].IsBundled, Is.True);
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
        Assert.That(service.Dormant[0].Id, Is.EqualTo("com.example.user"), "activateBundled 不许顺手激活用户插件。");
        Assert.That(created.Single(plugin => plugin.Id == "com.example.bundled").InitializeCalls, Is.EqualTo(1));
        Assert.That(created.Single(plugin => plugin.Id == "com.example.user").InitializeCalls, Is.Zero);
    }

    [Test]
    public async Task DiscoverAsync_BundledPluginPreviouslyActivated_IsRestoredWithoutFlag()
    {
        WriteDll(_bundled, "bundled.dll");
        _store.Save("com.example.bundled", true);
        var service = CreateService();

        await service.DiscoverAsync();

        Assert.That(service.Active, Has.Count.EqualTo(1));
        Assert.That(service.Active[0].Id, Is.EqualTo("com.example.bundled"));
    }

    [Test]
    public async Task DiscoverAsync_BundledPluginPreviouslyDeactivated_StaysDormant()
    {
        WriteDll(_bundled, "bundled.dll");
        _store.Save("com.example.bundled", true);
        _store.Save("com.example.bundled", false);
        var service = CreateService();

        await service.DiscoverAsync();

        Assert.That(service.Active, Is.Empty);
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DiscoverAsync_MissingBundledDirectory_IsNotCreatedByDiscovery()
    {
        var missingBundled = _workspace!.Combine("no-such-bundled");
        WriteDll(_user, "user.dll");
        var service = CreateService(bundledDirectory: missingBundled);

        var result = await service.DiscoverAsync();

        // 安装目录由发布包提供，扫描不该在安装目录里造目录（用户数据目录那边才是 EnsureDirectory 的地盘）。
        Assert.That(Directory.Exists(missingBundled), Is.False);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
    }

    // ---------- 同 Id 冲突 ----------

    [Test]
    public async Task DiscoverAsync_SameIdInBothDirectories_BundledWinsAndUserCopyIsReported()
    {
        var bundledPath = WriteDll(_bundled, "bundled.dll");
        WriteDll(_user, "user-copy.dll");
        var loader = SameIdLoader("com.example.dup");
        var service = CreateService(loader);

        await service.DiscoverAsync();

        Assert.That(service.Dormant, Has.Count.EqualTo(1), "同 Id 只留一个。");
        Assert.That(service.Dormant[0].IsBundled, Is.True);
        Assert.That(service.Dormant[0].AssemblyPath, Is.EqualTo(bundledPath), "内置优先。");
        Assert.That(service.Bundled, Has.Count.EqualTo(1));
        Assert.That(loader.ReleaseCalls, Is.EqualTo(1), "被顶掉的那份必须释放 ALC。");

        Assert.That(service.Failures, Has.Count.EqualTo(1));
        Assert.That(service.Failures[0].Name, Is.EqualTo("user-copy"));
        Assert.That(service.Failures[0].FailureReason, Does.Contain("内置插件优先"));
    }

    [Test]
    public async Task DiscoverAsync_TwoUserPluginsWithSameId_StaysSilent()
    {
        WriteDll(_user, "one.dll");
        WriteDll(_user, "two.dll");
        var loader = SameIdLoader("com.example.same");
        var service = CreateService(loader);

        await service.DiscoverAsync();

        // 既有行为不回归：用户目录内部撞 Id 只是静默跳过（不新增失败条目）。
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
        Assert.That(service.Failures, Is.Empty);
        Assert.That(loader.ReleaseCalls, Is.EqualTo(1));
    }

    // ---------- 启动期引导 ----------

    [Test]
    public async Task RunAsync_CreatesBothDirectories()
    {
        var bundled = _workspace!.Combine("fresh-bundled");
        var user = _workspace.Combine("fresh-user");
        var service = CreateService(new FakePluginLoader(), bundled, user);

        await new PluginBootstrapper(service, _store).RunAsync();

        Assert.That(Directory.Exists(bundled), Is.True);
        Assert.That(Directory.Exists(user), Is.True);
    }

    [Test]
    public async Task RunAsync_ActivatesBundledAndLeavesUserPluginsDormant()
    {
        WriteDll(_bundled, "bundled.dll");
        WriteDll(_user, "user.dll");
        var service = CreateService();

        await new PluginBootstrapper(service, _store).RunAsync();

        Assert.That(service.Active, Has.Count.EqualTo(1));
        Assert.That(service.Active[0].Id, Is.EqualTo("com.example.bundled"));
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
        Assert.That(service.Dormant[0].Id, Is.EqualTo("com.example.user"));
    }

    [Test]
    public async Task RunAsync_RestoresPersistedUserActivation()
    {
        WriteDll(_user, "user.dll");
        _store.Save("com.example.user", true);
        var created = new List<FakePlugin>();
        var service = CreateService(LoaderCapturing(created));

        await new PluginBootstrapper(service, _store).RunAsync();

        Assert.That(service.Active, Has.Count.EqualTo(1));
        Assert.That(service.Active[0].Id, Is.EqualTo("com.example.user"));
        Assert.That(created.Single(plugin => plugin.Id == "com.example.user").InitializeCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_AlreadyActiveBundledPlugin_IsNotInitializedTwice()
    {
        WriteDll(_bundled, "bundled.dll");
        _store.Save("com.example.bundled", true);
        var created = new List<FakePlugin>();
        var service = CreateService(LoaderCapturing(created));

        await new PluginBootstrapper(service, _store).RunAsync();

        // 发现阶段已经激活过，恢复阶段必须跳过（重复 Initialize 是插件自己的噩梦）。
        Assert.That(created.Single(plugin => plugin.Id == "com.example.bundled").InitializeCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_PluginInitializeThrows_IsSwallowed()
    {
        WriteDll(_bundled, "bundled.dll");
        var loader = new FakePluginLoader();
        loader.Factory = _ =>
        {
            var plugin = new FakePlugin
            {
                Id = "com.example.bundled",
                Name = "bundled",
                InitializeException = new InvalidOperationException("插件初始化炸弹"),
            };
            return PluginAssemblyLoadAttempt.Ok(new PluginAssemblyLoad(plugin, "FakePlugin", () => loader.ReleaseCalls++));
        };
        var service = CreateService(loader);

        Assert.DoesNotThrowAsync(async () => await new PluginBootstrapper(service, _store).RunAsync());
        Assert.That(service.Active, Is.Empty);
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_CorruptActivationFile_StillCompletes()
    {
        WriteDll(_bundled, "bundled.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, "{ 坏掉的 JSON");
        var service = CreateService();

        Assert.DoesNotThrowAsync(async () => await new PluginBootstrapper(service, _store).RunAsync());
        Assert.That(service.Active, Has.Count.EqualTo(1), "激活状态文件坏了不该影响内置插件的正常激活。");
    }

    [Test]
    public async Task RunAsync_ServiceThrows_IsSwallowed()
    {
        Assert.DoesNotThrowAsync(async () => await new PluginBootstrapper(new ThrowingPluginService(), _store).RunAsync());
    }

    [Test]
    public async Task RunAsync_CancelledToken_IsSwallowed()
    {
        WriteDll(_bundled, "bundled.dll");
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.DoesNotThrowAsync(async () => await new PluginBootstrapper(service, _store).RunAsync(cts.Token));
        Assert.That(service.Active, Is.Empty);
    }

    // ---------- 辅助 ----------

    private PluginService CreateService(
        IPluginAssemblyLoader? loader = null,
        string? bundledDirectory = null,
        string? userDirectory = null) =>
        new(
            userDirectory ?? _user,
            loader ?? new FakePluginLoader(),
            null,
            bundledDirectory ?? _bundled,
            _store);

    private static string WriteDll(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "假 dll：加载器是假的，不会真读 PE 头。");
        return path;
    }

    /// <summary>按路径造插件并保留实例，便于断言 <see cref="FakePlugin.InitializeCalls"/>。</summary>
    private static FakePluginLoader LoaderCapturing(List<FakePlugin> created)
    {
        var loader = new FakePluginLoader();
        loader.Factory = path =>
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var plugin = new FakePlugin { Id = "com.example." + name, Name = name };
            created.Add(plugin);
            return PluginAssemblyLoadAttempt.Ok(new PluginAssemblyLoad(plugin, "FakePlugin", () => loader.ReleaseCalls++));
        };
        return loader;
    }

    /// <summary>无论哪个文件都返回同一个 Id 的加载器（造同 Id 冲突用）。</summary>
    private static FakePluginLoader SameIdLoader(string id)
    {
        var loader = new FakePluginLoader();
        loader.Factory = _ =>
        {
            var plugin = new FakePlugin { Id = id, Name = "同 Id 插件" };
            return PluginAssemblyLoadAttempt.Ok(new PluginAssemblyLoad(plugin, "FakePlugin", () => loader.ReleaseCalls++));
        };
        return loader;
    }

    /// <summary>所有成员都抛异常的替身：验证引导流程真的把异常吞住了。</summary>
    private sealed class ThrowingPluginService : IPluginService
    {
        public string PluginsDirectory => throw new InvalidOperationException("目录读取炸弹");

        public string BundledDirectory => throw new InvalidOperationException("内置目录读取炸弹");

        public IReadOnlyList<PluginInfo> Active => throw new InvalidOperationException("激活列表炸弹");

        public IReadOnlyList<PluginInfo> Dormant => throw new InvalidOperationException("未激活列表炸弹");

        public IReadOnlyList<PluginInfo> Failures => throw new InvalidOperationException("失败列表炸弹");

        public event EventHandler? Changed
        {
            add => throw new InvalidOperationException("订阅炸弹");
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("扫描炸弹");

        public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(bool activateBundled, CancellationToken ct = default) =>
            throw new InvalidOperationException("扫描炸弹");

        public Task<PluginLoadResult> LoadAsync(string pluginPath, bool activate = false, CancellationToken ct = default) =>
            throw new InvalidOperationException("加载炸弹");

        public Task<PluginLoadResult> ActivateAsync(string pluginId, CancellationToken ct = default) =>
            throw new InvalidOperationException("激活炸弹");

        public Task<OperationResult> DeactivateAsync(string pluginId, CancellationToken ct = default) =>
            throw new InvalidOperationException("停用炸弹");

        public Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken ct = default) =>
            throw new InvalidOperationException("重载炸弹");

        public Task<OperationResult> UnloadAsync(string pluginId, CancellationToken ct = default) =>
            throw new InvalidOperationException("卸载炸弹");

        public Task<PluginLoadResult> AddFromPathAsync(string sourcePath, bool activate = false, CancellationToken ct = default) =>
            throw new InvalidOperationException("新增炸弹");
    }
}
