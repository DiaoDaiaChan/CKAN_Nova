using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Instances;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 顶栏常驻「启动游戏」按钮：全页面可用，所以它自己必须守住三件事——
/// 没有实例不许亮、实例被占用不许亮、启动结果必须落到用户看得见的地方（底部状态栏）。
/// </summary>
[TestFixture]
public sealed class ShellLaunchGameTests
{
    private static readonly InstanceInfo Unlocked =
        new("主实例", @"C:\Games\KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 3);

    private static readonly InstanceInfo Locked =
        new("主实例", @"C:\Games\KSP", "1.12.5", IsDefault: true, IsLocked: true, ModuleCount: 3);

    [Test]
    public void LaunchGameCommand_IsDisabledWithoutCurrentInstance()
    {
        var shell = CreateShell(instance: null);

        Assert.That(shell.CanLaunchGame, Is.False);
        Assert.That(shell.LaunchGameCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void LaunchGameCommand_IsDisabledWhileTheInstanceIsLocked()
    {
        var shell = CreateShell(instance: Locked);

        Assert.That(shell.CanLaunchGame, Is.False, "实例被占用时顶栏按钮不能亮，否则点下去只换来一次失败");
        Assert.That(shell.LaunchGameCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void LaunchGameCommand_TurnsOnWhenAnUnlockedInstanceBecomesCurrent()
    {
        var instances = new FakeInstanceService();
        var shell = CreateShell(instance: null, instances: instances);
        Assert.That(shell.LaunchGameCommand.CanExecute(null), Is.False);

        instances.RaiseCurrentChanged(Unlocked);

        Assert.That(shell.CanLaunchGame, Is.True);
        Assert.That(shell.LaunchGameCommand.CanExecute(null), Is.True, "切换实例后必须重新查一遍可执行条件");
    }

    [Test]
    public async Task LaunchGameCommand_UsesTheSavedCommandAndReportsToTheStatusBar()
    {
        var shell = CreateShell(instance: Unlocked);
        var store = new RecordingCommandStore { Saved = new[] { "KSP_x64.exe -single-instance" } };
        var launcher = new RecordingShellLauncher();
        shell.CommandStore = store;
        shell.ShellLauncher = launcher;

        await shell.LaunchGameCommand.ExecuteAsync(null);

        Assert.That(launcher.LastGameDir, Is.EqualTo(Unlocked.GameDir));
        Assert.That(launcher.LastCommand, Is.EqualTo("KSP_x64.exe -single-instance"));
        Assert.That(shell.StatusText, Is.EqualTo("已启动：KSP_x64.exe -single-instance"));
    }

    [Test]
    public async Task LaunchGameCommand_FallsBackToTheMachineDefaultWhenNothingIsSaved()
    {
        var shell = CreateShell(instance: Unlocked);
        var store = new RecordingCommandStore { Saved = null, Defaults = new[] { "steam://rungameid/220200" } };
        var launcher = new RecordingShellLauncher();
        shell.CommandStore = store;
        shell.ShellLauncher = launcher;

        await shell.LaunchGameCommand.ExecuteAsync(null);

        Assert.That(launcher.LastCommand, Is.EqualTo("steam://rungameid/220200"));
    }

    [Test]
    public async Task LaunchGameCommand_SaysSoWhenThereIsNoUsableCommand()
    {
        var shell = CreateShell(instance: Unlocked);
        shell.CommandStore = new RecordingCommandStore { Saved = null, Defaults = Array.Empty<string>() };
        shell.ShellLauncher = new RecordingShellLauncher();

        await shell.LaunchGameCommand.ExecuteAsync(null);

        Assert.That(shell.StatusText, Is.EqualTo("Ui_Home_ErrorNoLaunchCommand"));
    }

    [Test]
    public async Task LaunchGameCommand_SurfacesTheLauncherFailureMessage()
    {
        var shell = CreateShell(instance: Unlocked);
        shell.CommandStore = new RecordingCommandStore { Saved = new[] { "missing.exe" } };
        shell.ShellLauncher = new RecordingShellLauncher
        {
            Result = OperationResult.Fail("启动命令指向的可执行文件不存在")
        };

        await shell.LaunchGameCommand.ExecuteAsync(null);

        Assert.That(shell.StatusText, Is.EqualTo("启动命令指向的可执行文件不存在"));
    }

    private static ShellViewModel CreateShell(InstanceInfo? instance, FakeInstanceService? instances = null)
    {
        instances ??= new FakeInstanceService();
        instances.Current = instance;

        // 译文注入成真实格式模板：启动成功文案带 {0}，否则断言不出命令是否真的写进了状态栏。
        return new ShellViewModel(
            new NavigationService(_ => null),
            key => key == "Ui_Home_StatusLaunched" ? "已启动：{0}" : key,
            instances: instances);
    }

    /// <summary>记录调用参数的假外壳：不真的起进程。</summary>
    private sealed class RecordingShellLauncher : IInstanceShellLauncher
    {
        public OperationResult Result { get; set; } = OperationResult.Ok();

        public string? LastGameDir { get; private set; }

        public string? LastCommand { get; private set; }

        public Task<OperationResult> OpenFolderAsync(string path) => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> LaunchGameAsync(string gameDir, string command)
        {
            LastGameDir = gameDir;
            LastCommand = command;
            return Task.FromResult(Result);
        }
    }

    /// <summary>假启动命令存储：区分「保存过」与「回落到默认值」两条路径。</summary>
    private sealed class RecordingCommandStore : IInstanceCommandStore
    {
        public IReadOnlyList<string>? Saved { get; set; }

        public IReadOnlyList<string> Defaults { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> DefaultsFor(string gameDirectory) => Defaults;

        public Task<OperationResult<IReadOnlyList<string>?>> ReadAsync(string gameDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<string>?>.Ok(Saved));

        public Task<OperationResult> WriteAsync(string gameDirectory, IReadOnlyList<string> commands, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }
}
