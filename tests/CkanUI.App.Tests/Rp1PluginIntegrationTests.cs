using System.Reflection;
using CkanUI.App.Plugins;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Plugins;
using CkanUI.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// RP-1 生涯日志插件在真实程序集 + 真实宿主 DI 上的接线验证：发现 → 页签声明 → 类型解析 → ActivatorUtilities 构造。
/// </summary>
[TestFixture]
public sealed class Rp1PluginIntegrationTests
{
    private const string PluginAssemblyName = "CkanUI.Plugin.Rp1";

    [Test]
    public async Task RP1生涯日志插件经宿主DI能构造出页面视图模型()
    {
        using var temp = new TempWorkspace("rp1-integration-test");
        var bundledDirectory = StageBundledDirectory(ResolvePluginOutputDirectory(), temp);
        var userDirectory = temp.Combine("user");
        var storePath = temp.Combine("activation.json");
        var gameDir = temp.Combine("game");
        Directory.CreateDirectory(Path.Combine(gameDir, "GameData", "RP-1"));
        Directory.CreateDirectory(Path.Combine(gameDir, "saves", "Test"));

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
                info.Id.Equals("com.ckanui.rp1", StringComparison.OrdinalIgnoreCase));
            Assert.That(plugin, Is.Not.Null, "内置目录里没有发现 RP-1 插件；实际：" + string.Join(", ", service.Bundled.Select(info => info.Id)));
            var found = plugin!;

            var descriptor = found.Pages.SingleOrDefault(page => page.Key == "rp1-career");
            Assert.That(descriptor, Is.Not.Null, "插件没有声明 key=rp1-career 的页签");
            var pageDescriptor = descriptor!;

            Assert.Multiple(() =>
            {
                Assert.That(pageDescriptor.Title, Is.EqualTo("RP-1 生涯日志"));
                Assert.That(pageDescriptor.Glyph, Is.EqualTo("History"));
                Assert.That(pageDescriptor.Section, Is.Null, "独立侧栏入口：生涯日志页签独立成Tab");
                Assert.That(pageDescriptor.Order, Is.EqualTo(40));
                Assert.That(pageDescriptor.ViewModelTypeName, Is.EqualTo("CkanUI.Plugin.Rp1.UI.Rp1CareerPageViewModel"));
            });

            var viewModelType = PluginPageFactory.ResolveViewModelType(pageDescriptor.ViewModelTypeName);
            Assert.That(viewModelType, Is.Not.Null, "宿主解析不到 RP-1 生涯日志页的视图模型类型");
            var resolved = viewModelType!;
            Assert.That(resolved.Assembly.GetName().Name, Is.EqualTo(PluginAssemblyName));
            Assert.That(typeof(IPluginPage).IsAssignableFrom(resolved), Is.True, "插件页视图模型必须实现 IPluginPage");

            using var provider = new ServiceCollection()
                .AddSingleton<IInstanceService>(new FakeInstances(new InstanceInfo("Test", gameDir, "1.12.5", true, false, 1)))
                .BuildServiceProvider();

            var viewModel = PluginPageFactory.Create(provider, resolved);
            Assert.That(viewModel, Is.Not.Null, "宿主 DI 造不出 RP-1 生涯日志页视图模型（构造函数选择或依赖解析断了）");

            var careerServiceField = resolved.GetField("_career", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(careerServiceField, Is.Not.Null, "视图模型的结构变了：找不到 IRp1CareerService 字段，这条用例需要跟着改");
            Assert.That(
                careerServiceField!.GetValue(viewModel),
                Is.Not.Null,
                "只注册 IInstanceService 时，页面必须自己接上插件内的生涯日志服务，而不是退化成一个空壳");

            Assert.That(viewModel, Is.InstanceOf<IDisposable>());
            ((IDisposable)viewModel!).Dispose();

            await service.UnloadAsync(found.Id);
            Assert.That(service.Active, Is.Empty, "卸载之后不该还挂着激活记录");
        }
        finally
        {
            // temp.Dispose() 会递归清理整个临时沙箱工作目录。
        }
    }

    private static string StageBundledDirectory(string pluginDirectory, TempWorkspace temp)
    {
        var staged = temp.Combine("bundled");
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
        var testDirectory = Path.GetDirectoryName(typeof(Rp1PluginIntegrationTests).Assembly.Location)!;
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
