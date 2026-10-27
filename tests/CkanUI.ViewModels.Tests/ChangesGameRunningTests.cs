using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 12 篇 F-33 / 03 篇 §4：游戏进程在跑时必须先给横幅并拦下主按钮，用户显式「仍要执行」才放行。
/// </summary>
[TestFixture]
public sealed class ChangesGameRunningTests
{
    private ChangesFakeChangeSetService _changeSets = null!;
    private ChangesFakeInstallerService _installer = null!;
    private ChangesCountingErrorReporter _errors = null!;
    private FakeInstanceService _instances = null!;

    [SetUp]
    public void SetUp()
    {
        _changeSets = new ChangesFakeChangeSetService();
        _installer = new ChangesFakeInstallerService();
        _errors = new ChangesCountingErrorReporter();
        _instances = new FakeInstanceService
        {
            Current = new InstanceInfo("主实例", @"C:\Games\KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 3),
        };
    }

    private ChangesPageViewModel Create(ChangeSetPreview? preview = null)
    {
        var effective = preview ?? ChangesFakes.Preview(new[] { ChangesFakes.Item("alpha"), ChangesFakes.Item("beta") });
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(effective);
        var vm = new ChangesPageViewModel(_changeSets, _installer, _errors, null, new ChangesImmediateDispatcher(), null, _instances);
        vm.Activate();
        vm.LoadPreview(effective);
        return vm;
    }

    [Test]
    public void GameRunning_ShowsWarningAndBlocksApply()
    {
        _instances.GameRunning = true;
        var vm = Create();

        Assert.That(vm.HasGameRunningWarning, Is.True);
        Assert.That(vm.IsGameRunning, Is.True);
        Assert.That(vm.CanApply, Is.False);
        Assert.That(vm.ApplyCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void GameNotRunning_NoWarningAndApplyAllowed()
    {
        _instances.GameRunning = false;
        var vm = Create();

        Assert.That(vm.HasGameRunningWarning, Is.False);
        Assert.That(vm.CanApply, Is.True);
        Assert.That(vm.ApplyCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void WarningText_IsReadableChinese()
    {
        _instances.GameRunning = true;
        var vm = Create();

        Assert.That(vm.GameRunningTitle, Is.EqualTo("游戏正在运行"));
        Assert.That(vm.GameRunningHint, Does.Contain("退出游戏"));
    }

    [Test]
    public void Activate_ProbesGameProcessOnce()
    {
        var vm = Create();
        Assert.That(_instances.IsGameRunningCalls, Is.GreaterThanOrEqualTo(1));
        Assert.That(vm, Is.Not.Null);
    }

    [Test]
    public async Task ApplyAnyway_RunsInstallerAndHidesWarning()
    {
        _instances.GameRunning = true;
        var vm = Create();

        await vm.ApplyAnywayCommand.ExecuteAsync(null);

        Assert.That(_installer.ApplyCount, Is.EqualTo(1));
        Assert.That(vm.HasGameRunningWarning, Is.False);
        Assert.That(vm.CanApply, Is.False, "执行完成后状态不再是 Idle，主按钮不应重新可点。");
    }

    [Test]
    public void ApplyAnywayCommand_IsDisabledWhenGameNotRunning()
    {
        _instances.GameRunning = false;
        var vm = Create();
        Assert.That(vm.ApplyAnywayCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void ConflictingPreview_StillBlockedEvenWhenGameNotRunning()
    {
        _instances.GameRunning = false;
        var preview = new ChangeSetPreview(
            new[] { ChangesFakes.Item("alpha") },
            new[] { "alpha 与 beta 冲突" },
            0,
            Array.Empty<string>());
        var vm = Create(preview);

        Assert.That(vm.HasGameRunningWarning, Is.False);
        Assert.That(vm.CanApply, Is.False);
    }

    [Test]
    public void GameRunning_WithoutInstanceService_DoesNotWarn()
    {
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(ChangesFakes.Preview(new[] { ChangesFakes.Item("alpha") }));
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("alpha") });
        var vm = new ChangesPageViewModel(_changeSets, _installer, _errors, null, new ChangesImmediateDispatcher());
        vm.Activate();
        vm.LoadPreview(preview);

        Assert.That(vm.HasGameRunningWarning, Is.False);
        Assert.That(vm.CanApply, Is.True);
    }
}
