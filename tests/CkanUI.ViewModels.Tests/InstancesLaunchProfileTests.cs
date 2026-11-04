using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 启动游戏前的「启动配置档」闸门：整合包切换必须在放注册表锁与拉进程之前完成；
/// 切换失败不许把游戏拉起来——拉起来的会是上一个整合包的世界。
/// </summary>
[TestFixture]
public sealed class InstancesLaunchProfileTests
{
    private const string MainDir = "D:/Games/KSP-Main";

    private static readonly InstanceInfo Main = new("Main", MainDir, "1.12.5", true, false, 10);

    [Test]
    public async Task 启动前切到启动配置档_发生在拉进程之前()
    {
        var h = new Harness();

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.Profiles.ApplyCalls, Is.EqualTo(new[] { "Main" }), "必须按实例名申请切换");
        Assert.That(h.Order, Is.EqualTo(new[] { "profile", "launch" }), "切换要在拉起游戏之前");
        Assert.That(h.LaunchCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task 切换启动配置档失败_不启动游戏()
    {
        var h = new Harness();
        h.Profiles.ApplyResult = OperationResult<bool>.Fail("游戏正在运行，不能切换配置档。", "KSP.exe 在跑。", retryable: true);

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.LaunchCalls, Is.Zero, "切换没成功就不能启动");
        Assert.That(h.Vm.ErrorText, Does.Contain("不能切换配置档"));
        Assert.That(h.Vm.ErrorText, Does.Contain("切换启动配置档失败"), "要说明是哪一步失败");
        Assert.That(h.Vm.ErrorDetail, Is.EqualTo("KSP.exe 在跑。"));
        Assert.That(h.Vm.LaunchPhase, Is.EqualTo(GameLaunchPhase.Idle), "失败要回到就绪，不能停在准备中");
    }

    [Test]
    public async Task 没接配置档服务_照旧启动()
    {
        var h = new Harness();
        h.Vm.Profiles = null;

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.LaunchCalls, Is.EqualTo(1));
        Assert.That(h.Order, Is.EqualTo(new[] { "launch" }));
    }

    private sealed class Harness
    {
        public Harness()
        {
            Service.Seed(new[] { Main });
            Service.Current = Main;

            Vm = new InstancesPageViewModel(Service, Dialogs, Errors)
            {
                PathProbe = new InstancesFakePathProbe(),
                ShellLauncher = Launcher,
                Profiles = Profiles,
            };

            Profiles.Order = Order;
            Launcher.Order = Order;
            Vm.Reload();
            Vm.Activate();
        }

        public InstancesFakeService Service { get; } = new();

        public InstancesFakeDialogs Dialogs { get; } = new();

        public InstancesFakeErrorReporter Errors { get; } = new();

        public RecordingProfileService Profiles { get; } = new();

        public RecordingLauncher Launcher { get; } = new();

        public List<string> Order { get; } = new();

        public int LaunchCalls => Launcher.LaunchCalls;

        public InstancesPageViewModel Vm { get; }
    }

    private sealed class RecordingLauncher : IInstanceShellLauncher
    {
        public List<string> Order { get; set; } = new();

        public int LaunchCalls { get; private set; }

        public Task<OperationResult> OpenFolderAsync(string path) => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> LaunchGameAsync(string gameDir, string command)
        {
            LaunchCalls++;
            Order.Add("launch");
            return Task.FromResult(OperationResult.Ok());
        }
    }

    private sealed class RecordingProfileService : IInstanceProfileService
    {
        public List<string> Order { get; set; } = new();

        public List<string> ApplyCalls { get; } = new();

        public OperationResult<bool> ApplyResult { get; set; } = OperationResult<bool>.Ok(true);

        public Task<OperationResult<bool>> ApplyLaunchProfileAsync(string instanceName, CancellationToken ct = default)
        {
            ApplyCalls.Add(instanceName);
            Order.Add("profile");
            return Task.FromResult(ApplyResult);
        }

        public Task<OperationResult<IReadOnlyList<InstanceProfileInfo>>> ListAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<InstanceProfileInfo>>.Ok(Array.Empty<InstanceProfileInfo>()));

        public Task<OperationResult<InstanceProfileInfo>> CreateAsync(string instanceName, string profileName, bool adoptCurrent = false, string? source = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceProfileInfo>.Fail("测试替身未实现。"));

        public Task<OperationResult<InstanceProfileSwitchResult>> SwitchAsync(string instanceName, string profileName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceProfileSwitchResult>.Fail("测试替身未实现。"));

        public Task<OperationResult<bool>> DeleteAsync(string instanceName, string profileName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Fail("测试替身未实现。"));

        public Task<OperationResult<string?>> GetLaunchProfileAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<string?>.Ok(null));

        public Task<OperationResult<bool>> SetLaunchProfileAsync(string instanceName, string? profileName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Ok(true));
    }
}
