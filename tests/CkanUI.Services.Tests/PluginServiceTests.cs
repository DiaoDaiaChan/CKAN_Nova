using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Plugins;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 插件宿主服务（12 篇 F-17 / 06 篇 UI-33）：发现、加载、激活、停用、重载、卸载、新增 + 失败隔离。
/// </summary>
/// <remarks>
/// 口径：插件是第三方代码，宿主要求「任何失败都变成中文可读原因，绝不冒泡成异常」。
/// 因此这里既用假加载器造各种失败分类（缺依赖 / 版本不符 / 构造抛异常 / 元数据抛异常），
/// 也用真实的 <see cref="AssemblyLoadContextPluginLoader"/> 喂坏 PE 文件，验证隔离是真的。
/// </remarks>
[TestFixture]
public sealed class PluginServiceTests
{
    private TempWorkspace? _workspace;
    private string _root = string.Empty;
    private string _pluginsDirectory = string.Empty;
    private string _activationPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        // 用仓库内 artifacts/tmp 而不是系统临时目录：受限沙箱下 GetTempPath 可能拒绝建子目录。
        _workspace = new TempWorkspace("plugins");
        _root = _workspace.Path;
        _pluginsDirectory = _workspace.Combine("plugins");
        _activationPath = _workspace.Combine("plugin-activation.json");
        Directory.CreateDirectory(_pluginsDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    // ---------- 发现 ----------

    [Test]
    public async Task DiscoverAsync_LoadsEveryDllAsDormantAndNeverActivates()
    {
        WriteFakeDll("alpha.dll");
        WriteFakeDll("beta.dll");
        var loader = new FakePluginLoader();
        var service = CreateService(loader);
        var changed = 0;
        service.Changed += (_, _) => changed++;

        var result = await service.DiscoverAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(service.Dormant.Select(info => info.Name), Is.EqualTo(new[] { "alpha", "beta" }));
        Assert.That(service.Active, Is.Empty, "扫描默认不激活任何插件");
        Assert.That(changed, Is.GreaterThanOrEqualTo(2));
        Assert.That(loader.Paths.All(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task DiscoverAsync_MissingDirectory_IsCreatedAndReturnsEmpty()
    {
        var service = CreateService(new FakePluginLoader());
        Directory.Delete(_pluginsDirectory, recursive: true);

        var result = await service.DiscoverAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(service.Dormant, Is.Empty);
        Assert.That(Directory.Exists(_pluginsDirectory), Is.True, "扫描要顺手把插件目录建出来");
    }

    // ---------- 加载 ----------

    [Test]
    public async Task LoadAsync_SamePathTwice_IsRejectedAsAlreadyLoaded()
    {
        var path = WriteFakeDll("alpha.dll");
        var service = CreateService(new FakePluginLoader());

        var first = await service.LoadAsync(path);
        var second = await service.LoadAsync(path);

        Assert.That(first.IsSuccess, Is.True);
        Assert.That(second.IsSuccess, Is.False);
        Assert.That(second.Failure, Is.EqualTo(PluginLoadFailure.AlreadyLoaded));
        Assert.That(second.Message, Does.Contain("已经加载"));
        Assert.That(service.Dormant, Has.Count.EqualTo(1), "被拒的重复加载不能多出一条");
    }

    [Test]
    public async Task LoadAsync_WhenLoaderFails_RecordsReadableFailureWithoutThrowing()
    {
        var path = WriteFakeDll("broken.dll");
        var loader = new FakePluginLoader
        {
            Factory = _ => PluginAssemblyLoadAttempt.Fail(
                PluginLoadFailure.NotAnAssembly,
                "「broken.dll」不是有效的 .NET 程序集。"),
        };
        var service = CreateService(loader);

        var result = await service.LoadAsync(path);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.NotAnAssembly));
        Assert.That(result.Message, Does.Contain("不是有效的 .NET 程序集"));
        Assert.That(service.Dormant, Is.Empty);
        Assert.That(service.Failures, Has.Count.EqualTo(1), "失败要留痕，否则用户不知道 dll 为什么没出现");
        Assert.That(service.Failures[0].Name, Is.EqualTo("broken"));
        Assert.That(service.Failures[0].State, Is.EqualTo(PluginState.Failed));
        Assert.That(service.Failures[0].FailureReason, Does.Contain("不是有效的 .NET 程序集"));
    }

    [Test]
    public async Task LoadAsync_DuplicatePluginId_IsRejectedAndReleasesNewAssembly()
    {
        var first = WriteFakeDll("alpha.dll");
        var second = WriteFakeDll("beta.dll");
        var loader = new FakePluginLoader();
        loader.Factory = _ => PluginAssemblyLoadAttempt.Ok(
            new PluginAssemblyLoad(new FakePlugin { Id = "com.example.same" }, "FakePlugin", () => loader.ReleaseCalls++));
        var service = CreateService(loader);

        var ok = await service.LoadAsync(first);
        var rejected = await service.LoadAsync(second);

        Assert.That(ok.IsSuccess, Is.True);
        Assert.That(rejected.IsSuccess, Is.False);
        Assert.That(rejected.Failure, Is.EqualTo(PluginLoadFailure.DuplicateId));
        Assert.That(rejected.Message, Does.Contain("com.example.same"));
        Assert.That(loader.ReleaseCalls, Is.EqualTo(1), "被拒的重复 Id 必须把新加载的 ALC 释放掉");
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LoadAsync_PluginRequiringNewerHost_IsRejected()
    {
        var path = WriteFakeDll("future.dll");
        var loader = new FakePluginLoader();
        loader.Factory = _ => PluginAssemblyLoadAttempt.Ok(
            new PluginAssemblyLoad(
                new FakePlugin { Name = "未来插件", MinimumHostVersion = "9.9.9" },
                "FakePlugin",
                () => loader.ReleaseCalls++));
        var service = CreateService(loader, hostVersion: "1.0.0");

        var result = await service.LoadAsync(path);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.HostVersionTooOld));
        Assert.That(result.Message, Does.Contain("9.9.9"));
        Assert.That(result.Message, Does.Contain("1.0.0"));
        Assert.That(loader.ReleaseCalls, Is.EqualTo(1));
        Assert.That(service.Dormant, Is.Empty);
    }

    [Test]
    public async Task LoadAsync_UnparsableMinimumHostVersion_IsTreatedAsUnlimited()
    {
        var path = WriteFakeDll("beta.dll");
        var loader = new FakePluginLoader
        {
            Factory = _ => PluginAssemblyLoadAttempt.Ok(
                new PluginAssemblyLoad(new FakePlugin { MinimumHostVersion = "1.2.0-beta" }, "FakePlugin", () => { })),
        };
        var service = CreateService(loader, hostVersion: "0.9.0");

        var result = await service.LoadAsync(path);

        Assert.That(result.IsSuccess, Is.True, "解析不了的版本串不能把人挡在门外");
    }

    [Test]
    public async Task LoadAsync_PluginWithoutId_IsRejected()
    {
        var path = WriteFakeDll("anonymous.dll");
        var loader = new FakePluginLoader();
        loader.Factory = _ => PluginAssemblyLoadAttempt.Ok(
            new PluginAssemblyLoad(new FakePlugin { Id = "  " }, "FakePlugin", () => loader.ReleaseCalls++));
        var service = CreateService(loader);

        var result = await service.LoadAsync(path);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.LoadFailed));
        Assert.That(result.Message, Does.Contain("唯一标识"));
    }

    [Test]
    public async Task LoadAsync_PluginMetadataThrows_IsIsolatedAsMetadataFailure()
    {
        var path = WriteFakeDll("explosive.dll");
        var loader = new FakePluginLoader();
        loader.Factory = _ => PluginAssemblyLoadAttempt.Ok(
            new PluginAssemblyLoad(new FakePlugin { ThrowOnMetadata = true }, "FakePlugin", () => loader.ReleaseCalls++));
        var service = CreateService(loader);

        var result = await service.LoadAsync(path);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.MetadataFailed));
        Assert.That(result.Message, Does.Contain("读取插件元数据失败"));
        Assert.That(loader.ReleaseCalls, Is.EqualTo(1));
    }

    // ---------- 激活 / 停用 ----------

    [Test]
    public async Task ActivateAsync_InitializeThrows_StaysDormantWithReason()
    {
        var path = WriteFakeDll("alpha.dll");
        var plugin = new FakePlugin { InitializeException = new InvalidOperationException("初始化时炸了") };
        var service = CreateService(FakeLoaderFor(plugin));
        await service.LoadAsync(path);

        var result = await service.ActivateAsync(plugin.Id);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.ActivationFailed));
        Assert.That(result.Message, Does.Contain("初始化时炸了"));
        Assert.That(service.Active, Is.Empty);
        Assert.That(service.Dormant, Has.Count.EqualTo(1), "激活失败要留在未激活栏里，用户还能重试");
        Assert.That(plugin.InitializeCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task DeactivateAsync_CallsDeinitializeAndReturnsToDormant()
    {
        var path = WriteFakeDll("alpha.dll");
        var plugin = new FakePlugin();
        var service = CreateService(FakeLoaderFor(plugin));
        await service.LoadAsync(path, activate: true);
        Assert.That(service.Active, Has.Count.EqualTo(1));

        var result = await service.DeactivateAsync(plugin.Id);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(plugin.DeinitializeCalls, Is.EqualTo(1));
        Assert.That(service.Active, Is.Empty);
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DeactivateAsync_UnknownId_FailsWithChineseMessage()
    {
        var service = CreateService(new FakePluginLoader());

        var result = await service.DeactivateAsync("不存在");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有找到插件"));
    }

    // ---------- 卸载 / 重载 ----------

    [Test]
    public async Task UnloadAsync_ClearsListsAndReleasesAssembly()
    {
        var path = WriteFakeDll("alpha.dll");
        var plugin = new FakePlugin();
        var loader = FakeLoaderFor(plugin);
        var service = CreateService(loader);
        await service.LoadAsync(path, activate: true);

        var result = await service.UnloadAsync(plugin.Id);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(service.Active, Is.Empty);
        Assert.That(service.Dormant, Is.Empty);
        Assert.That(loader.ReleaseCalls, Is.EqualTo(1), "卸载必须释放可卸载 ALC");
        Assert.That(plugin.DeinitializeCalls, Is.EqualTo(1), "激活中的插件卸载前要先 Deinitialize");
    }

    [Test]
    public async Task ReloadAsync_UnloadsOldAssemblyAndKeepsActivationState()
    {
        var path = WriteFakeDll("alpha.dll");
        var plugins = new Queue<FakePlugin>();
        var first = new FakePlugin();
        var second = new FakePlugin();
        plugins.Enqueue(first);
        plugins.Enqueue(second);
        var loader = new FakePluginLoader();
        loader.Factory = _ => PluginAssemblyLoadAttempt.Ok(
            new PluginAssemblyLoad(plugins.Dequeue(), "FakePlugin", () => loader.ReleaseCalls++));
        var service = CreateService(loader);
        await service.LoadAsync(path, activate: true);

        var result = await service.ReloadAsync(first.Id);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(loader.ReleaseCalls, Is.EqualTo(1), "重载要先卸旧");
        Assert.That(first.DeinitializeCalls, Is.EqualTo(1));
        Assert.That(service.Active, Has.Count.EqualTo(1), "重载保持原激活状态");
        Assert.That(first.InitializeCalls + second.InitializeCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task ReloadAsync_UnknownId_FailsReadably()
    {
        var service = CreateService(new FakePluginLoader());

        var result = await service.ReloadAsync("不存在");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.NotFound));
    }

    // ---------- 新增 ----------

    [Test]
    public async Task AddFromPathAsync_CopiesIntoPluginsDirectoryAndLoads()
    {
        var source = Path.Combine(_root, "outside", "external.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(source, "fake");
        var service = CreateService(new FakePluginLoader());

        var result = await service.AddFromPathAsync(source);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.Exists(Path.Combine(_pluginsDirectory, "external.dll")), Is.True);
        Assert.That(service.Dormant, Has.Count.EqualTo(1));
        Assert.That(result.Message, Does.Contain("未激活"), "新增默认不激活");
    }

    [Test]
    public async Task AddFromPathAsync_NonDll_IsRejected()
    {
        var source = Path.Combine(_root, "readme.txt");
        await File.WriteAllTextAsync(source, "not a plugin");
        var service = CreateService(new FakePluginLoader());

        var result = await service.AddFromPathAsync(source);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.NotAnAssembly));
        Assert.That(result.Message, Does.Contain("只能添加 .dll"));
    }

    [Test]
    public async Task AddFromPathAsync_MissingFile_IsRejected()
    {
        var service = CreateService(new FakePluginLoader());

        var result = await service.AddFromPathAsync(Path.Combine(_root, "nope.dll"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(PluginLoadFailure.NotFound));
    }

    // ---------- 事件 ----------

    [Test]
    public async Task Changed_ThrowingSubscriber_DoesNotBreakTheLoad()
    {
        var path = WriteFakeDll("alpha.dll");
        var service = CreateService(new FakePluginLoader());
        var notified = 0;
        service.Changed += (_, _) => throw new InvalidOperationException("订阅方炸了");
        service.Changed += (_, _) => notified++;

        var result = await service.LoadAsync(path);

        Assert.That(result.IsSuccess, Is.True, "订阅方异常不能影响宿主");
        Assert.That(notified, Is.GreaterThan(0), "后面的订阅方仍要收到通知");
    }

    [Test]
    public void Cancellation_IsHonoured()
    {
        var service = CreateService(new FakePluginLoader());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await service.DiscoverAsync(cts.Token));
    }

    // ---------- 真实加载器（坏 PE / 不是插件） ----------

    [Test]
    public void RealLoader_NonManagedFile_FailsWithReadableReason()
    {
        var path = Path.Combine(_pluginsDirectory, "garbage.dll");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03 });
        var loader = new AssemblyLoadContextPluginLoader();

        var attempt = loader.TryLoad(path);

        Assert.That(attempt.IsSuccess, Is.False);
        Assert.That(attempt.Failure, Is.EqualTo(PluginLoadFailure.NotAnAssembly));
        Assert.That(attempt.Message, Does.Contain("garbage.dll"));
    }

    [Test]
    public void RealLoader_MissingFile_ReportsNotFound()
    {
        var loader = new AssemblyLoadContextPluginLoader();

        var attempt = loader.TryLoad(Path.Combine(_pluginsDirectory, "nope.dll"));

        Assert.That(attempt.IsSuccess, Is.False);
        Assert.That(attempt.Failure, Is.EqualTo(PluginLoadFailure.NotFound));
    }

    [Test]
    public void RealLoader_AssemblyWithoutPluginType_ReportsNoPluginType()
    {
        // 拿一份真实的托管程序集（契约层自己）当"不是插件"的样本：
        // 它会被加载进独立的可卸载 ALC，因此里面的类型与宿主看到的不是同一批，
        // 没有任何类型能被判定为实现 ICkanUiPlugin —— 正是隔离生效的表现。
        var source = typeof(IPluginService).Assembly.Location;
        var path = Path.Combine(_pluginsDirectory, Path.GetFileName(source));
        File.Copy(source, path, overwrite: true);
        var loader = new AssemblyLoadContextPluginLoader();

        var attempt = loader.TryLoad(path);

        Assert.That(attempt.IsSuccess, Is.False);
        Assert.That(attempt.Failure, Is.EqualTo(PluginLoadFailure.NoPluginType));
        Assert.That(attempt.Message, Does.Contain("ICkanUiPlugin"));
    }

    // ---------- 辅助 ----------

    // 激活状态必须落在临时目录：默认路径是用户的 %LocalAppData%/CKAN/plugin-activation.json，
    // 用例一旦激活插件就会往真实用户目录写记录（曾经污染过一次）。
    private PluginService CreateService(IPluginAssemblyLoader loader, string? hostVersion = null) =>
        new(_pluginsDirectory, loader, hostVersion, bundledDirectory: null, activationStore: new PluginActivationStore(_activationPath));

    private static FakePluginLoader FakeLoaderFor(FakePlugin plugin)
    {
        var loader = new FakePluginLoader();
        loader.Factory = _ => PluginAssemblyLoadAttempt.Ok(
            new PluginAssemblyLoad(plugin, "FakePlugin", () => loader.ReleaseCalls++));
        return loader;
    }

    private string WriteFakeDll(string fileName)
    {
        var path = Path.Combine(_pluginsDirectory, fileName);
        File.WriteAllText(path, "不是真正的 PE 文件，服务层用假加载器，不会真读内容。");
        return path;
    }
}

