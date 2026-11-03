using CkanUI.App.Plugins;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Plugins;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 首方插件（性能页签）在<b>真实程序集</b>上的接线验证：发现 → 页签描述 → 视图模型解析 → 构造。
/// </summary>
/// <remarks>
/// <para>用真 dll 而不是假加载器，是因为这条链上每一环都只对真程序集成立：ALC 里的类型要靠
/// 逐个程序集查找才拿得到，<see cref="PluginPageFactory"/> 的注释里专门写了为什么不缓存 Type。</para>
/// <para>找不到 dll 时 <see cref="Assert.Ignore(string)"/> 而不是失败：测试不该替"你还没构建它"背锅。
/// 但只要构建过，这条链断了就必须红——这正是"插件页签是个占位符"的原始病灶。</para>
/// </remarks>
[TestFixture]
public class PluginPerformanceIntegrationTests
{
    private const string PluginAssemblyName = "CkanUI.Plugin.Performance";

    [Test]
    public async Task 首方性能插件能被发现且页签描述与视图模型都对()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        // 真实发布形态：$(OutDir)plugins 下只有插件自己的 dll（+pdb），不带依赖副本。
        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-plugins-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            var result = await service.DiscoverAsync(activateBundled: true);

            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(service.Failures, Is.Empty, "内置目录里只有插件自身，不该有任何加载失败条目");

            var plugin = service.Bundled.SingleOrDefault(info =>
                info.Id.Equals("com.ckanui.performance", StringComparison.OrdinalIgnoreCase));

            Assert.That(plugin, Is.Not.Null, "内置目录里没有发现性能插件；实际发现：" + string.Join(", ", service.Bundled.Select(i => i.Id)));
            var found = plugin!;

            Assert.Multiple(() =>
            {
                Assert.That(found.Name, Is.EqualTo("性能"));
                Assert.That(found.IsBundled, Is.True);
                // 内置 + activateBundled:true = 宿主为自己带的插件背书，应当已经激活；
                // 未激活的插件页签不会进导航栏，等于没做。
                Assert.That(found.State, Is.EqualTo(PluginState.Active));
            });

            var page = found.Pages.SingleOrDefault(descriptor => descriptor.Key == "performance");
            Assert.That(page, Is.Not.Null, "插件没有声明 key=performance 的页签");
            var pageDescriptor = page!;

            Assert.Multiple(() =>
            {
                Assert.That(pageDescriptor.Title, Is.EqualTo("性能"));
                Assert.That(pageDescriptor.ViewModelTypeName, Is.EqualTo("CkanUI.Plugin.Performance.PerformancePageViewModel"));
            });

            // 关键一环：类型住在可卸载 ALC 里，只能按名字在已加载程序集里找。
            var resolved = PluginPageFactory.ResolveViewModelType(pageDescriptor.ViewModelTypeName);
            Assert.That(resolved, Is.Not.Null, "宿主解析不到插件页的视图模型类型（ALC 隔离出问题了）");
            var viewModelType = resolved!;

            Assert.Multiple(() =>
            {
                Assert.That(viewModelType.FullName, Is.EqualTo(pageDescriptor.ViewModelTypeName));
                Assert.That(viewModelType.Assembly.GetName().Name, Is.EqualTo(PluginAssemblyName));
                Assert.That(typeof(IPluginPage).IsAssignableFrom(viewModelType), Is.True, "视图模型必须实现 IPluginPage 标记接口");
            });

            // 再往下一环：宿主用 ActivatorUtilities 构造插件页，构造期可以注入宿主服务（这里是 IInstanceService）。
            using var provider = new ServiceCollection()
                .AddSingleton<IInstanceService>(new FakeInstances())
                .BuildServiceProvider();

            var viewModel = PluginPageFactory.Create(provider, viewModelType);
            Assert.That(viewModel, Is.Not.Null, "宿主构造插件页失败（构造函数注入的依赖没接上？）");
            Assert.That(viewModel!.GetType().FullName, Is.EqualTo(pageDescriptor.ViewModelTypeName));

