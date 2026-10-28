using System.IO.Compression;
using System.Security.Cryptography;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Update;
using CkanUI.Services.Update;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 12 篇 F-52：自动更新「下载 → 校验 → 解包 → 备份 → 替换 → 回滚」全链路。
/// <para>
/// 全部用例离线：下载端口用替身直接把预先做好的 zip 拷到目标路径，不碰网络。
/// </para>
/// </summary>
[TestFixture]
public sealed class UpdateInstallerTests
{
    private static readonly DateTimeOffset FixedNow = new(2025, 3, 4, 5, 6, 7, TimeSpan.FromHours(8));

    [Test]
    public async Task Install_ReplacesFilesAndKeepsBackupCopy()
    {
        using var workspace = new TempWorkspace("update-install");
        var program = NewProgramDir(workspace, ("old.dll", "旧内容"));
        var package = CreatePackage(workspace, ("old.dll", "新内容"), ("added.dll", "新增"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var value = result.Value!;
        Assert.That(value.Stage, Is.EqualTo(UpdateInstallStage.Completed));
        Assert.That(value.IsInstalled, Is.True);
        Assert.That(value.RestartRequired, Is.True, "装完必须提示重启");
        Assert.That(value.RolledBack, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(program, "old.dll")), Is.EqualTo("新内容"));
        Assert.That(File.ReadAllText(Path.Combine(program, "added.dll")), Is.EqualTo("新增"));
        Assert.That(value.Replaced, Is.EquivalentTo(new[] { "added.dll", "old.dll" }));
        Assert.That(value.Created, Is.EqualTo(new[] { "added.dll" }));
        Assert.That(value.BackupDirectory, Is.Not.Null);
        Assert.That(File.ReadAllText(Path.Combine(value.BackupDirectory!, "old.dll")), Is.EqualTo("旧内容"),
            "被覆盖的旧文件必须留一份备份");
        Assert.That(value.Message, Does.Contain("重启"));
    }

    [Test]
    public async Task Install_ReportsEveryStageAndFinishesAtHundredPercent()
    {
        using var workspace = new TempWorkspace("update-progress");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace, ("a.dll", "新"));
        var installer = NewInstaller(workspace, ServingZip(package), program);
        var recorder = new ProgressRecorder();

        var result = await installer.InstallAsync(Plan(package), recorder);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var stages = recorder.Items.Select(static item => item.Stage).Distinct().ToArray();
        Assert.That(stages, Does.Contain(UpdateInstallStage.Downloading));
        Assert.That(stages, Does.Contain(UpdateInstallStage.Verifying));
        Assert.That(stages, Does.Contain(UpdateInstallStage.Extracting));
        Assert.That(stages, Does.Contain(UpdateInstallStage.BackingUp));
        Assert.That(stages, Does.Contain(UpdateInstallStage.Replacing));
        Assert.That(stages, Does.Contain(UpdateInstallStage.Completed));
        Assert.That(recorder.Items[^1].Percent, Is.EqualTo(100));
        Assert.That(recorder.Items[^1].StageText, Is.EqualTo("更新已就位"));
    }

