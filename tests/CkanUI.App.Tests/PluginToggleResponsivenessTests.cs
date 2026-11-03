using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.App.Tests.Automation;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Plugins;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Plugins;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 「启停插件时界面会卡住」这条缺陷的回归：真插件 dll + 真 PluginService + 真页面命令，
/// 断言一次启停只让侧栏集合动一次，并且在有限时间内收口。
/// </summary>
/// <remarks>
/// <para>卡住的成因不是死锁，而是 UI 线程上的活太多：<c>PluginService</c> 的
/// Activate/Deactivate 都是 <c>Task.FromResult(XxxCore(...))</c> 的同步实现（Initialize、
/// Deinitialize、写激活表、释放页面全在同一条调用栈里），而外壳按 <c>RailItems</c> 逐项
/// 销毁重建整套侧栏菜单，一次启停就可能触发十几次全量重建。</para>
/// <para>这条用例故意<b>不碰任何 Avalonia 控件</b>：无头宿主把 <c>Dispatcher.UIThread</c> 绑在
/// 最先初始化它的那根线程上，而 32 worker 并发跑全量时，本用例未必落在同一根线程 ——
/// 一读 <c>ContentControl.Content</c> 就是跨线程异常（真实踩过）。界面重建本身由
/// <c>PluginNavRailTests</c> 在同步路径上覆盖，这里只钉「命令的代价」。</para>
/// <para>同步上下文换成本用例自己的手动泵：<c>ShellViewModel</c> 用它把变更通知排回
/// 「界面线程」，我们逐条泵完 —— 于是无论跑在哪个线程上都可复现。</para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class PluginToggleResponsivenessTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void 启停插件在无头宿主里能跑完且侧栏只做最小重建()
    {
        var pluginDirectory = ResolvePluginOutputDirectory();
        if (!System.IO.File.Exists(System.IO.Path.Combine(pluginDirectory, "CkanUI.Plugin.Performance.dll")))
        {
            Assert.Ignore("还没构建 CkanUI.Plugin.Performance，跳过启停响应性验证。");
        }

        var bundledDirectory = StageBundledDirectory(pluginDirectory);
        var userDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ckanui-user-" + Guid.NewGuid().ToString("N"));
        var storePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ckanui-activation-" + Guid.NewGuid().ToString("N") + ".json");
        var pump = new ManualPumpContext();
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(pump);

            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            var shell = new ShellViewModel(new SilentNavigation(), plugins: service);
            var railEvents = 0;
            shell.RailItems.CollectionChanged += (_, _) => railEvents++;

            var vm = new PluginsPageViewModel(service, new QuietErrorReporter());

            // 首次扫描：把内置目录里的插件列进右栏（未激活）。
            _ = vm.RefreshCommand.ExecuteAsync(null);
            PumpUntil(pump, () => !vm.IsBusy, "首次扫描插件目录");
            Assert.That(vm.DormantPlugins, Has.Count.EqualTo(1), "内置性能插件应当出现在未激活栏");
            Assert.That(shell.NavItems.Any(item => item.Key == "performance"), Is.False, "未激活时不该有性能入口");

            railEvents = 0;

            // 激活：真插件要 Initialize + 写激活表 + 广播；这里必须能在有限时间内结束。
            vm.SelectedDormant = vm.DormantPlugins[0];
            var activate = Stopwatch.StartNew();
            _ = vm.ActivateCommand.ExecuteAsync(null);
            PumpUntil(pump, () => !vm.IsBusy && vm.ActivePlugins.Count == 1, "激活内置插件");
            activate.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(service.Active, Has.Count.EqualTo(1), "激活要真的落到服务层");
                Assert.That(activate.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
                    "激活一次插件用了 " + activate.ElapsedMilliseconds + " ms —— 调用线程被独占就是用户看到的卡住");
                Assert.That(shell.NavItems.Any(item => item.Key == "performance"), Is.True, "激活后页面入口要进 NavItems");
                Assert.That(railEvents, Is.EqualTo(1),
                    "只有插件自述页该新增一个侧栏项；分节入口项必须被复用，否则一次激活就是十几次全量重建。实际 " + railEvents + " 次");
            });

            railEvents = 0;

            // 停用：Deinitialize + 释放页面 + 广播，同样要收得住。
            vm.SelectedActive = vm.ActivePlugins[0];
            var deactivate = Stopwatch.StartNew();
            _ = vm.DeactivateCommand.ExecuteAsync(null);
            PumpUntil(pump, () => !vm.IsBusy && vm.ActivePlugins.Count == 0, "停用内置插件");
            deactivate.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(service.Active, Is.Empty, "停用要真的落到服务层");
                Assert.That(deactivate.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
                    "停用一次插件用了 " + deactivate.ElapsedMilliseconds + " ms");
                Assert.That(shell.NavItems.Any(item => item.Key == "performance"), Is.False, "停用后页面入口也要从 NavItems 摘掉");
                Assert.That(railEvents, Is.EqualTo(1), "停用只该删掉那一个侧栏项；实际 " + railEvents + " 次");
            });

            Task.Run(() => service.UnloadAsync(service.Dormant.FirstOrDefault()?.Id ?? "com.ckanui.performance"))
                .GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    /// <summary>排自己的同步上下文队列，直到条件成立；超时就判失败，免得挂死把整轮回归拖住。</summary>
    private static void PumpUntil(ManualPumpContext pump, Func<bool> condition, string what)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(30))
            {
                Assert.Fail("等待「" + what + "」超过 30 秒：命令没有完成，界面线程的队列也没被排空。");
            }

            pump.RunPending();
            Thread.Sleep(2);
        }

        pump.RunPending();
    }

    /// <summary>手动泵的同步上下文，扮演「界面线程」：Post 只入队，由本用例逐条泵完。</summary>
    private sealed class ManualPumpContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                _queue.Enqueue((d, state));
            }
        }

        public void RunPending()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }

                    item = _queue.Dequeue();
                }

                item.Callback(item.State);
            }
        }
    }
    private static string StageBundledDirectory(string pluginDirectory)
    {
        var staged = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ckanui-bundled-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(staged);
        foreach (var extension in new[] { ".dll", ".pdb" })
        {
            var source = System.IO.Path.Combine(pluginDirectory, "CkanUI.Plugin.Performance" + extension);
            if (System.IO.File.Exists(source))
            {
                System.IO.File.Copy(source, System.IO.Path.Combine(staged, System.IO.Path.GetFileName(source)));
            }
        }

        return staged;
    }

    private static string ResolvePluginOutputDirectory()
    {
        var testDirectory = System.IO.Path.GetDirectoryName(typeof(PluginToggleResponsivenessTests).Assembly.Location)!;
        var configuration = System.IO.Directory.GetParent(testDirectory)?.Name ?? "Debug";
        var repoRoot = FindRepoRoot(testDirectory);
        return System.IO.Path.Combine(repoRoot, "src", "CkanUI.Plugin.Performance", "bin", configuration, "net10.0");
    }

    private static string FindRepoRoot(string start)
    {
        var current = start;
        while (current is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current, "ckan-ui.sln")))
            {
                return current;
            }

            current = System.IO.Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("找不到 ckan-ui.sln 所在目录：" + start);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, recursive: true);
            }
            else if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (System.IO.IOException)
        {
        }
    }

    /// <summary>插件页只要一个不吞异常的上报口；这条用例期望没有任何错误落进来。</summary>
    private sealed class QuietErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported;

        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();

        public void Report(Exception exception, string context, string? userMessage = null) =>
            Reported?.Invoke(new ErrorEntry(DateTime.UtcNow, context, userMessage ?? exception.Message, exception.ToString(), exception));

        public void Report(string context, string userMessage, string? detail = null) =>
            Reported?.Invoke(new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null));
    }
}