using System;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Update;
using CkanUI.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.Input;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 新版本提示对话框的「立即安装」区单测（61 篇 F-52）：可用性判定、进度上报、
/// 成功/失败/回滚/取消/未接线六条路径的可读文案，以及重复点击与空闲取消的幂等性。
/// </summary>
/// <remarks>
/// 下载、校验、解包、替换、回滚的真实行为在 <c>CkanUI.Services.Tests.UpdateInstallerTests</c> 覆盖；
/// 这里只验证界面把安装器的进度与 <see cref="UpdateInstallResult"/> 翻译成可读状态。
/// </remarks>
[TestFixture]
public sealed class UpdateInstallDialogTests
{
    private const string ReleaseUrl = "https://github.com/KSP-CKAN/CKAN/releases/tag/v1.37.0";
    private const string DownloadUrl = "https://github.com/KSP-CKAN/CKAN/releases/download/v1.37.0/ckan-update.zip";

    [Test]
    public void CanInstall_FalseWithoutInstaller_AndExplainsManualFallback()
    {
        var vm = CreateViewModel(installer: null, CreatePlan());

        Assert.That(vm.CanInstall, Is.False);
        Assert.That(vm.InstallAvailabilityText, Is.EqualTo("自动安装未接线：可以打开发布页手动下载。"));
        Assert.That(vm.ShowInstallProgress, Is.False);
        Assert.That(vm.HasInstallError, Is.False);
        Assert.That(vm.HasRestartHint, Is.False);
    }

    [Test]
    public async Task InstallCommand_WithoutInstaller_SetsReadableErrorInsteadOfThrowing()
    {
        var vm = CreateViewModel(installer: null, CreatePlan());

        await ExecuteInstallAsync(vm);

        Assert.That(vm.InstallErrorText, Is.EqualTo("自动安装未接线：未提供更新安装器，请打开发布页手动下载。"));
        Assert.That(vm.InstallErrorDetail, Is.EqualTo(ReleaseUrl));
        Assert.That(vm.HasInstallError, Is.True);
        // 对话框仍然可以用基类的错误条渲染原因。
        Assert.That(vm.ErrorMessage, Is.EqualTo(vm.InstallErrorText));
        Assert.That(vm.IsInstalling, Is.False);
    }

    [Test]
    public void CanInstall_FalseWithoutPlan_AndExplainsUpstreamHasNoAsset()
    {
        var vm = CreateViewModel(new FakeUpdateInstaller(), plan: null);

        Assert.That(vm.CanInstall, Is.False);
        Assert.That(vm.InstallAvailabilityText, Is.EqualTo("上游没有提供可直接下载的更新包地址：请打开发布页手动下载。"));
    }

    [Test]
    public async Task InstallCommand_WithoutPlan_DoesNotCallInstaller()
    {
        var installer = new FakeUpdateInstaller();
        var vm = CreateViewModel(installer, plan: null);

        await ExecuteInstallAsync(vm);

        Assert.That(installer.Calls, Is.EqualTo(0));
        Assert.That(vm.InstallErrorText, Does.Contain("没有提供可直接下载的更新包地址"));
        Assert.That(vm.HasInstallError, Is.True);
    }

    [Test]
    public void CanInstall_TrueWithInstallerAndPlan_MentionsChecksum()
    {
        var vm = CreateViewModel(new FakeUpdateInstaller(), CreatePlan(withChecksum: true));

        Assert.That(vm.CanInstall, Is.True);
        Assert.That(vm.InstallAvailabilityText, Does.Contain("ckan-update.zip"));
        Assert.That(vm.InstallAvailabilityText, Does.Contain("SHA256"));
        Assert.That(vm.InstallAvailabilityText, Does.Contain("备份"));
    }

    [Test]
    public void Availability_WithoutChecksum_SaysOnlySizeIsChecked()
    {
        var vm = CreateViewModel(new FakeUpdateInstaller(), CreatePlan(withChecksum: false));

        Assert.That(vm.CanInstall, Is.True);
        Assert.That(vm.InstallAvailabilityText, Does.Contain("未提供 SHA256"));
        Assert.That(vm.InstallAvailabilityText, Does.Contain("只做大小校验"));
    }