    [Test]
    public async Task Install_VerifiesSha256WhenUpstreamProvidesOne()
    {
        using var workspace = new TempWorkspace("update-sha-ok");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace, ("a.dll", "新"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package, sha256: Sha256Of(package)));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.ChecksumStatus, Does.Contain("SHA256 校验通过"));
    }

    [Test]
    public async Task Install_RejectsChecksumMismatchAndCleansTheDownload()
    {
        using var workspace = new TempWorkspace("update-sha-bad");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace, ("a.dll", "新"));
        var installer = NewInstaller(workspace, ServingZip(package), program);
        var fakeHash = new string('0', 64);

        var result = await installer.InstallAsync(Plan(package, sha256: fakeHash));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("SHA256 校验失败"));
        Assert.That(result.Detail, Does.Contain("更新包="));
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("旧"), "校验失败不许动程序目录");
        Assert.That(Directory.GetFiles(Path.Combine(workspace.Path, "updates"), "download-*"),
            Is.Empty, "校验失败的更新包必须删除，避免反复重试同一个坏包");
    }

    [Test]
    public async Task Install_RecordsMissingChecksumInsteadOfSilentlyPassing()
    {
        using var workspace = new TempWorkspace("update-sha-none");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace, ("a.dll", "新"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.ChecksumStatus, Does.Contain("未提供"));
    }

    [Test]
    public async Task Install_RejectsPackageWhoseSizeDoesNotMatch()
    {
        using var workspace = new TempWorkspace("update-size");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace, ("a.dll", "新"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package, expectedSize: new FileInfo(package).Length + 9));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("大小与预期不符"));
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("旧"));
    }

    [Test]
    public async Task Install_RejectsZipSlipEntryAndWritesNothingOutsideStaging()
    {
        using var workspace = new TempWorkspace("update-zip-slip");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace, ("../evil.txt", "坏东西"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("越界路径"));
        Assert.That(File.Exists(workspace.Combine("evil.txt")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("旧"));
    }

    [Test]
    public async Task Install_RejectsFileThatIsNotAZip()
    {
        using var workspace = new TempWorkspace("update-not-zip");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var notZip = workspace.Combine("not-a-zip.zip");
        File.WriteAllText(notZip, "这不是压缩包");
        var installer = NewInstaller(workspace, ServingPath(notZip), program);

        var result = await installer.InstallAsync(Plan(notZip));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不是有效的 zip"));
    }

    [Test]
    public async Task Install_RejectsEmptyArchive()
    {
        using var workspace = new TempWorkspace("update-empty-zip");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace);
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("空压缩包"));
    }

    [Test]
    public async Task Install_StripsSingleWrapperDirectoryOfReleaseZip()
    {
        using var workspace = new TempWorkspace("update-wrapper");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace,
            ("CKAN-1.36/a.dll", "新"),
            ("CKAN-1.36/sub/b.dll", "子目录"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("新"));
        Assert.That(File.ReadAllText(Path.Combine(program, "sub", "b.dll")), Is.EqualTo("子目录"),
            "release 包自带的单层外壳目录要剥掉，不能变成 program/CKAN-1.36/…");
    }

    [Test]
    public async Task Install_KeepsProgramFilesThatAreNotInThePackage()
    {
        using var workspace = new TempWorkspace("update-keep");
        var program = NewProgramDir(workspace, ("a.dll", "旧"), ("user-settings.json", "{}"));
        var package = CreatePackage(workspace, ("a.dll", "新"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(File.ReadAllText(Path.Combine(program, "user-settings.json")), Is.EqualTo("{}"),
            "更新包没带的文件绝不能被删");
    }

    [Test]
    public async Task Install_RollsBackReplacedFilesWhenAReplaceFails()
    {
        using var workspace = new TempWorkspace("update-rollback");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        // 目标位置先占一个同名目录，替换阶段必然失败（File.Copy 无法覆盖目录）。
        Directory.CreateDirectory(Path.Combine(program, "zz.dll"));
        var package = CreatePackage(workspace, ("a.dll", "新"), ("zz.dll", "冲突"));
        var installer = NewInstaller(workspace, ServingZip(package), program);

        var result = await installer.InstallAsync(Plan(package));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("已回滚"));
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("旧"),
            "已替换的文件必须回滚成旧内容");
        Assert.That(Directory.Exists(Path.Combine(program, "zz.dll")), Is.True);
    }

    [Test]
    public async Task Install_RollsBackWhenCancelledDuringReplace()
    {
        using var workspace = new TempWorkspace("update-cancel-rollback");
        var program = NewProgramDir(workspace, ("a.dll", "旧"), ("b.dll", "旧B"));
        var package = CreatePackage(workspace, ("a.dll", "新A"), ("b.dll", "新B"));
        var installer = NewInstaller(workspace, ServingZip(package), program);
        using var source = new CancellationTokenSource();
        var recorder = new ProgressRecorder
        {
            OnReport = item =>
            {
                if (item.Stage == UpdateInstallStage.Replacing && item.Percent > 0)
                {
                    source.Cancel();
                }
            },
        };

        var result = await installer.InstallAsync(Plan(package), recorder, source.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("更新已取消"));
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("旧"));
        Assert.That(File.ReadAllText(Path.Combine(program, "b.dll")), Is.EqualTo("旧B"));
    }

    [Test]
    public async Task Install_DoesNotStartWhenTokenIsAlreadyCancelled()
    {
        using var workspace = new TempWorkspace("update-cancel-early");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var package = CreatePackage(workspace, ("a.dll", "新"));
        var downloads = ServingZip(package);
        var installer = NewInstaller(workspace, downloads, program);
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = await installer.InstallAsync(Plan(package), null, source.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("更新已取消。"));
        Assert.That(downloads.Requests, Is.Empty, "已取消时连下载都不该发起");
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("旧"));
    }

    [Test]
    public async Task Install_ReturnsReadableFailureWhenDownloadFails()
    {
        using var workspace = new TempWorkspace("update-download-fail");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var installer = NewInstaller(workspace,
            new FakeDownloadService(static _ => OperationResult<DownloadTaskInfo>.Fail("网络中断，请稍后重试。", "测试", true)),
            program);

        var result = await installer.InstallAsync(new UpdateInstallPlan(new Uri("https://example.invalid/ckan.zip")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("网络中断"));
        Assert.That(result.Retryable, Is.False, "安装端口自己吞掉重试语义，界面只看文案");
        Assert.That(File.ReadAllText(Path.Combine(program, "a.dll")), Is.EqualTo("旧"));
    }

    [Test]
    public async Task Install_RejectsPlanWithoutDownloadUrl()
    {
        using var workspace = new TempWorkspace("update-no-url");
        var program = NewProgramDir(workspace, ("a.dll", "旧"));
        var installer = NewInstaller(workspace, ServingZip(CreatePackage(workspace, ("a.dll", "新"))), program);

        var nullPlan = await installer.InstallAsync(null!);
        var nullUrl = await installer.InstallAsync(new UpdateInstallPlan(null!));

        Assert.That(nullPlan.IsSuccess, Is.False);
        Assert.That(nullPlan.Message, Does.Contain("缺少更新包信息"));
        Assert.That(nullUrl.IsSuccess, Is.False);
        Assert.That(nullUrl.Message, Does.Contain("没有下载地址"));
    }

    [Test]
    public void Plan_DerivesFileNameFromUrl()
    {
        var zipped = new UpdateInstallPlan(new Uri("https://example.com/releases/CKAN-1.36.0.zip"));
        var bare = new UpdateInstallPlan(new Uri("https://example.com/releases/CKAN-1.36.0"));
        var empty = new UpdateInstallPlan(new Uri("https://example.com/"));

        Assert.That(zipped.FileName, Is.EqualTo("CKAN-1.36.0.zip"));
        Assert.That(bare.FileName, Is.EqualTo("CKAN-1.36.0.zip"));
        Assert.That(empty.FileName, Is.EqualTo("ckan-update.zip"));
        Assert.That(zipped.HasChecksum, Is.False);
        Assert.That(zipped.Title, Is.EqualTo("CKAN-1.36.0.zip"));
    }

    // ---------- 辅助 ----------

    private static UpdateInstaller NewInstaller(TempWorkspace workspace, IDownloadService downloads, string program) =>
        new(downloads, workspace.Combine("updates"), program, static () => FixedNow);

    private static string NewProgramDir(TempWorkspace workspace, params (string Name, string Content)[] files)
    {
        var program = workspace.Combine("program");
        Directory.CreateDirectory(program);
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(program, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return program;
    }

    private static UpdateInstallPlan Plan(string packagePath, string? sha256 = null, long expectedSize = 0) =>
        new(
            new Uri(Path.GetFullPath(packagePath)),
            sha256,
            expectedSize,
            "1.36.0",
            Path.GetFileName(packagePath));

    private static string CreatePackage(TempWorkspace workspace, params (string Entry, string Content)[] entries)
    {
        var path = workspace.Combine("package-" + Guid.NewGuid().ToString("N") + ".zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var handle = archive.CreateEntry(entry);
            using var writer = new StreamWriter(handle.Open());
            writer.Write(content);
        }

        return path;
    }

    private static string Sha256Of(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static FakeDownloadService ServingZip(string packagePath) => ServingPath(packagePath);

    private static FakeDownloadService ServingPath(string sourcePath) => new(request =>
    {
        File.Copy(sourcePath, request.DestinationPath, overwrite: true);
        var length = new FileInfo(sourcePath).Length;
        return OperationResult<DownloadTaskInfo>.Ok(new DownloadTaskInfo(
            request.DestinationPath, request.PrimaryUrl, length, length, null, null, TimeSpan.Zero));
    });

    private sealed class FakeDownloadService : IDownloadService
    {
        private readonly Func<DownloadRequest, OperationResult<DownloadTaskInfo>> _handler;

        public FakeDownloadService(Func<DownloadRequest, OperationResult<DownloadTaskInfo>> handler) =>
            _handler = handler;

        public List<DownloadRequest> Requests { get; } = new();

        public Task<OperationResult<DownloadTaskInfo>> DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgressInfo>? progress = null,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            progress?.Report(DownloadProgressInfo.Start("正在下载更新包 …"));
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class ProgressRecorder : IProgress<UpdateInstallProgress>
    {
        public List<UpdateInstallProgress> Items { get; } = new();

        public Action<UpdateInstallProgress>? OnReport { get; set; }

        public void Report(UpdateInstallProgress value)
        {
            Items.Add(value);
            OnReport?.Invoke(value);
        }
    }
}