            if (viewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // 卸载是插件系统的底线能力：ALC 要能释放，否则改一次插件就得重启宿主。
            var unload = await service.UnloadAsync(found.Id);
            Assert.That(unload.IsSuccess, Is.True, unload.Message);
            Assert.That(service.Bundled, Is.Empty, "卸载后内置列表应清空");
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    [Test]
    public async Task 内置插件同Id时用户目录里的同Id插件被判重复()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-plugins-dup-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-dup-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            // 把同一份 dll 放进用户目录，模拟"用户自己也装了一份性能插件"。
            Directory.CreateDirectory(userDirectory);
            File.Copy(
                Path.Combine(bundledDirectory, PluginAssemblyName + ".dll"),
                Path.Combine(userDirectory, PluginAssemblyName + ".dll"),
                overwrite: true);

            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            var result = await service.DiscoverAsync(activateBundled: true);

            Assert.That(result.IsSuccess, Is.True, result.Message);

            var diagnostics =
                "Bundled=[" + string.Join(" | ", service.Bundled.Select(i => i.Id + "/" + i.IsBundled + "/" + i.AssemblyPath)) + "] " +
                "Failures=[" + string.Join(" | ", service.Failures.Select(i => i.Id + ":" + i.FailureReason)) + "]";
            TestContext.Out.WriteLine(diagnostics);

            Assert.Multiple(() =>
            {
                // 内置优先：只留一份，且是内置那份。
                Assert.That(service.Bundled.Count, Is.EqualTo(1), diagnostics);
                Assert.That(service.Bundled[0].IsBundled, Is.True, diagnostics);
                Assert.That(service.Failures.Count, Is.EqualTo(1), diagnostics);
                Assert.That(service.Failures[0].State, Is.EqualTo(PluginState.Failed), diagnostics);
                Assert.That(service.Failures[0].FailureReason, Does.Contain("DuplicateId").Or.Contain("同一个标识"), diagnostics);
            });

            var unload = await service.UnloadAsync(service.Bundled[0].Id);
            Assert.That(unload.IsSuccess, Is.True, unload.Message);
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    [Test]
    public async Task 激活后的性能页签进入插件页签目录()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-plugins-nav-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-nav-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            await service.DiscoverAsync(activateBundled: true);
            var pluginId = service.Active[0].Id;

            // 宿主既有导航键（顺序与 ShellViewModel._baseNavItems 一致）：插件页签要排在插件之后。
            var hostKeys = new[]
            {
                "mods", "content", "changes", "repos", "modpacks", "modpack-editor", "instances", "playtime",
                "unmanaged", "cache", "downloadstats", "dedup", "installhistory", "plugins", "logs",
                "settings", "home",
            };

            var entries = PluginPageCatalog.Collect(service.Active, hostKeys);

            Assert.That(entries, Has.Count.EqualTo(1), "激活的性能插件应当恰好贡献一个页签");
            var entry = entries[0];
            Assert.Multiple(() =>
            {
                Assert.That(entry.Key, Is.EqualTo("performance"));
                Assert.That(entry.Title, Is.EqualTo("性能"));
                Assert.That(entry.ViewModelTypeName, Is.EqualTo("CkanUI.Plugin.Performance.PerformancePageViewModel"));
                // DisplayName 带版本，用于两个插件页签重名时点名来源。
                Assert.That(entry.PluginName, Is.EqualTo("性能 - 1.0.0"));
            });

            await service.UnloadAsync(pluginId);
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    [Test]
    public async Task 未激活的内置插件不会产生页签()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-plugins-dormant-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-dormant-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            // 默认路径（不传 activateBundled）＝不给内置插件背书，插件必须留在 Dormant。
            await service.DiscoverAsync();

            Assert.Multiple(() =>
            {
                Assert.That(service.Bundled, Has.Count.EqualTo(1));
                Assert.That(service.Bundled[0].State, Is.EqualTo(PluginState.Dormant));
                Assert.That(service.Active, Is.Empty);
            });

            Assert.That(PluginPageCatalog.Collect(service.Active, new[] { "plugins" }), Is.Empty, "未激活的插件不该往导航栏塞页签");

            await service.UnloadAsync(service.Bundled[0].Id);
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    /// <summary>
    /// 把插件工程输出目录里"属于插件自己"的文件复制到一个干净的暂存目录。
    /// </summary>
    /// <remarks>
    /// 直接拿 bin 目录当内置目录会带上 CopyLocal 的依赖副本（CkanUI.Services.Abstractions.dll 等），
    /// 扫描器会把每个 dll 都当候选插件、为依赖各留一条"没有实现 ICkanUiPlugin"的失败记录。
    /// 真实发布的 plugins/ 只有插件自己的 dll，所以测试也该按这个形态来。
    /// </remarks>
    private static string StageBundledDirectory(string pluginDirectory)
    {
        var staged = Path.Combine(Path.GetTempPath(), "ckanui-bundled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staged);

        foreach (var extension in new[] { ".dll", ".pdb" })
        {
            var source = Path.Combine(pluginDirectory, PluginAssemblyName + extension);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(staged, PluginAssemblyName + extension), overwrite: true);
            }
        }

        return staged;
    }

