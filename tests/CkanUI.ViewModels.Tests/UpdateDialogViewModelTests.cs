using System;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class UpdateDialogViewModelTests
{
    private static UpdateCheckResult CreateUpdateResult(
        bool hasUpdate = true,
        string currentVersion = "v1.31.2",
        string latestVersion = "v1.32.0",
        string releaseNotes = "Bug fixes and improvements",
        string releaseUrl = "https://github.com/KSP-CKAN/CKAN/releases/tag/v1.32.0",
        bool isDevBuild = false) =>
        new(hasUpdate, currentVersion, latestVersion, releaseNotes, releaseUrl, isDevBuild);

    [Test]
    public void Properties_MatchUpdateCheckResult()
    {
        var result = CreateUpdateResult();
        var vm = new UpdateAvailableDialogViewModel(result);

        Assert.That(vm.Title, Is.EqualTo("发现新版本"));
        Assert.That(vm.CurrentVersion, Is.EqualTo("v1.31.2"));
        Assert.That(vm.LatestVersion, Is.EqualTo("v1.32.0"));
        Assert.That(vm.ReleaseNotes, Is.EqualTo("Bug fixes and improvements"));
        Assert.That(vm.ReleaseUrl, Is.EqualTo("https://github.com/KSP-CKAN/CKAN/releases/tag/v1.32.0"));
        Assert.That(vm.IsDevBuild, Is.False);
        Assert.That(vm.UpdatePromptText, Does.Contain("CKAN 存在可用的新版本"));
    }

    [Test]
    public void DevBuild_ShowsDevBuildPromptText()
    {
        var result = CreateUpdateResult(isDevBuild: true, latestVersion: "v1.32.0-dev");
        var vm = new UpdateAvailableDialogViewModel(result);

        Assert.That(vm.Title, Is.EqualTo("发现开发版更新"));
        Assert.That(vm.IsDevBuild, Is.True);
        Assert.That(vm.UpdatePromptText, Does.Contain("新的开发构建版本"));
    }

    [Test]
    public void OpenReleaseUrlCommand_InvokesLauncherDelegate()
    {
        var result = CreateUpdateResult();
        string? launchedUrl = null;
        var vm = new UpdateAvailableDialogViewModel(result, url =>
        {
            launchedUrl = url;
            return Task.FromResult(true);
        });

        Assert.That(vm.OpenReleaseUrlCommand.CanExecute(null), Is.True);
        vm.OpenReleaseUrlCommand.Execute(null);

        Assert.That(launchedUrl, Is.EqualTo(result.ReleaseUrl));
    }

    [Test]
    public void OpenReleaseUrlCommand_WithoutLauncher_SetsErrorMessage()
    {
        var result = CreateUpdateResult();
        var vm = new UpdateAvailableDialogViewModel(result, browserLauncher: null);

        vm.OpenReleaseUrlCommand.Execute(null);

        Assert.That(vm.ErrorMessage, Does.Contain("无法自动打开浏览器"));
    }

    [Test]
    public void CloseCommand_ClosesDialog()
    {
        var result = CreateUpdateResult();
        var vm = new UpdateAvailableDialogViewModel(result);

        Assert.That(vm.IsClosed, Is.False);
        vm.CloseCommand.Execute(null);

        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public async Task SettingsPageViewModel_CheckForUpdatesCommand_WhenUpdateAvailable_SetsStatusAndShowsDialog()
    {
        var updateResult = CreateUpdateResult(hasUpdate: true, latestVersion: "v2.0.0");
        var fakeUpdateService = new FakeUpdateCheckService(
            OperationResult<UpdateCheckResult>.Ok(updateResult));

        var fakeSettings = new FakeSettingsService();
        var fakeUiSettings = new FakeUiSettingsStore();

        UpdateAvailableDialogViewModel? presentedDialog = null;
        var settingsVm = new SettingsPageViewModel(
            fakeSettings,
            fakeUiSettings,
            updateCheckService: fakeUpdateService)
        {
            ShowUpdateDialogAsync = dialog =>
            {
                presentedDialog = dialog;
                return Task.CompletedTask;
            }
        };

        await settingsVm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.That(settingsVm.StatusText, Does.Contain("发现新版本 v2.0.0"));
        Assert.That(presentedDialog, Is.Not.Null);
        Assert.That(presentedDialog!.LatestVersion, Is.EqualTo("v2.0.0"));
    }

    [Test]
    public async Task SettingsPageViewModel_CheckForUpdatesCommand_WhenNoUpdate_SetsStatusWithoutShowingDialog()
    {
        var updateResult = CreateUpdateResult(hasUpdate: false);
        var fakeUpdateService = new FakeUpdateCheckService(
            OperationResult<UpdateCheckResult>.Ok(updateResult));

        var fakeSettings = new FakeSettingsService();
        var fakeUiSettings = new FakeUiSettingsStore();

        var dialogShown = false;
        var settingsVm = new SettingsPageViewModel(
            fakeSettings,
            fakeUiSettings,
            updateCheckService: fakeUpdateService)
        {
            ShowUpdateDialogAsync = _ =>
            {
                dialogShown = true;
                return Task.CompletedTask;
            }
        };

        await settingsVm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.That(settingsVm.StatusText, Does.Contain("已是最新版本"));
        Assert.That(dialogShown, Is.False);
    }

    private sealed class FakeUpdateCheckService : CkanUI.Services.Abstractions.IUpdateCheckService
    {
        private readonly OperationResult<UpdateCheckResult> _result;

        public FakeUpdateCheckService(OperationResult<UpdateCheckResult> result)
        {
            _result = result;
        }

        public Task<OperationResult<UpdateCheckResult>> CheckForUpdatesAsync(
            bool includeDevBuilds = false,
            System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }
}
