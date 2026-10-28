using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 实例页「启动游戏后自动累计时长」的接线测试（F-48）：启动成功才开会话、游戏退出才结算，
/// 并且**不受资源释放配置影响**——关闭释放档或宿主没注入释放服务时，退出信号仍然要订阅，
/// 否则计时永远结算不了。全部用替身，不启动任何真实进程。
/// </summary>
[TestFixture]
public sealed class InstancesPlayTimeTrackingTests
{
    private const string MainDir = "D:/Games/KSP-Main";

    private static readonly InstanceInfo Main = new("Main", MainDir, "1.12.5", true, false, 10);

    [Test]
    public async Task 启动成功_开始计时()
    {
        var h = new Harness();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Tracker.Begins, Is.EqualTo(new[] { "Main" }), "启动成功后要按实例名开会话");
        Assert.That(h.Vm.StatusText, Does.Contain("已启动"));
    }

    [Test]
    public async Task 启动失败_不开始计时()
    {
        var h = new Harness();
        h.Shell.LaunchResult = OperationResult.Fail("找不到可执行文件。");

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Tracker.Begins, Is.Empty, "游戏没起来就不该有会话，否则会凭空累计");
        Assert.That(h.Vm.ErrorText, Does.Contain("找不到可执行文件"));
    }

    [Test]
    public async Task 游戏退出_结算时长()
    {
        var h = new Harness();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Tracker.Ends, Is.EqualTo(1));
    }

    [Test]
    public async Task 没有释放服务_退出信号仍订阅()
    {
        var h = new Harness(withReleaseService: false);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Tracker.Ends, Is.EqualTo(1), "计时只依赖退出信号，与是否做资源释放无关");
    }

    [Test]
    public async Task 关闭释放档_仍结算时长()
    {
        var h = new Harness(ResourceReleaseTier.Off);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Release.Calls, Is.Empty, "关闭档不排程");

        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Tracker.Ends, Is.EqualTo(1));
    }

    [Test]
    public async Task 未注入计时器_启动照常()
    {
        var h = new Harness(withTracker: false);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Vm.StatusText, Does.Contain("已启动"), "没有计时服务时启动流程不应受影响");
        Assert.That(h.Vm.HasError, Is.False);
    }

    [Test]
    public async Task 离开页面后游戏退出_仍结算时长()
    {
        // 真实场景就是「启动之后切到别的页签继续玩」，所以离开页面不能把结算信号一起丢掉。
        var h = new Harness();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);
        h.Vm.Deactivate();
        await AwaitShutdown(h);

        Assert.That(h.Tracker.Ends, Is.Zero, "离开页面时游戏还在跑，不能提前结算");

        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Tracker.Ends, Is.EqualTo(1));
    }

    [Test]
    public async Task 离开页面与游戏退出_只恢复一次资源()
    {
        var h = new Harness(ResourceReleaseTier.Aggressive, blockOnDelay: true);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        h.Vm.Deactivate();
        await AwaitShutdown(h);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Release.RestoreCalls, Is.EqualTo(1), "离开页面已经恢复过，退出时不该再恢复一次");
        Assert.That(h.Tracker.Ends, Is.EqualTo(1));
    }

    [Test]
    public async Task 没有打开会话时_离开页面照旧退订()
    {
        var h = new Harness(withReleaseService: false);

        h.Vm.Deactivate();
        await AwaitShutdown(h);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Tracker.Ends, Is.Zero, "没开会话就没有可结算的东西");
    }

    private static async Task AwaitPending(Harness h)
    {
        var pending = h.Vm.PendingRelease;
        if (pending is not null)
        {
            await pending;
        }
    }

    private static async Task AwaitShutdown(Harness h)
    {
        var shutdown = h.Vm.PendingReleaseShutdown;
        if (shutdown is not null)
        {
            await shutdown;
        }
    }

    private sealed class Harness
    {
        public Harness(
            ResourceReleaseTier tier = ResourceReleaseTier.Balanced,
            bool blockOnDelay = false,
            bool withReleaseService = true,
            bool withTracker = true)
        {
            Settings.Current = new UiSettings { ReleaseTier = tier };
            Scheduler.Blocking = blockOnDelay;

            Service.Seed(new[] { Main });
            Service.Current = Main;

            Vm = new InstancesPageViewModel(Service, Dialogs, Errors)
            {
                PathProbe = Probe,
                FolderPicker = Picker,
                ShellLauncher = Shell,
                SteamLibraries = Steam,
                ReleaseScheduler = Scheduler,
                UiSettings = Settings,
            };

            if (withReleaseService)
            {
                Vm.ResourceRelease = Release;
            }

            if (withTracker)
            {
                Vm.PlayTime = Tracker;
            }

            Vm.Reload();
            Vm.Activate();
        }

        public InstancesFakeService Service { get; } = new();

        public InstancesFakeDialogs Dialogs { get; } = new();

        public InstancesFakeErrorReporter Errors { get; } = new();

        public InstancesFakePathProbe Probe { get; } = new();

        public InstancesFakeShellLauncher Shell { get; } = new();

        public InstancesFakeSteamSource Steam { get; } = new();

        public InstancesFakeFolderPicker Picker { get; } = new();

        public FakeReleaseService Release { get; } = new();

        public FakeReleaseScheduler Scheduler { get; } = new();

        public FakeReleaseSettingsStore Settings { get; } = new();

        public FakePlayTimeTracker Tracker { get; } = new();

        public InstancesPageViewModel Vm { get; }
    }

    /// <summary>计时器替身：只记「谁开了会话、结算了几次」，不碰文件系统。</summary>
    private sealed class FakePlayTimeTracker : IPlayTimeTracker
    {
        public List<string> Begins { get; } = new();

        public int Ends { get; private set; }

        public string? ActiveInstanceName { get; private set; }

        public bool BeginSession(string instanceName)
        {
            Begins.Add(instanceName);
            ActiveInstanceName = instanceName;
            return true;
        }

        public double EndSession()
        {
            Ends++;
            ActiveInstanceName = null;
            return 0d;
        }

        public bool DiscardOpenSession()
        {
            var had = ActiveInstanceName is not null;
            ActiveInstanceName = null;
            return had;
        }
    }

    private sealed class FakeReleaseService : IResourceReleaseService
    {
        public List<ResourceReleaseTier> Calls { get; } = new();

        public int RestoreCalls { get; private set; }

        public OperationResult RestoreResult { get; set; } = OperationResult.Ok();

        public bool IsReleased { get; private set; }

        public event Action<ResourceReleaseReport>? Released
        {
            add { }
            remove { }
        }

        public Task<OperationResult<ResourceReleaseReport>> ReleaseAsync(
            ResourceReleaseTier tier,
            IProgress<OperationProgress>? progress = null,
            CancellationToken ct = default)
        {
            Calls.Add(tier);
            var report = new ResourceReleaseReport(
                tier,
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                Enum.GetValues<ResourceReleaseLayer>()
                    .Select(layer => ResourceReleaseLayerReport.Skipped(layer, "替身未执行。"))
                    .ToArray());
            return Task.FromResult(OperationResult<ResourceReleaseReport>.Ok(report));
        }

        public Task<OperationResult> RestoreAsync(CancellationToken ct = default)
        {
            RestoreCalls++;
            return Task.FromResult(RestoreResult);
        }
    }

    /// <summary>可控延时器：默认立即返回（时间表一口气跑完），<see cref="Blocking"/> 时一直等到取消。</summary>
    private sealed class FakeReleaseScheduler : IInstancesDelayScheduler
    {
        public List<TimeSpan> Delays { get; } = new();

        public bool Blocking { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Blocking && delay > TimeSpan.Zero
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.CompletedTask;
        }
    }

    private sealed class FakeReleaseSettingsStore : IUiSettingsStore
    {
        public UiSettings Current { get; set; } = new();

        public UiSettings Load() => Current;

        public event Action<UiSettings>? Changed
        {
            add { }
            remove { }
        }

        public OperationResult<bool> Save(UiSettings settings)
        {
            Current = settings;
            return OperationResult<bool>.Ok(true);
        }
    }
}
