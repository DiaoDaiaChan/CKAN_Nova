using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Spacedock;

/// <summary>
/// SpaceDock ZIP 的下载与安装实现。
/// </summary>
/// <remarks>
/// 安装布局交给 <see cref="SpaceDockInstallAdapter"/> 判断：GameData 内容写进 GameData，
/// Ships / Missions 等写进游戏根目录；没有结构信息的压缩包收进 GameData/&lt;模组名&gt;。
/// 任何 <c>..</c>、绝对路径、盘符或符号链接式条目都直接拒绝，避免 zip-slip。
/// </remarks>
public sealed class SpaceDockInstaller : ISpaceDockInstaller, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    /// <summary>默认构造：下载超时放宽到 5 分钟（大型模组包可能有数百 MB）。</summary>
    public SpaceDockInstaller()
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, ownsClient: true)
    {
    }

    /// <summary>测试构造。</summary>
    public SpaceDockInstaller(HttpMessageHandler handler)
        : this(new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(5) }, ownsClient: true)
    {
    }

    /// <summary>宿主构造。</summary>
    public SpaceDockInstaller(HttpClient client)
        : this(client, ownsClient: false)
    {
    }

    private SpaceDockInstaller(HttpClient client, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _ownsClient = ownsClient;
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> DownloadAsync(
        SpaceDockVersionInfo version,
        string targetDirectory,
        string suggestedFileName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!Uri.TryCreate(version.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
            (downloadUri.Scheme != Uri.UriSchemeHttps && downloadUri.Scheme != Uri.UriSchemeHttp))
        {
            return OperationResult<string>.Fail("SpaceDock 下载链接无效", version.DownloadUrl);
        }

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return OperationResult<string>.Fail("下载目录为空");
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            var path = BuildUniquePath(targetDirectory, suggestedFileName);
            using var response = await _client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<string>.Fail(
                    $"SpaceDock 下载失败（HTTP {(int)response.StatusCode}）",
                    downloadUri.ToString(),
                    retryable: response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                               (int)response.StatusCode >= 500);
            }

            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(path);
                throw;
            }

            return OperationResult<string>.Ok(path);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Fail("下载已取消", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            return OperationResult<string>.Fail("下载 SpaceDock 模组失败", ex.Message, retryable: true);
        }
        catch (IOException ex)
        {
            return OperationResult<string>.Fail("写入下载文件失败", ex.Message, retryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<string>.Fail("没有权限写入下载目录", ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<SpaceDockInstallPlan>> InspectAsync(
        string archivePath,
        string modName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return Task.FromResult(OperationResult<SpaceDockInstallPlan>.Fail("模组压缩包不存在", archivePath));
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            using var archive = ZipFile.OpenRead(archivePath);
            var entryNames = archive.Entries
                .Where(static entry => !string.IsNullOrEmpty(entry.Name))
                .Select(static entry => entry.FullName);
            var plan = SpaceDockInstallAdapter.Analyze(entryNames, modName);
            if (plan.Entries.Count == 0)
            {
                return Task.FromResult(OperationResult<SpaceDockInstallPlan>.Fail("压缩包里没有可安装的文件"));
            }

            ct.ThrowIfCancellationRequested();
            var targets = plan.Entries
                .Select(static entry => entry.Target == SpaceDockInstallAdapter.Target.GameRoot
                    ? SpaceDockInstallTarget.GameRoot
                    : SpaceDockInstallTarget.GameData)
                .Distinct()
                .ToArray();
            var result = new SpaceDockInstallPlan(
                MapStrategy(plan.Strategy),
                plan.InstalledRoot,
                targets,
                plan.Entries.Count);
            return Task.FromResult(OperationResult<SpaceDockInstallPlan>.Ok(result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<SpaceDockInstallPlan>.Fail("检查压缩包已取消", retryable: true));
        }
        catch (InvalidDataException ex)
        {
            return Task.FromResult(OperationResult<SpaceDockInstallPlan>.Fail("压缩包结构不安全，已拒绝安装", ex.Message));
        }
        catch (IOException ex)
        {
            return Task.FromResult(OperationResult<SpaceDockInstallPlan>.Fail("读取压缩包失败", ex.Message, retryable: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(OperationResult<SpaceDockInstallPlan>.Fail("没有权限读取压缩包", ex.Message));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> InstallAsync(
        string archivePath,
        string gameDirectory,
        string modName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return Task.FromResult(OperationResult<string>.Fail("模组压缩包不存在", archivePath));
        }

        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
        {
            return Task.FromResult(OperationResult<string>.Fail("游戏目录不存在", gameDirectory));
        }

        var staging = Path.Combine(Path.GetTempPath(), "ckanui-spacedock", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        try
        {
            Directory.CreateDirectory(staging);
            using var archive = ZipFile.OpenRead(archivePath);
            var entryNames = archive.Entries
                .Where(static entry => !string.IsNullOrEmpty(entry.Name))
                .Select(static entry => entry.FullName);
            var plan = SpaceDockInstallAdapter.Analyze(entryNames, modName);
            if (plan.Entries.Count == 0)
            {
                return Task.FromResult(OperationResult<string>.Fail("压缩包里没有可安装的文件"));
            }

            ExtractToStaging(archive, plan, staging, ct);
            ct.ThrowIfCancellationRequested();

            var gameRoot = Path.GetFullPath(gameDirectory);
            var gameDataRoot = Path.GetFullPath(Path.Combine(gameRoot, "GameData"));
            var stagingGameData = Path.Combine(staging, "GameData");
            var stagingRoot = Path.Combine(staging, "Root");
            if (Directory.Exists(stagingGameData))
            {
                CopyTree(stagingGameData, gameDataRoot, ct);
            }

            if (Directory.Exists(stagingRoot))
            {
                CopyTree(stagingRoot, gameRoot, ct);
            }

            var targetsGameRoot = plan.Entries.Any(static entry => entry.Target == SpaceDockInstallAdapter.Target.GameRoot);
            var installedPath = targetsGameRoot
                ? gameRoot
                : Path.GetFullPath(Path.Combine(gameDataRoot, plan.InstalledRoot));
            return Task.FromResult(OperationResult<string>.Ok(installedPath));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<string>.Fail("安装已取消", retryable: true));
        }
        catch (InvalidDataException ex)
        {
            return Task.FromResult(OperationResult<string>.Fail("压缩包结构不安全，已拒绝安装", ex.Message));
        }
        catch (IOException ex)
        {
            return Task.FromResult(OperationResult<string>.Fail("安装 SpaceDock 模组失败", ex.Message, retryable: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(OperationResult<string>.Fail("没有权限写入 GameData", ex.Message));
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static void ExtractToStaging(
        ZipArchive archive,
        SpaceDockInstallAdapter.Plan plan,
        string staging,
        CancellationToken ct)
    {
        var mappings = new Dictionary<string, SpaceDockInstallAdapter.Entry>(StringComparer.Ordinal);
        foreach (var mapping in plan.Entries)
        {
            mappings.TryAdd(mapping.SourcePath, mapping);
        }

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name) || !mappings.TryGetValue(entry.FullName, out var mapping))
            {
                continue;
            }

            var destinationRoot = Path.Combine(
                staging,
                mapping.Target == SpaceDockInstallAdapter.Target.GameData ? "GameData" : "Root");
            var relativeParts = mapping.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, Path.Combine(relativeParts)));
            EnsureUnderRoot(destinationRoot, destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }

    private static void CopyTree(string sourceRoot, string destinationRoot, CancellationToken ct)
    {
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
            EnsureUnderRoot(destinationRoot, destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static void EnsureUnderRoot(string root, string path, string original)
    {
        var fullRoot = Path.GetFullPath(root);
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"压缩包条目越出目标目录：{original}");
        }
    }

    private static SpaceDockInstallStrategy MapStrategy(SpaceDockInstallAdapter.Strategy strategy)
        => strategy switch
        {
            SpaceDockInstallAdapter.Strategy.GameDataTree => SpaceDockInstallStrategy.GameDataTree,
            SpaceDockInstallAdapter.Strategy.SingleFolder => SpaceDockInstallStrategy.SingleFolder,
            SpaceDockInstallAdapter.Strategy.FlatPayload => SpaceDockInstallStrategy.FlatPayload,
            SpaceDockInstallAdapter.Strategy.GameRootTree => SpaceDockInstallStrategy.GameRootTree,
            SpaceDockInstallAdapter.Strategy.MixedRoots => SpaceDockInstallStrategy.MixedRoots,
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };

    private static string BuildUniquePath(string directory, string suggestedFileName)
    {
        var name = SpaceDockInstallAdapter.SanitizeName(Path.GetFileName(suggestedFileName));
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name += ".zip";
        }

        var path = Path.Combine(directory, name);
        var index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)} ({index++}).zip");
        }

        return path;
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