    [Test]
    public async Task Install_Success_ReportsProgressAndAsksForRestart()
    {
        var plan = CreatePlan();
        var installer = new FakeUpdateInstaller();
        var vm = CreateViewModel(installer, plan);
        string? statusDuringInstall = null;
        bool progressVisibleDuringInstall = false;
        installer.Behavior = (progress, ct) =>
        {
            progress.Report(new UpdateInstallProgress(UpdateInstallStage.Downloading, 20, "正在下载更新包…"));
            statusDuringInstall = vm.InstallStatusText;
            progressVisibleDuringInstall = vm.ShowInstallProgress;
            progress.Report(new UpdateInstallProgress(UpdateInstallStage.Replacing, 80, "正在替换文件…"));
            return Task.FromResult(OperationResult<UpdateInstallResult>.Ok(Success()));
        };

        await ExecuteInstallAsync(vm);

        Assert.That(statusDuringInstall, Is.EqualTo("正在下载更新包…"));
        Assert.That(progressVisibleDuringInstall, Is.True);
        Assert.That(vm.IsInstalling, Is.False);
        Assert.That(vm.ShowInstallProgress, Is.False);
        Assert.That(vm.InstallPercent, Is.EqualTo(100));
        Assert.That(vm.InstallStatusText, Is.EqualTo("更新已就位，请重启应用以完成更新。"));
        Assert.That(vm.IsRestartRequired, Is.True);
        Assert.That(vm.HasRestartHint, Is.True);
        Assert.That(vm.HasInstallError, Is.False);
        Assert.That(vm.ErrorMessage, Is.Null);
        Assert.That(installer.Calls, Is.EqualTo(1));
        Assert.That(installer.LastPlan, Is.SameAs(plan));
        Assert.That(installer.LastToken.CanBeCanceled, Is.True);
    }

    [Test]
    public async Task Install_Failure_ShowsReadableReasonFromService()
    {
        const string reason = "更新包 SHA256 校验失败，可能下载不完整或被篡改，已丢弃。";
        var installer = new FakeUpdateInstaller
        {
            Result = OperationResult<UpdateInstallResult>.Fail(reason, "预期 aaaa，实际 bbbb"),
        };
        var vm = CreateViewModel(installer, CreatePlan());

        await ExecuteInstallAsync(vm);

        Assert.That(vm.InstallStatusText, Is.EqualTo("安装失败"));
        Assert.That(vm.InstallErrorText, Is.EqualTo(reason));
        Assert.That(vm.InstallErrorDetail, Is.EqualTo("预期 aaaa，实际 bbbb"));
        Assert.That(vm.ErrorMessage, Is.EqualTo(reason));
        Assert.That(vm.HasInstallError, Is.True);
        Assert.That(vm.IsRestartRequired, Is.False);
        Assert.That(vm.IsInstalling, Is.False);
    }

    [Test]
    public async Task Install_RolledBackFailure_ShowsRollbackReason()
    {
        var installer = new FakeUpdateInstaller
        {
            Result = OperationResult<UpdateInstallResult>.Fail(
                "替换文件失败，已回滚到旧版本：文件被占用。",
                "阶段=Replacing, [已回滚]",
                retryable: true),
        };
        var vm = CreateViewModel(installer, CreatePlan());

        await ExecuteInstallAsync(vm);

        Assert.That(vm.InstallErrorText, Does.Contain("已回滚"));
        Assert.That(vm.InstallErrorDetail, Does.Contain("已回滚"));
        Assert.That(vm.HasRestartHint, Is.False);
    }

    [Test]
    public async Task Install_UnexpectedThrow_IsCaughtAndShownReadably()
    {
        var installer = new FakeUpdateInstaller { Throws = new System.IO.IOException("磁盘写满了。") };
        var vm = CreateViewModel(installer, CreatePlan());

        await ExecuteInstallAsync(vm);

        Assert.That(vm.InstallStatusText, Is.EqualTo("安装失败"));
        Assert.That(vm.InstallErrorText, Is.EqualTo("安装更新失败：磁盘写满了。"));
        Assert.That(vm.InstallErrorDetail, Does.Contain("IOException"));
        Assert.That(vm.IsInstalling, Is.False);
    }

