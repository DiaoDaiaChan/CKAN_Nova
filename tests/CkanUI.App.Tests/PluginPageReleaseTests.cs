using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CkanUI.App.Plugins;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Plugins;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 关掉插件之后，页面实例与插件程序集是不是真的被回收了。
/// </summary>
/// <remarks>
/// <para>
/// 这条用例守的是「停用/卸载不是改个状态位」：页面视图模型带着一个 1 Hz 的轮询定时器，
/// 只要外壳还钉着它，可回收的 <c>AssemblyLoadContext</c> 就永远收不掉，插件 dll 也一直映射着。
/// 所以断言分三层：停用后页面实例被 Dispose（<see cref="PluginPageCache.RetainOnly"/> 的口径）、
/// 卸载后视图模型与程序集都被 GC 回收（<see cref="WeakReference.IsAlive"/> 变 false）、
/// dll 文件的句柄已释放（能直接删掉暂存目录）。
/// </para>
/// <para>
/// 用例不建视图、不 Show 窗口：视图层是 <c>MainWindow.PruneRemovedPageViews</c> 的职责，
/// 这里只验证「服务 + 页面缓存」这一层真的把引用放掉了。GC 有不确定性，所以循环若干轮。
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class PluginPageReleaseTests
{
    private const string PluginAssemblyName = "CkanUI.Plugin.Performance";

    [Test]
    public void 卸载插件后页面视图模型与程序集都能被回收()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-user-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-" + Guid.NewGuid().ToString("N") + ".json");

        WeakReference pageReference;
        WeakReference assemblyReference;

        try
        {
            (pageReference, assemblyReference) = CreatePageThenUnload(bundledDirectory, userDirectory, storePath);

            // 全量并发跑（32 个 worker）时 GC 会拖后腿，20×25ms 不够：给到最多 3 秒。
            for (var attempt = 0; attempt < 60 && (pageReference.IsAlive || assemblyReference.IsAlive); attempt++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }

            Assert.Multiple(() =>
            {
                Assert.That(pageReference.IsAlive, Is.False, "插件卸载后页面视图模型还活着：说明还有人在钉它（定时器、旧视图或缓存）");
                Assert.That(assemblyReference.IsAlive, Is.False, "插件程序集还活着：可回收的 AssemblyLoadContext 没被卸掉");
            });

            // dll 句柄没放开的话这里会抛 IOException。
            Directory.Delete(bundledDirectory, recursive: true);
            Assert.That(Directory.Exists(bundledDirectory), Is.False);
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    [Test]
    public void 停用插件就把页面实例从缓存里释放掉()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-user-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            Assert.Multiple(() => AssertPageReleasedOnDeactivate(bundledDirectory, userDirectory, storePath));
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    /// <summary>
    /// 造页面、停用、卸载，全部局限在这个方法的栈帧里；只把 WeakReference 带出去。
    /// </summary>
    /// <remarks>
    /// NoInlining 是必须的：这些局部变量（页面实例、<see cref="Type"/>、服务）一旦被内联进调用方，
    /// JIT 可能让它们的生命周期跨过卸载点，弱引用于是永远不失效，用例会变成假红。
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Page, WeakReference Assembly) CreatePageThenUnload(
        string bundledDirectory,
        string userDirectory,
        string storePath)
    {
        var service = CreateService(bundledDirectory, userDirectory, storePath);
        var (plugin, assembly) = DiscoverSingle(service);
        var id = plugin.Id;
        var (cache, page, type) = CreatePage(service, assembly);

        var pageReference = new WeakReference(page);
        var assemblyReference = new WeakReference(type.Assembly);

        // 卸载标志是 PluginPageFactory 跳过旧一份程序集的依据；没有它，重载后页面会解析到旧类型。
        var context = AssemblyLoadContext.GetLoadContext(type.Assembly) as IPluginAssemblyContext;
        Assert.That(context, Is.Not.Null, "插件加载上下文应实现 IPluginAssemblyContext");
        Assert.That(context!.IsUnloading, Is.False, "加载中的上下文不该被标记成卸载中");

        var deactivated = service.DeactivateAsync(id).GetAwaiter().GetResult();
        Assert.That(deactivated.IsSuccess, Is.True, "停用插件失败：" + deactivated.Message);

        // 外壳订阅的是服务层的 Changed；这里按宿主的做法，把活着的页签目录喂给缓存。
        var live = PluginPageCatalog.Collect(service.Active, Array.Empty<string>()).Select(entry => entry.Key).ToList();
        cache.RetainOnly(live);
        Assert.That(cache.Count, Is.Zero, "插件停用后页面实例还留在缓存里");

        var unloaded = service.UnloadAsync(id).GetAwaiter().GetResult();
        Assert.That(unloaded.IsSuccess, Is.True, "卸载插件失败：" + unloaded.Message);
        Assert.That(context.IsUnloading, Is.True, "UnloadAsync 之后加载上下文必须标记为卸载中（否则重载后类型解析会挑到旧一份）");

        return (pageReference, assemblyReference);
    }

    private static void AssertPageReleasedOnDeactivate(string bundledDirectory, string userDirectory, string storePath)
    {
        var service = CreateService(bundledDirectory, userDirectory, storePath);
        var (plugin, assembly) = DiscoverSingle(service);
        var id = plugin.Id;
        var (cache, page, _) = CreatePage(service, assembly);
        var disposedField = page.GetType().GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(disposedField, Is.Not.Null, "页面视图模型里应有一个 _disposed 标志（Dispose 的幂等开关）");

        var deactivated = service.DeactivateAsync(id).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(deactivated.IsSuccess, Is.True, "停用插件失败：" + deactivated.Message);
            Assert.That(service.Active.Any(plugin => plugin.Id == id), Is.False, "停用后插件还在活跃列表里");
        });

        var live = PluginPageCatalog.Collect(service.Active, Array.Empty<string>()).Select(entry => entry.Key).ToList();
        cache.RetainOnly(live);

        Assert.Multiple(() =>
        {
            Assert.That(cache.Count, Is.Zero);
            Assert.That(disposedField!.GetValue(page), Is.EqualTo(true), "页面实例必须被 Dispose：轮询定时器与事件订阅都在里面");
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PluginService CreateService(string bundledDirectory, string userDirectory, string storePath) =>
        new(
            userDirectory,
            new AssemblyLoadContextPluginLoader(),
            hostVersion: null,
            bundledDirectory: bundledDirectory,
            activationStore: new PluginActivationStore(storePath));

    /// <summary>扫描并激活唯一的插件；顺带用「扫描前后程序集差集」认领本用例这一份程序集。</summary>
    /// <remarks>
    /// 不能按 <c>Assembly.Location</c> 认：加载器用 <c>LoadFromStream</c> 装 dll，这种程序集的 Location 是空串。
    /// 也不能按名字在 AppDomain 里随便取第一个：同进程里别的用例可能还留着同名插件的另一份 ALC。
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CkanUI.Services.Abstractions.Dtos.PluginInfo Plugin, Assembly Assembly) DiscoverSingle(PluginService service)
    {
        var known = new HashSet<Assembly>(AppDomain.CurrentDomain.GetAssemblies());

        var discovered = service.DiscoverAsync(activateBundled: true).GetAwaiter().GetResult();
        Assert.That(discovered.IsSuccess, Is.True, "扫描内置插件失败：" + discovered.Message);
        Assert.That(service.Failures, Is.Empty, "扫描时不该有失败记录");

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !known.Contains(assembly)
                && string.Equals(assembly.GetName().Name, PluginAssemblyName, StringComparison.Ordinal))
            .ToList();

        Assert.That(loaded.Count, Is.EqualTo(1), "扫描应当新加载恰好一份插件程序集（实际 " + loaded.Count + " 份）");

        return (service.Active.Single(), loaded[0]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (PluginPageCache Cache, object Page, Type Type) CreatePage(PluginService service, Assembly assembly)
    {
        var descriptor = service.Active.Single().Pages.Single(page => page.Key == "performance");

        // 刻意不用 PluginPageFactory.ResolveViewModelType：它按全名在 AppDomain 里逐个程序集找，
        // 同进程里只要有另一个用例还加载着同名插件（不同 ALC），就可能拿到别人那份类型——
        // 那样本用例卸载的是自己的 ALC，弱引用却盯着别人的程序集，结果取决于用例执行顺序。
        Assert.That(assembly.IsCollectible, Is.True, "插件程序集应该是可回收的");

        var type = assembly.GetType(descriptor.ViewModelTypeName, throwOnError: false);
        Assert.That(type, Is.Not.Null, "解析不到性能页的视图模型类型");

        var cache = new PluginPageCache();

        // 不走 PluginPageFactory.Create：它要 IServiceProvider 里注册的 IInstanceService，
        // 而页面视图模型有无参构造（没有实例服务时退化成空态），这里直接用最轻的形态。
        var page = cache.GetOrCreate("performance", () => Activator.CreateInstance(type!));

        Assert.That(page, Is.Not.Null, "页面实例没造出来");

        return (cache, page!, type!);
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
        var testDirectory = Path.GetDirectoryName(typeof(PluginPageReleaseTests).Assembly.Location)!;
        var configuration = Directory.GetParent(testDirectory)?.Name ?? "Debug";
        var repoRoot = FindRepoRoot(testDirectory);

        var pluginDirectory = Path.Combine(repoRoot, "src", PluginAssemblyName, "bin", configuration, "net10.0");

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
}
