using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class PlayTimePageViewModelTests
{
    private sealed class FakePlayTimeService : IPlayTimeService
    {
        public List<PlayTimeEntry> Items { get; } = new(); public int Saves { get; private set; }
        public IReadOnlyList<PlayTimeEntry> Load() => Items;
        public bool Save(string name, double hours) { Saves++; Items.RemoveAll(x => x.InstanceName == name); Items.Add(new(name, hours)); return true; }

        public int Resets { get; private set; }
        public List<string> ResetNames { get; } = new();
        public OperationResult ResetResult { get; set; } = OperationResult.Ok();
        public Exception? ResetException { get; set; }

        public Task<OperationResult> ResetAsync(string instanceName, CancellationToken ct = default)
        {
            Resets++;
            ResetNames.Add(instanceName);
            if (ResetException is not null) throw ResetException;
            if (!ResetResult.IsSuccess) return Task.FromResult(ResetResult);
            Items.RemoveAll(x => x.InstanceName == instanceName);
            Items.Add(new(instanceName, 0));
            return Task.FromResult(ResetResult);
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public string? LastTitle { get; private set; }
        public string? LastMessage { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
        {
            ConfirmCalls++;
            LastTitle = title;
            LastMessage = message;
            return Task.FromResult(ConfirmResult);
        }

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
            => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title,
            Func<IProgress<double>, CancellationToken, Task> work) => Task.FromResult(OperationResult<bool>.Ok(true));
    }

    [Test] public void LoadsEmptyAndAllowsEditCommitCancelAndInvalidInput()
    {
        var service = new FakePlayTimeService(); var vm = new PlayTimePageViewModel(service); vm.RefreshCommand.Execute(null); Assert.That(vm.ShowEmptyState, Is.True);
        service.Items.Add(new("KSP", 8)); vm.RefreshCommand.Execute(null); var row = vm.Entries.Single(); vm.BeginEditCommand.Execute(row); Assert.That(row.IsEditing, Is.True); row.EditText = "12.6"; vm.CommitEditCommand.Execute(row); Assert.That(row.Hours, Is.EqualTo(13)); Assert.That(service.Saves, Is.EqualTo(1));
        vm.BeginEditCommand.Execute(row); row.EditText = "-2"; vm.CommitEditCommand.Execute(row); Assert.That(row.IsEditing, Is.True); Assert.That(service.Saves, Is.EqualTo(1)); Assert.That(vm.ErrorText, Is.Not.Null); vm.CancelEditCommand.Execute(row); Assert.That(row.IsEditing, Is.False);
    }

    /// <summary>确认之后才调服务；成功后列表要按新值刷新，状态栏给出带实例名的回执。</summary>
    [Test]
    public async Task ResetConfirmsThenCallsServiceAndRefreshesRow()
    {
        var service = new FakePlayTimeService();
        service.Items.Add(new("KSP", 8));
        var dialogs = new FakeDialogs();
        var vm = new PlayTimePageViewModel(service, dialogs);
        vm.RefreshCommand.Execute(null);
        vm.SelectedEntry = vm.Entries.Single();

        Assert.That(vm.HasSelection, Is.True);
        Assert.That(vm.CanReset, Is.True);

        await vm.ResetCommand.ExecuteAsync(null);

        Assert.That(dialogs.ConfirmCalls, Is.EqualTo(1));
        Assert.That(dialogs.LastTitle, Does.Contain("KSP"));
        Assert.That(dialogs.LastMessage, Does.Contain("KSP"));
        Assert.That(dialogs.LastMessage, Does.Contain("无法撤销"));
        Assert.That(service.Resets, Is.EqualTo(1));
        Assert.That(service.ResetNames.Single(), Is.EqualTo("KSP"));
        Assert.That(vm.Entries.Single().Hours, Is.EqualTo(0));
        Assert.That(vm.SelectedEntry?.InstanceName, Is.EqualTo("KSP"));
        Assert.That(vm.StatusText, Is.EqualTo("已重置「KSP」的时长。"));
        Assert.That(vm.HasStatus, Is.True);
        Assert.That(vm.ErrorText, Is.Null);
    }

    /// <summary>取消确认时一个字节都不该动。</summary>
    [Test]
    public async Task ResetDoesNothingWhenConfirmationIsCancelled()
    {
        var service = new FakePlayTimeService();
        service.Items.Add(new("KSP", 8));
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var vm = new PlayTimePageViewModel(service, dialogs);
        vm.RefreshCommand.Execute(null);
        vm.SelectedEntry = vm.Entries.Single();

        await vm.ResetCommand.ExecuteAsync(null);

        Assert.That(dialogs.ConfirmCalls, Is.EqualTo(1));
        Assert.That(service.Resets, Is.EqualTo(0));
        Assert.That(vm.Entries.Single().Hours, Is.EqualTo(8));
        Assert.That(vm.StatusText, Is.EqualTo("已取消重置。"));
        Assert.That(vm.ErrorText, Is.Null);
    }

    /// <summary>服务返回失败时只写错误出口，不抛异常，也不刷新成假的 0。</summary>
    [Test]
    public async Task ResetFailureReportsErrorWithoutThrowing()
    {
        var service = new FakePlayTimeService { ResetResult = OperationResult.Fail("游戏时长文件只读。", retryable: true) };
        service.Items.Add(new("KSP", 8));
        var vm = new PlayTimePageViewModel(service, new FakeDialogs());
        vm.RefreshCommand.Execute(null);
        vm.SelectedEntry = vm.Entries.Single();

        Assert.DoesNotThrowAsync(async () => await vm.ResetCommand.ExecuteAsync(null));

        Assert.That(service.Resets, Is.EqualTo(1));
        Assert.That(vm.ErrorText, Does.Contain("游戏时长文件只读。"));
        Assert.That(vm.ErrorText, Does.Contain("可重试"));
        Assert.That(vm.StatusText, Does.Contain("游戏时长文件只读。"));
        Assert.That(vm.Entries.Single().Hours, Is.EqualTo(8));
    }

    /// <summary>服务抛异常时也只是落到错误出口（最后一道兜底）。</summary>
    [Test]
    public async Task ResetExceptionIsCaughtIntoErrorText()
    {
        var service = new FakePlayTimeService { ResetException = new InvalidOperationException("盘满了") };
        service.Items.Add(new("KSP", 8));
        var vm = new PlayTimePageViewModel(service, new FakeDialogs());
        vm.RefreshCommand.Execute(null);
        vm.SelectedEntry = vm.Entries.Single();

        Assert.DoesNotThrowAsync(async () => await vm.ResetCommand.ExecuteAsync(null));

        Assert.That(vm.ErrorText, Does.Contain("盘满了"));
    }

    /// <summary>没有确认对话框时重置入口不可用；即使硬执行也只会写错误文案，绝不静默清零。</summary>
    [Test]
    public async Task ResetIsBlockedWithoutConfirmationDialog()
    {
        var service = new FakePlayTimeService();
        service.Items.Add(new("KSP", 8));
        var vm = new PlayTimePageViewModel(service);
        vm.RefreshCommand.Execute(null);
        vm.SelectedEntry = vm.Entries.Single();

        Assert.That(vm.HasSelection, Is.True);
        Assert.That(vm.CanReset, Is.False);
        Assert.That(vm.ResetCommand.CanExecute(null), Is.False);

        await vm.ResetCommand.ExecuteAsync(null);

        Assert.That(service.Resets, Is.EqualTo(0));
        Assert.That(vm.ErrorText, Does.Contain("确认对话框"));
        Assert.That(vm.Entries.Single().Hours, Is.EqualTo(8));
    }
}
