using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 实例页「启动游戏后释放资源」的钩子测试（18 篇 §4/§7）：启动成功才排程、档位决定时间表、
/// 游戏退出或离开页面要取消并恢复、关闭档连计时器都不起。全部用替身，不启动任何真实进程。
/// </summary>
[TestFixture]
public sealed class InstancesLaunchReleaseTests
{
    private const string MainDir = "D:/Games/KSP-Main";

    private static readonly InstanceInfo Main = new("Main", MainDir, "1.12.5", true, false, 10);

    [Test]
    public async Task 启动成功后_按均衡档排程两段()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Release.Calls, Is.EqualTo(new[] { ResourceReleaseTier.Conservative, ResourceReleaseTier.Balanced }));
        // 第一段是 T3「立即」，不经过计时器；只有 T4 的 +10s 会真的等。
        Assert.That(h.Scheduler.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(10) }));
        Assert.That(h.Release.RestoreCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task 激进档_按T3_T4_T5排三段()
    {
        var h = new Harness(ResourceReleaseTier.Aggressive);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Release.Calls, Is.EqualTo(new[]
        {
            ResourceReleaseTier.Conservative,
            ResourceReleaseTier.Balanced,
            ResourceReleaseTier.Aggressive,
        }));
        Assert.That(h.Scheduler.Delays, Is.EqualTo(new[]
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
        }));
    }

    [Test]
    public async Task 保守档_只排一段()
    {
        var h = new Harness(ResourceReleaseTier.Conservative);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Release.Calls, Is.EqualTo(new[] { ResourceReleaseTier.Conservative }));
    }

    [Test]
    public async Task 关闭档不排程()
    {
        var h = new Harness(ResourceReleaseTier.Off);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Release.Calls, Is.Empty);
        Assert.That(h.Scheduler.Delays, Is.Empty);
        Assert.That(h.Vm.StatusText, Does.Contain("已启动"));
    }

    [Test]
    public async Task 启动失败不排程()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);
        h.Shell.LaunchResult = OperationResult.Fail("找不到可执行文件。");

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);

        Assert.That(h.Release.Calls, Is.Empty);
        Assert.That(h.Vm.ErrorText, Does.Contain("找不到可执行文件"), "启动失败要把服务给的失败原因带到错误栏");
    }

    [Test]
    public async Task 游戏退出_取消后续释放并恢复数据()
    {
        var h = new Harness(ResourceReleaseTier.Aggressive, blockOnDelay: true);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        Assert.That(h.Release.Calls, Is.EqualTo(new[] { ResourceReleaseTier.Conservative }), "第一段立即执行，其余在等计时");

        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Release.Calls, Does.Not.Contain(ResourceReleaseTier.Aggressive), "退出后不再继续后续层");
        Assert.That(h.Release.RestoreCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task 离开页面_也会取消并恢复()
    {
        var h = new Harness(ResourceReleaseTier.Aggressive, blockOnDelay: true);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        h.Vm.Deactivate();
        await AwaitShutdown(h);

        Assert.That(h.Release.RestoreCalls, Is.EqualTo(1));
        Assert.That(h.Release.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task 恢复失败_走统一错误出口且可重试()
    {
        var h = new Harness(ResourceReleaseTier.Conservative);
        h.Release.RestoreResult = OperationResult.Fail("恢复注册表失败，可以重试。", "锁拿不到。", retryable: true);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        await AwaitPending(h);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Release.RestoreCalls, Is.EqualTo(1));
        Assert.That(h.Vm.ErrorText, Does.Contain("恢复"));
        Assert.That(h.Vm.ErrorDetail, Is.EqualTo("锁拿不到。"));
        Assert.That(h.Vm.HasError, Is.True, "恢复失败必须出现在错误栏，而不是只写日志");
    }

    // ---- E6 启动状态机：启动前还锁、精简模式、恢复出口（18 篇 §4/§6） ----

    [Test]
    public async Task 均衡档_启动前先还注册表锁并进入精简模式()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);
        h.Release.CoreDataResult = OperationResult.Ok();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.Release.CoreDataCalls, Is.EqualTo(1), "启动前必须只跑一次 L2");
        Assert.That(h.Vm.IsTrimMode, Is.True, "放锁成功即进入精简模式");
        Assert.That(h.Vm.LaunchPhase, Is.EqualTo(GameLaunchPhase.Running));
        Assert.That(h.Vm.TrimModeText, Is.EqualTo("精简模式"));
    }

    [Test]
    public async Task 关闭档_连锁都不动()
    {
        var h = new Harness(ResourceReleaseTier.Off);
        h.Release.CoreDataResult = OperationResult.Ok();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.Release.CoreDataCalls, Is.Zero, "用户关掉释放后连 L2 都不该跑");
        Assert.That(h.Vm.IsTrimMode, Is.False);
    }

    [Test]
    public async Task 放锁失败_不阻断启动()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);
        h.Release.CoreDataResult = OperationResult.Fail("注册表正被占用。");

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.Shell.LaunchCalls, Is.EqualTo(1), "拿不到锁也要把游戏拉起来");
        Assert.That(h.Vm.IsTrimMode, Is.False);
        Assert.That(h.Vm.StatusText, Does.Contain("已启动"));
    }

    [Test]
    public async Task 启动失败_把刚放掉的锁立刻收回来()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);
        h.Release.CoreDataResult = OperationResult.Ok();
        h.Shell.LaunchResult = OperationResult.Fail("找不到可执行文件。");

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.Release.CoreDataCalls, Is.EqualTo(1));
        Assert.That(h.Release.RestoreCalls, Is.EqualTo(1), "进程没起来必须立刻恢复，不能停在精简模式");
        Assert.That(h.Vm.IsTrimMode, Is.False);
        Assert.That(h.Vm.LaunchPhase, Is.EqualTo(GameLaunchPhase.Idle));
    }

    [Test]
    public async Task 游戏退出_退出精简模式()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);
        h.Release.CoreDataResult = OperationResult.Ok();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Vm.IsTrimMode, Is.False);
        Assert.That(h.Vm.LaunchPhase, Is.EqualTo(GameLaunchPhase.Idle));
        Assert.That(h.Vm.CanRestoreTrimMode, Is.False);
    }

    [Test]
    public async Task 精简模式下_可以手动恢复数据()
    {
        var h = new Harness(ResourceReleaseTier.Aggressive, blockOnDelay: true);
        h.Release.CoreDataResult = OperationResult.Ok();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        Assert.That(h.Vm.CanRestoreTrimMode, Is.True);

        h.Vm.RestoreTrimModeCommand.Execute(null);
        await AwaitShutdown(h);

        Assert.That(h.Release.RestoreCalls, Is.EqualTo(1));
        Assert.That(h.Vm.IsTrimMode, Is.False);
        Assert.That(h.Vm.LaunchPhase, Is.EqualTo(GameLaunchPhase.Idle));
    }

    [Test]
    public async Task 恢复失败_留住精简模式并可重试()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);
        h.Release.CoreDataResult = OperationResult.Ok();
        h.Release.RestoreResult = OperationResult.Fail("恢复注册表失败，可以重试。", "锁拿不到。", retryable: true);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);
        h.Shell.RaiseExited(MainDir);
        await AwaitShutdown(h);

        Assert.That(h.Vm.IsTrimMode, Is.True, "没恢复成功就不能假装回到常态");
        Assert.That(h.Vm.CanRestoreTrimMode, Is.True, "失败后要留一个重试出口");
        Assert.That(h.Vm.ErrorText, Does.Contain("恢复"));

        h.Release.RestoreResult = OperationResult.Ok();
        h.Vm.RestoreTrimModeCommand.Execute(null);
        await AwaitShutdown(h);

        Assert.That(h.Vm.IsTrimMode, Is.False, "重试成功才退出精简模式");
        Assert.That(h.Release.RestoreCalls, Is.EqualTo(2));
    }

    [Test]
    public void 启动阶段文案_随状态机变化()
    {
        var h = new Harness(ResourceReleaseTier.Balanced);

        Assert.That(h.Vm.LaunchPhase, Is.EqualTo(GameLaunchPhase.Idle));
        Assert.That(h.Vm.LaunchPhaseText, Is.EqualTo("就绪"));
        Assert.That(h.Vm.IsLaunchActive, Is.False);
        Assert.That(h.Vm.TrimModeText, Is.Empty);
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
        public Harness(ResourceReleaseTier tier, bool blockOnDelay = false)
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
                ResourceRelease = Release,
                ReleaseScheduler = Scheduler,
                UiSettings = Settings,
            };

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

        public InstancesPageViewModel Vm { get; }
    }

    private sealed class FakeReleaseService : IResourceReleaseService
    {
        public List<ResourceReleaseTier> Calls { get; } = new();

        public int RestoreCalls { get; private set; }

        /// <summary>启动前「只跑 L2」的调用次数（18 篇 §4 T0）。</summary>
        public int CoreDataCalls { get; private set; }

        public OperationResult CoreDataResult { get; set; } = OperationResult.Ok();

        public Task<OperationResult> ReleaseCoreDataAsync(CancellationToken ct = default)
        {
            CoreDataCalls++;
            return Task.FromResult(CoreDataResult);
        }

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