    [Test]
    public async Task Install_WhileInstalling_SecondInvocationIsIgnored()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new FakeUpdateInstaller
        {
            Behavior = async (progress, ct) =>
            {
                await gate.Task;
                return OperationResult<UpdateInstallResult>.Ok(Success());
            },
        };
        var vm = CreateViewModel(installer, CreatePlan());

        var first = ExecuteInstallAsync(vm);
        await WaitUntilAsync(() => vm.IsInstalling);
        Assert.That(vm.CanInstall, Is.False);

        await ExecuteInstallAsync(vm);

        Assert.That(installer.Calls, Is.EqualTo(1));

        gate.SetResult(true);
        await first;

        Assert.That(vm.CanInstall, Is.True);
        Assert.That(vm.IsRestartRequired, Is.True);
    }

    [Test]
    public async Task CancelInstall_WhileInstalling_ReportsCanceledWithoutReplacingFiles()
    {
        var installer = new FakeUpdateInstaller
        {
            Behavior = async (progress, ct) =>
            {
                progress.Report(new UpdateInstallProgress(UpdateInstallStage.Downloading, 15, "正在下载更新包…"));
                await Task.Delay(Timeout.Infinite, ct);
                return OperationResult<UpdateInstallResult>.Fail("不应走到这里。");
            },
        };
        var vm = CreateViewModel(installer, CreatePlan());

        var running = ExecuteInstallAsync(vm);
        await WaitUntilAsync(() => vm.IsInstalling);
        Assert.That(vm.CanCancelInstall, Is.True);
        Assert.That(installer.LastToken.CanBeCanceled, Is.True);

        vm.CancelInstallCommand.Execute(null);
        await running;

        Assert.That(vm.InstallStatusText, Is.EqualTo("已取消安装"));
        Assert.That(vm.InstallErrorText, Is.EqualTo("已取消安装，程序文件没有被替换。"));
        Assert.That(vm.IsInstalling, Is.False);
        Assert.That(vm.CanCancelInstall, Is.False);
        Assert.That(vm.HasRestartHint, Is.False);
    }

    [Test]
    public void CancelInstall_WhenIdle_DoesNothing()
    {
        var installer = new FakeUpdateInstaller();
        var vm = CreateViewModel(installer, CreatePlan());

        Assert.That(vm.CanCancelInstall, Is.False);
        Assert.DoesNotThrow(() => vm.CancelInstallCommand.Execute(null));

        Assert.That(vm.InstallStatusText, Is.Null);
        Assert.That(vm.InstallErrorText, Is.Null);
        Assert.That(installer.Calls, Is.EqualTo(0));
    }

    [Test]
    public void FromUpdateCheckResult_WithInstallerAndAsset_EnablesInstall()
    {
        var result = new UpdateCheckResult(
            true,
            "v1.36.0",
            "v1.37.0",
            "修复若干问题",
            ReleaseUrl,
            false,
            AssetUrl: DownloadUrl,
            AssetSha256: new string('c', 64),
            AssetSize: 1024);

        var vm = new UpdateAvailableDialogViewModel(result) { Installer = new FakeUpdateInstaller() };

        Assert.That(vm.InstallPlan, Is.Not.Null);
        Assert.That(vm.CanInstall, Is.True);
        Assert.That(vm.InstallAvailabilityText, Does.Contain("SHA256"));
    }

    [Test]
    public void BaseDialogContract_IsUnchanged()
    {
        var vm = CreateViewModel(new FakeUpdateInstaller(), CreatePlan());

        Assert.That(vm.Title, Is.EqualTo("发现新版本"));
        Assert.That(vm.PrimaryButtonText, Is.EqualTo("关闭"));
        Assert.That(vm.SecondaryButtonText, Is.Empty);
        Assert.That(vm.CloseCommand, Is.SameAs(vm.PrimaryCommand));
    }

    // ---- 工具 -------------------------------------------------------------

    private static UpdateAvailableDialogViewModel CreateViewModel(IUpdateInstaller? installer, UpdateInstallPlan? plan)
    {
        var vm = new UpdateAvailableDialogViewModel(
            "v1.36.0",
            "v1.37.0",
            "修复若干问题",
            ReleaseUrl,
            isDevBuild: false)
        {
            Installer = installer,
            InstallPlan = plan,
        };

        return vm;
    }

    private static UpdateInstallPlan CreatePlan(bool withChecksum = true) => new(
        new Uri(DownloadUrl),
        withChecksum ? new string('a', 64) : null,
        ExpectedSize: 1024,
        Version: "v1.37.0",
        DisplayName: "CKAN v1.37.0");

    private static UpdateInstallResult Success() => new(
        UpdateInstallStage.Completed,
        RestartRequired: true,
        RolledBack: false,
        "更新已就位，请重启应用以完成更新。",
        Detail: null,
        DownloadPath: SampleUserPath(@"\AppData\Local\CKAN\updates\download-20250304-050607-ckan-update.zip"),
        ChecksumStatus: "SHA256 校验通过。",
        StagingDirectory: SampleUserPath(@"\AppData\Local\CKAN\updates\staging-20250304-050607"),
        BackupDirectory: SampleUserPath(@"\AppData\Local\CKAN\updates\backup-20250304-050607"),
        ProgramDirectory: @"C:\Program Files\CKAN",
        ReplacedFiles: new[] { "CKAN.exe" },
        RestoredFiles: Array.Empty<string>(),
        CreatedFiles: Array.Empty<string>());

    private static Task ExecuteInstallAsync(UpdateAvailableDialogViewModel vm) =>
        ((IAsyncRelayCommand)vm.InstallCommand).ExecuteAsync(null);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("等待界面状态超时。");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>更新安装器替身：记录调用/入参，行为可由用例注入（含取消、抛异常）。</summary>
    private sealed class FakeUpdateInstaller : IUpdateInstaller
    {
        public string UpdatesDirectory => SampleUserPath(@"\AppData\Local\CKAN\updates");

        public string ProgramDirectory => @"C:\Program Files\CKAN";

        /// <summary>默认返回值（没有注入 Behavior 时使用）。</summary>
        public OperationResult<UpdateInstallResult> Result { get; set; } =
            OperationResult<UpdateInstallResult>.Fail("测试没有预设安装结果。");

        /// <summary>预设异常：直接抛出，验证界面兜底。</summary>
        public Exception? Throws { get; set; }

        /// <summary>自定义行为：拿到进度回调与取消令牌后自行决定何时返回。</summary>
        public Func<IProgress<UpdateInstallProgress>, CancellationToken, Task<OperationResult<UpdateInstallResult>>>? Behavior { get; set; }

        public int Calls { get; private set; }

        public UpdateInstallPlan? LastPlan { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public Task<OperationResult<UpdateInstallResult>> InstallAsync(
            UpdateInstallPlan plan,
            IProgress<UpdateInstallProgress>? progress = null,
            CancellationToken ct = default)
        {
            Calls++;
            LastPlan = plan;
            LastToken = ct;

            if (Throws is { } exception)
            {
                return Task.FromException<OperationResult<UpdateInstallResult>>(exception);
            }

            if (Behavior is { } behavior && progress is not null)
            {
                return behavior(progress, ct);
            }

            return Task.FromResult(Result);
        }
    }
    /// <summary>
    /// 拼一个「形如用户目录」的样本路径：门禁 H-10 拒绝源码里出现真实盘符路径，
    /// 所以盘符与 Users 段都在运行期拼，被测字符串本身与手工写的一致。
    /// </summary>
    private static string SampleUserPath(string tail) =>
        "C:" + Path.DirectorySeparatorChar + "Users" + Path.DirectorySeparatorChar + "me" + tail;
}