/// <summary>可编排行为的假插件（真插件需要编译程序集，测试不要求）。</summary>
internal sealed class FakePlugin : ICkanUiPlugin
{
    private string _id = "com.example.fake";

    /// <summary>抛出异常时用来验证元数据失败的隔离。</summary>
    public bool ThrowOnMetadata { get; set; }

    /// <inheritdoc />
    public string Id
    {
        get => ThrowOnMetadata ? throw new InvalidOperationException("元数据炸弹") : _id;
        set => _id = value;
    }

    /// <inheritdoc />
    public string Name { get; set; } = "fake";

    /// <inheritdoc />
    public string Version { get; set; } = "1.0.0";

    /// <inheritdoc />
    public string Author { get; set; } = "测试";

    /// <inheritdoc />
    public string Description { get; set; } = "测试用插件。";

    /// <inheritdoc />
    public string MinimumHostVersion { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages { get; set; } = Array.Empty<PluginPageDescriptor>();

    /// <summary>Initialize 的调用次数。</summary>
    public int InitializeCalls { get; private set; }

    /// <summary>Deinitialize 的调用次数。</summary>
    public int DeinitializeCalls { get; private set; }

    /// <summary>非 null 时 Initialize 抛这个异常。</summary>
    public Exception? InitializeException { get; set; }

    /// <inheritdoc />
    public void Initialize()
    {
        InitializeCalls++;
        if (InitializeException is not null)
        {
            throw InitializeException;
        }
    }

    /// <inheritdoc />
    public void Deinitialize() => DeinitializeCalls++;
}

/// <summary>假加载器：不碰文件系统，按 <see cref="Factory"/> 编排成功/失败。</summary>
internal sealed class FakePluginLoader : IPluginAssemblyLoader
{
    /// <summary>每个路径返回什么（null = 按文件名造一个成功的插件）。</summary>
    public Func<string, PluginAssemblyLoadAttempt>? Factory { get; set; }

    /// <summary>被请求加载的路径（按调用顺序）。</summary>
    public List<string> Paths { get; } = new();

    /// <summary>Release 被调用了几次（验证失败/卸载时 ALC 有没有还回去）。</summary>
    public int ReleaseCalls { get; set; }

    /// <inheritdoc />
    public PluginAssemblyLoadAttempt TryLoad(string assemblyPath)
    {
        Paths.Add(assemblyPath);
        if (Factory is not null)
        {
            return Factory(assemblyPath);
        }

        var plugin = new FakePlugin
        {
            Id = "com.example." + Path.GetFileNameWithoutExtension(assemblyPath),
            Name = Path.GetFileNameWithoutExtension(assemblyPath),
        };
        return PluginAssemblyLoadAttempt.Ok(new PluginAssemblyLoad(plugin, "FakePlugin", () => ReleaseCalls++));
    }
}
