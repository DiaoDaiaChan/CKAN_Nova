using CkanUI.App.Plugins;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;
using CkanUI.Services.Plugins;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 存储管理插件在真实程序集 + 真实宿主 DI 上的接线验证：发现 → 页签声明 → 类型解析 → ActivatorUtilities 构造。
/// </summary>
[TestFixture]
public sealed class StoragePluginIntegrationTests
{
    private const string PluginAssemblyName = "CkanUI.Plugin.Storage";
    private const string PageViewModelTypeName = "CkanUI.Plugin.Storage.UI.StoragePageViewModel";

    private static bool ReadBool(object target, string property) =>
        (bool)(target.GetType().GetProperty(property)?.GetValue(target) ?? false);

    private static string ReadString(object target, string property) =>
        target.GetType().GetProperty(property)?.GetValue(target) as string ?? string.Empty;

    [Test]
    public async Task 存储插件经宿主DI能构造出页面视图模型()
    {
        var bundledDirectory = StageBundledDirectory(ResolvePluginOutputDirectory());
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-user-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            var discovered = await service.DiscoverAsync(activateBundled: true);
            Assert.That(discovered.IsSuccess, Is.True, discovered.Message);
            Assert.That(service.Failures, Is.Empty, "内置目录里只有插件自身，不该有任何加载失败条目");

            var plugin = service.Active.SingleOrDefault(info =>
                info.Id.Equals("com.ckanui.storage", StringComparison.OrdinalIgnoreCase));
            Assert.That(plugin, Is.Not.Null, "内置目录里没有发现存储插件；实际：" + string.Join(", ", service.Bundled.Select(info => info.Id)));
            var found = plugin!;

            var descriptor = found.Pages.SingleOrDefault(page => page.Key == "storage");
            Assert.That(descriptor, Is.Not.Null, "插件没有声明 key=storage 的页签");
            var pageDescriptor = descriptor!;

            Assert.Multiple(() =>
            {
                Assert.That(pageDescriptor.Title, Is.EqualTo("存储"));
                Assert.That(pageDescriptor.Glyph, Is.EqualTo("Storage"));
                Assert.That(pageDescriptor.Section, Is.EqualTo("cache"), "存储页挂在「数据」分节下，不进侧栏一级入口");
                Assert.That(pageDescriptor.Order, Is.EqualTo(20));
                Assert.That(pageDescriptor.ViewModelTypeName, Is.EqualTo(PageViewModelTypeName));
            });

            var viewModelType = PluginPageFactory.ResolveViewModelType(pageDescriptor.ViewModelTypeName);
            Assert.That(viewModelType, Is.Not.Null, "宿主解析不到存储页的视图模型类型");
            var resolved = viewModelType!;
            Assert.That(resolved.Assembly.GetName().Name, Is.EqualTo(PluginAssemblyName));
            Assert.That(typeof(IPluginPage).IsAssignableFrom(resolved), Is.True, "插件页视图模型必须实现 IPluginPage");

            var gameDir = Path.Combine(Path.GetTempPath(), "ckanui-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(gameDir);

            using var provider = new ServiceCollection()
                .AddSingleton<IInstanceService>(new FakeInstances(new InstanceInfo("Test", gameDir, "1.12.5", true, false, 1)))
                .AddSingleton<IInstanceStorageService>(new FakeStorage())
                .BuildServiceProvider();

            // 跨 ALC 的坑：插件 ALC 里的 StoragePageViewModel 与测试程序集引用的那个不是同一个 Type，
            // 用 `as StoragePageViewModel` 在并发全量下会拿到 null（单跑时默认 ALC 的副本先命中才碰巧绿）。
            // 所以这里只认 FullName + 反射读属性，Dispose 走默认 ALC 的 IDisposable。
            var withService = PluginPageFactory.Create(provider, resolved);
            Assert.That(withService, Is.Not.Null, "宿主 DI 造不出存储页视图模型（构造函数选择或依赖解析断了）");
            Assert.That(withService!.GetType().FullName, Is.EqualTo(PageViewModelTypeName));
            Assert.That(ReadBool(withService, "HasError"), Is.False);
            ((IDisposable)withService).Dispose();

            using var bare = new ServiceCollection()
                .AddSingleton<IInstanceService>(new FakeInstances(new InstanceInfo("Test", gameDir, "1.12.5", true, false, 1)))
                .BuildServiceProvider();
            var withoutService = PluginPageFactory.Create(bare, resolved);
            Assert.That(withoutService, Is.Not.Null, "没注册存储服务也要能开出页面，而不是构造失败");
            Assert.That(withoutService!.GetType().FullName, Is.EqualTo(PageViewModelTypeName));
            var emptyDetail = ReadString(withoutService, "EmptyDetail");
            Assert.That(emptyDetail, Does.Contain("IInstanceStorageService"), "缺服务时要说清缺的是什么，不能白屏");
            Assert.That(emptyDetail, Does.Contain("宿主注册"), "还要说清这是宿主接线问题，而不是用户操作错误");
            ((IDisposable)withoutService).Dispose();

            await service.UnloadAsync(found.Id);
            Assert.That(service.Active, Is.Empty, "卸载之后不该还挂着激活记录");
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    private sealed class FakeStorage : IInstanceStorageService
    {
        public Task<OperationResult<InstanceStorageReport>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceStorageReport>.Ok(
                new InstanceStorageReport(Array.Empty<InstanceStorageRow>(), 0, 0, DateTime.UtcNow, Array.Empty<string>())));

        public Task<OperationResult<ProfileDedupReport>> DeduplicateProfilesAsync(string instanceName, bool dryRun = true, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<ProfileDedupReport>.Ok(
                new ProfileDedupReport(instanceName, dryRun, 0, 0, 0, Array.Empty<string>())));
    }

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

    private static string ResolvePluginOutputDirectory()
    {
        var testDirectory = Path.GetDirectoryName(typeof(StoragePluginIntegrationTests).Assembly.Location)!;
        var configuration = Directory.GetParent(testDirectory)?.Name ?? "Debug";

        var current = testDirectory;
        while (current is not null && !File.Exists(Path.Combine(current, "ckan-ui.sln")))
        {
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.That(current, Is.Not.Null, "找不到 ckan-ui.sln，无法定位插件工程输出目录。");
        var pluginDirectory = Path.Combine(current!, "src", PluginAssemblyName, "bin", configuration, "net10.0");
        if (!File.Exists(Path.Combine(pluginDirectory, PluginAssemblyName + ".dll")))
        {
            Assert.Ignore("还没构建 " + pluginDirectory + "（先 dotnet build src/" + PluginAssemblyName + "）");
        }

        return pluginDirectory;
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

    private sealed class FakeInstances : IInstanceService
    {
        public FakeInstances(InstanceInfo? current) => Current = current;

        public IReadOnlyList<InstanceInfo> Instances { get; } = Array.Empty<InstanceInfo>();

        public InstanceInfo? Current { get; set; }

#pragma warning disable CS0067
        public event Action? InstancesChanged;

        public event Action<InstanceInfo?>? CurrentChanged;
#pragma warning restore CS0067

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
