using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Update;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Update;

/// <summary>
/// 自动更新安装服务（12 篇 F-52）：下载 → 校验 → 解包 → 备份 → 原地替换，失败自动回滚。
/// <para>
/// 设计约束：
/// <list type="bullet">
/// <item>下载一律走已注册的 <see cref="IDownloadService"/> 端口，本服务不新建任何 HttpClient；</item>
/// <item>任何失败都返回 <see cref="OperationResult{T}"/> 的中文文案，绝不把异常抛给界面；</item>
/// <item>替换前把将被覆盖的旧文件复制到 backup-&lt;时间戳&gt;，替换中途出错立即回滚；</item>
/// <item>解包逐条校验条目落点，越界路径（zip slip）直接拒绝整个更新包。</item>
/// </list>
/// </para>
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private const string CkanFolderName = "CKAN";
    private const string UpdatesFolderName = "updates";
    private const string DownloadPrefix = "download-";
    private const string StagingPrefix = "staging-";
    private const string BackupPrefix = "backup-";
    private const string FallbackPackageName = "ckan-update.zip";
    private const int MaxPayloadDescendDepth = 4;

    private readonly IDownloadService _downloads;
    private readonly Func<DateTimeOffset> _clock;
    private readonly log4net.ILog _log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);

    /// <summary>
    /// 构造安装器。
    /// </summary>
    /// <param name="downloadService">已注册的下载端口（必填）。</param>
    /// <param name="updatesDirectory">更新包/备份目录；为空时用 <see cref="DefaultUpdatesDirectory"/>。</param>
    /// <param name="programDirectory">被替换的程序目录；为空时用 <see cref="AppContext.BaseDirectory"/>。</param>
    /// <param name="clock">时钟，测试可注入固定时间。</param>
    public UpdateInstaller(
        IDownloadService downloadService,
        string? updatesDirectory = null,
        string? programDirectory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _downloads = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        _clock = clock ?? (static () => DateTimeOffset.Now);
        UpdatesDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(updatesDirectory)
            ? DefaultUpdatesDirectory
            : updatesDirectory!);
        ProgramDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(programDirectory)
            ? AppContext.BaseDirectory
            : programDirectory!);
    }

    /// <summary>默认更新目录：%LocalAppData%\CKAN\updates。</summary>
    public static string DefaultUpdatesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CkanFolderName,
        UpdatesFolderName);

    /// <inheritdoc />
    public string UpdatesDirectory { get; }

    /// <inheritdoc />
    public string ProgramDirectory { get; }

    /// <inheritdoc />
    public async Task<OperationResult<UpdateInstallResult>> InstallAsync(
        UpdateInstallPlan plan,
        IProgress<UpdateInstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (plan is null)
        {
            return Fail(UpdateInstallStage.Failed, "缺少更新包信息，无法安装。");
        }

        if (plan.DownloadUrl is null)
        {
            return Fail(UpdateInstallStage.Failed, "更新包没有下载地址，无法安装。");
        }

        var stamp = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var downloadPath = Path.Combine(UpdatesDirectory, DownloadPrefix + stamp + "-" + plan.FileName);
        var stagingRoot = Path.Combine(UpdatesDirectory, StagingPrefix + stamp);
        var backupDirectory = Path.Combine(UpdatesDirectory, BackupPrefix + stamp);

        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(UpdatesDirectory);

            // 1. 下载
            Report(progress, UpdateInstallProgress.For(UpdateInstallStage.Downloading, $"正在下载 {plan.Title} …"));
            var download = await DownloadAsync(plan, downloadPath, progress, ct).ConfigureAwait(false);
            if (!download.IsSuccess)
            {
                return Fail(UpdateInstallStage.Failed, download.Message ?? "下载更新包失败。", download.Detail, downloadPath);
            }

            // 2. 校验
            ct.ThrowIfCancellationRequested();
            Report(progress, UpdateInstallProgress.For(UpdateInstallStage.Verifying, "正在校验更新包 …", 30));
            var verify = VerifyPackage(downloadPath, plan);
            if (!verify.IsSuccess)
            {
                TryDelete(downloadPath);
                return Fail(UpdateInstallStage.Failed, verify.Message ?? "更新包校验未通过。", verify.Detail, downloadPath);
            }

            var checksumStatus = verify.Value ?? string.Empty;

            // 3. 解包
            ct.ThrowIfCancellationRequested();
            Report(progress, UpdateInstallProgress.For(UpdateInstallStage.Extracting, "正在解包更新包 …", 55));
            var extract = ExtractPackage(downloadPath, stagingRoot);
            if (!extract.IsSuccess)
            {
                return Fail(UpdateInstallStage.Failed, extract.Message ?? "更新包解压失败。",
                    extract.Detail, downloadPath, checksumStatus, stagingRoot);
            }

            var payloadRoot = extract.Value!;

            // 4. 备份 + 5. 替换
            ct.ThrowIfCancellationRequested();
            Report(progress, UpdateInstallProgress.For(UpdateInstallStage.BackingUp, "正在备份旧文件 …", 70));
            var apply = ApplyPayload(payloadRoot, backupDirectory, checksumStatus, progress, ct);
            if (!apply.IsSuccess)
            {
                return Fail(UpdateInstallStage.Failed, apply.Message ?? "更新包无法应用到程序目录。",
                    apply.Detail, downloadPath, checksumStatus, stagingRoot, backupDirectory,
                    apply.Value?.Restored, apply.Value?.RolledBack ?? false);
            }

            var result = apply.Value!;
            _log.Info($"自动更新已就位：{result.Replaced.Count} 个文件，备份目录 {backupDirectory}");
            Report(progress, UpdateInstallProgress.For(
                UpdateInstallStage.Completed,
                "更新已就位，重启应用后生效。",
                100));
            return OperationResult<UpdateInstallResult>.Ok(result);
        }
        catch (OperationCanceledException ex)
        {
            _log.Info($"自动更新被取消：{ex.Message}");
            Report(progress, UpdateInstallProgress.For(UpdateInstallStage.Canceled, "更新已取消。"));
            return Fail(UpdateInstallStage.Canceled, "更新已取消。", ex.Message, downloadPath, null, stagingRoot, backupDirectory);
        }
        catch (Exception ex)
        {
            _log.Warn("自动更新失败", ex);
            return Fail(UpdateInstallStage.Failed, "更新失败：" + ex.Message, ex.ToString(), downloadPath, null, stagingRoot, backupDirectory);
        }
    }

    // ---------- 下载 ----------

    private async Task<OperationResult<DownloadTaskInfo>> DownloadAsync(
        UpdateInstallPlan plan,
        string destinationPath,
        IProgress<UpdateInstallProgress>? progress,
        CancellationToken ct)
    {
        var request = new DownloadRequest(
            Urls: new[] { plan.DownloadUrl },
            DestinationPath: destinationPath,
            ExpectedSize: plan.ExpectedSize,
            ExpectedSha256: plan.ExpectedSha256,
            MimeType: "application/zip",
            DisplayName: plan.Title);
        var relay = new DownloadProgressRelay(progress);
        var result = await _downloads.DownloadAsync(request, relay, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        if (!File.Exists(destinationPath))
        {
            return OperationResult<DownloadTaskInfo>.Fail("下载完成但没有找到更新包文件。", destinationPath);
        }

        return result;
    }

    // ---------- 校验 ----------

    private static OperationResult<string> VerifyPackage(string downloadPath, UpdateInstallPlan plan)
    {
        var info = new FileInfo(downloadPath);
        if (!info.Exists)
        {
            return OperationResult<string>.Fail("更新包不存在，无法校验。", downloadPath);
        }

        if (info.Length <= 0)
        {
            return OperationResult<string>.Fail("更新包为空文件，已丢弃。", downloadPath);
        }

        if (plan.ExpectedSize > 0 && info.Length != plan.ExpectedSize)
        {
            return OperationResult<string>.Fail(
                $"更新包大小与预期不符（预期 {plan.ExpectedSize} 字节，实际 {info.Length} 字节），已丢弃。",
                downloadPath);
        }

        if (!plan.HasChecksum)
        {
            return OperationResult<string>.Ok("上游未提供 SHA256 校验值，已跳过完整性校验（只做了大小校验）。");
        }

        var expected = NormalizeHash(plan.ExpectedSha256!);
        var actual = ComputeSha256(downloadPath);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.Fail(
                "更新包 SHA256 校验失败，可能下载不完整或被篡改，已丢弃。",
                $"预期 {expected}，实际 {actual}");
        }

        return OperationResult<string>.Ok("SHA256 校验通过。");
    }

    private static string NormalizeHash(string hash)
    {
        var trimmed = hash.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["sha256:".Length..];
        }

        return trimmed.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    // ---------- 解包 ----------

    private static OperationResult<string> ExtractPackage(string archivePath, string stagingRoot)
    {
        try
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }

            Directory.CreateDirectory(stagingRoot);
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count == 0)
            {
                return OperationResult<string>.Fail("更新包是空压缩包，已拒绝安装。", stagingRoot);
            }

            var files = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    // 目录条目，跳过（落点由文件条目创建）。
                    continue;
                }

                if (!TryResolveEntryPath(stagingRoot, entry.FullName, out var target))
                {
                    return OperationResult<string>.Fail(
                        "更新包内含越界路径（可能是恶意包），已拒绝解压。",
                        entry.FullName);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                files++;
            }

            if (files == 0)
            {
                return OperationResult<string>.Fail("更新包里没有任何文件，已拒绝安装。", stagingRoot);
            }

            return OperationResult<string>.Ok(ResolvePayloadRoot(stagingRoot));
        }
        catch (InvalidDataException ex)
        {
            return OperationResult<string>.Fail("更新包不是有效的 zip 文件，已拒绝安装。", ex.Message);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Fail("解压更新包失败：" + ex.Message, ex.ToString());
        }
    }

    /// <summary>把条目相对路径拼成暂存目录下的绝对路径，越界（.. / 盘符 / 绝对路径）返回 false。</summary>
    private static bool TryResolveEntryPath(string root, string entryFullName, out string target)
    {
        target = string.Empty;
        var normalized = entryFullName.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                return false;
            }
        }

        if (Path.IsPathRooted(normalized))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsInside(root, candidate))
        {
            return false;
        }

        target = candidate;
        return true;
    }

    /// <summary>剥掉压缩包自带的单层外壳目录（GitHub release 资产常见形状：CKAN/…）。</summary>
    private static string ResolvePayloadRoot(string stagingRoot)
    {
        var current = stagingRoot;
        for (var depth = 0; depth < MaxPayloadDescendDepth; depth++)
        {
            var files = Directory.GetFiles(current);
            if (files.Length > 0)
            {
                return current;
            }

            var directories = Directory.GetDirectories(current);
            if (directories.Length != 1)
            {
                return current;
            }

            current = directories[0];
        }

        return current;
    }

    // ---------- 备份 + 替换 ----------

    private OperationResult<UpdateInstallResult> ApplyPayload(
        string payloadRoot,
        string backupDirectory,
        string checksumStatus,
        IProgress<UpdateInstallProgress>? progress,
        CancellationToken ct)
    {
        var relativeFiles = EnumerateRelativeFiles(payloadRoot);
        if (relativeFiles.Count == 0)
        {
            return OperationResult<UpdateInstallResult>.Fail("更新包里没有任何文件，已拒绝安装。", payloadRoot);
        }

        Directory.CreateDirectory(ProgramDirectory);
        Directory.CreateDirectory(backupDirectory);

        var backedUp = new List<string>();
        var replaced = new List<string>();
        var created = new List<string>();
        try
        {
            foreach (var relative in relativeFiles)
            {
                ct.ThrowIfCancellationRequested();
                var destination = Path.Combine(ProgramDirectory, relative);
                if (!IsInside(ProgramDirectory, destination))
                {
                    return OperationResult<UpdateInstallResult>.Fail(
                        "更新包内含越界路径（可能是恶意包），已拒绝安装。",
                        relative);
                }

                if (File.Exists(destination))
                {
                    var backupPath = Path.Combine(backupDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destination, backupPath, overwrite: true);
                    backedUp.Add(relative);
                }
            }

            Report(progress, UpdateInstallProgress.For(UpdateInstallStage.Replacing, "正在替换程序文件 …", 0));
            for (var index = 0; index < relativeFiles.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var relative = relativeFiles[index];
                var source = Path.Combine(payloadRoot, relative);
                var destination = Path.Combine(ProgramDirectory, relative);
                var existed = File.Exists(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                replaced.Add(relative);
                if (!existed)
                {
                    created.Add(relative);
                }

                Report(progress, UpdateInstallProgress.For(
                    UpdateInstallStage.Replacing,
                    $"正在替换程序文件 {index + 1}/{relativeFiles.Count} …",
                    percent: (int)((index + 1) * 100L / relativeFiles.Count)));
            }

            return OperationResult<UpdateInstallResult>.Ok(new UpdateInstallResult(
                Stage: UpdateInstallStage.Completed,
                RestartRequired: true,
                RolledBack: false,
                Message: "更新已就位，请重启应用以完成更新。",
                DownloadPath: null,
                ChecksumStatus: checksumStatus,
                StagingDirectory: payloadRoot,
                BackupDirectory: backupDirectory,
                ProgramDirectory: ProgramDirectory,
                ReplacedFiles: replaced,
                RestoredFiles: Array.Empty<string>(),
                CreatedFiles: created));
        }
        catch (OperationCanceledException)
        {
            var restored = Rollback(backupDirectory, replaced, created);
            return OperationResult<UpdateInstallResult>.Fail(
                "更新已取消，已回滚到旧版本。",
                $"已还原 {restored.Count} 个文件",
                retryable: false);
        }
        catch (Exception ex)
        {
            var restored = Rollback(backupDirectory, replaced, created);
            return OperationResult<UpdateInstallResult>.Fail(
                "替换文件失败，已回滚到旧版本：" + ex.Message,
                ex.ToString(),
                retryable: true);
        }
    }

    /// <summary>回滚：已在备份里的文件还原，本次新建的文件删除。</summary>
    private List<string> Rollback(string backupDirectory, List<string> replaced, List<string> created)
    {
        var restored = new List<string>();
        foreach (var relative in replaced)
        {
            try
            {
                var backupPath = Path.Combine(backupDirectory, relative);
                var destination = Path.Combine(ProgramDirectory, relative);
                if (File.Exists(backupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupPath, destination, overwrite: true);
                    restored.Add(relative);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"回滚文件失败：{relative}", ex);
            }
        }

        foreach (var relative in created)
        {
            if (restored.Contains(relative))
            {
                continue;
            }

            try
            {
                var destination = Path.Combine(ProgramDirectory, relative);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"回滚时删除新文件失败：{relative}", ex);
            }
        }

        return restored;
    }

    private static List<string> EnumerateRelativeFiles(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ---------- 小工具 ----------

    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalized = Path.GetFullPath(candidate);
        return normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void Report(IProgress<UpdateInstallProgress>? progress, UpdateInstallProgress value)
    {
        try
        {
            progress?.Report(value);
        }
        catch (Exception)
        {
            // 进度回调是界面的事，回调抛错不应该让安装失败。
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 删不掉就留着，不影响用户看到失败原因。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private static OperationResult<UpdateInstallResult> Fail(
        UpdateInstallStage stage,
        string message,
        string? detail = null,
        string? downloadPath = null,
        string? checksumStatus = null,
        string? stagingDirectory = null,
        string? backupDirectory = null,
        IReadOnlyList<string>? restored = null,
        bool rolledBack = false) =>
        OperationResult<UpdateInstallResult>.Fail(
            message,
            BuildFailureDetail(stage, detail, downloadPath, checksumStatus, stagingDirectory, backupDirectory, restored, rolledBack));

    private static string? BuildFailureDetail(
        UpdateInstallStage stage,
        string? detail,
        string? downloadPath,
        string? checksumStatus,
        string? stagingDirectory,
        string? backupDirectory,
        IReadOnlyList<string>? restored,
        bool rolledBack)
    {
        var parts = new List<string> { "阶段=" + stage };
        if (rolledBack)
        {
            parts.Add("已回滚");
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            parts.Add(detail!);
        }

        if (!string.IsNullOrWhiteSpace(downloadPath))
        {
            parts.Add("更新包=" + downloadPath);
        }

        if (!string.IsNullOrWhiteSpace(checksumStatus))
        {
            parts.Add("校验=" + checksumStatus);
        }

        if (!string.IsNullOrWhiteSpace(stagingDirectory))
        {
            parts.Add("暂存=" + stagingDirectory);
        }

        if (!string.IsNullOrWhiteSpace(backupDirectory))
        {
            parts.Add("备份=" + backupDirectory);
        }

        if (restored is { Count: > 0 })
        {
            parts.Add("已还原文件=" + restored.Count.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join("\n", parts);
    }

    /// <summary>把下载端口的分阶段进度翻译成安装进度（百分比在阶段内 0-100）。</summary>
    private sealed class DownloadProgressRelay : IProgress<DownloadProgressInfo>
    {
        private readonly IProgress<UpdateInstallProgress>? _inner;

        public DownloadProgressRelay(IProgress<UpdateInstallProgress>? inner) => _inner = inner;

        public void Report(DownloadProgressInfo value)
        {
            if (value is null)
            {
                return;
            }

            var message = string.IsNullOrWhiteSpace(value.Message)
                ? "正在下载更新包 …"
                : value.Message;
            Report(new UpdateInstallProgress(
                UpdateInstallStage.Downloading,
                Math.Clamp(value.Percent, 0, 100),
                message,
                value.BytesReceived,
                value.TotalBytes));
        }

        private void Report(UpdateInstallProgress value) => _inner?.Report(value);
    }
}