    /// <summary>
    /// 找插件工程的构建输出目录：与测试程序集同配置（Debug/Release）。
    /// </summary>
    private static string ResolvePluginOutputDirectory()
    {
        var testDirectory = Path.GetDirectoryName(typeof(PluginPerformanceIntegrationTests).Assembly.Location)!;
        var configuration = Directory.GetParent(testDirectory)?.Name ?? "Debug";
        var repoRoot = FindRepoRoot(testDirectory);

        var pluginDirectory = Path.Combine(
            repoRoot,
            "src",
            PluginAssemblyName,
            "bin",
            configuration,
            "net10.0");

        if (!File.Exists(Path.Combine(pluginDirectory, PluginAssemblyName + ".dll")))
        {
            Assert.Ignore("还没构建 " + pluginDirectory + "（先 dotnet build src/" + PluginAssemblyName + "）");
        }

        return pluginDirectory;
    }

    private static string FindRepoRoot(string start)
    {
        var current = start;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "ckan-ui.sln")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("找不到 ckan-ui.sln，无法定位插件工程输出目录。");
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 收尾失败不该让用例变红。
        }
    }

    [Test]
    public async Task 引导完成后性能页签出现在导航栏的插件之后()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-user-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var store = new PluginActivationStore(storePath);
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: store);

            // 真宿主里 ShellViewModel 先构造、引导在后台跑完再靠 Changed 事件把页签补进导航栏。
            // 这里把两步按同样顺序走一遍：构造时没有页签，RunAsync 之后必须出现。
            var shell = new ShellViewModel(new SilentNavigation(), plugins: service);
            Assert.That(shell.NavItems.Any(item => item.Key == "performance"), Is.False, "引导之前不该有插件页签");

            await new PluginBootstrapper(service, store).RunAsync();

            var items = shell.NavItems.ToList();
            var index = items.FindIndex(item => item.Key == "performance");
            Assert.That(index, Is.GreaterThan(0), "引导完成后导航栏里没有性能页签；插件服务 Changed 事件与导航栏重建之间断了");

            Assert.Multiple(() =>
            {
                Assert.That(items[index].Title, Is.EqualTo("性能"));
                Assert.That(items[index - 1].Key, Is.EqualTo("plugins"), "插件页签必须紧跟在「插件」管理入口之后");
            });
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }


    private sealed class FakeInstances : IInstanceService
    {
        public IReadOnlyList<InstanceInfo> Instances { get; } = Array.Empty<InstanceInfo>();

        public InstanceInfo? Current { get; set; }

        public event Action? InstancesChanged;

        public event Action<InstanceInfo?>? CurrentChanged;

        public bool IsGameRunning(string instanceName = "") => false;

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不切换实例"));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不添加实例"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不克隆实例"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不重命名实例"));

        public void RaiseCurrentChanged(InstanceInfo? instance) => CurrentChanged?.Invoke(instance);

        public void RaiseInstancesChanged() => InstancesChanged?.Invoke();
    }
}
