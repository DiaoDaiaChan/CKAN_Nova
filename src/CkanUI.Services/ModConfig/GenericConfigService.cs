using System.Globalization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using log4net;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 通用配置文档的加载结果。
/// <para>
/// 与 <see cref="GenericConfigFile"/> 的区别：这里带上了可编辑的 <see cref="ConfigNodeDocument"/>，
/// 调用方拿它改值，再交回 <see cref="IGenericConfigService.SaveAsync"/>。
/// </para>
/// </summary>
/// <param name="FilePath">绝对路径（已归一化）。</param>
/// <param name="RelativePath">相对 <c>&lt;gameDir&gt;/GameData</c> 的路径（不在 GameData 内时相对 gameDir），<c>/</c> 分隔。</param>
/// <param name="Document">无损按行编辑的文档。</param>
/// <param name="CanWrite">当前是否允许写回（只读属性/解析错误/非法 UTF-8 都会让它变 false）。</param>
/// <param name="ReadonlyReason">不可写原因（人话）。</param>
public sealed record GenericConfigDocument(
    string FilePath,
    string RelativePath,
    ConfigNodeDocument Document,
    bool CanWrite,
    string? ReadonlyReason);

/// <summary>
/// 不依赖 adapter 包的通用 ConfigNode 配置读写（「Mod 配置」页的兜底路径）。
/// <para>
/// 接口放在实现同一个文件里（而不是 <c>CkanUI.Services.Abstractions</c>）：签名里的
/// <see cref="ConfigNodeDocument"/> 属于 <c>CkanUI.Services</c> 工程，Abstractions 不引用 Services，
/// 挪接口会在两个工程之间形成反向依赖。
/// </para>
/// </summary>
public interface IGenericConfigService
{
    /// <summary>扫描 <c>&lt;gameDir&gt;/GameData</c> 下的候选 cfg。gameDir 为空时按 instanceName 取实例目录。</summary>
    Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
        string gameDir,
        string instanceName,
        CancellationToken cancellationToken = default);

    /// <summary>加载一个 cfg；路径必须落在 gameDir 之内（拒绝 <c>..</c> 逃逸）。</summary>
    Task<OperationResult<GenericConfigDocument>> LoadAsync(
        string gameDir,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把 <paramref name="document"/> 的改动写回磁盘：先备份（<c>&lt;文件名&gt;.bak-&lt;yyyyMMdd-HHmmss&gt;</c>），
    /// 再写同目录临时文件并原子替换。游戏正在运行且未 <paramref name="confirmedGameRunning"/> 时拒绝。
    /// </summary>
    Task<OperationResult<GenericConfigSaveResult>> SaveAsync(
        string gameDir,
        string instanceName,
        string filePath,
        ConfigNodeDocument document,
        bool confirmedGameRunning,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IGenericConfigService"/> 的默认实现。口径对齐 <see cref="AdapterWriter"/>：
/// 串行化写入、写前备份、同目录临时文件 + <c>File.Move(overwrite: true)</c>、游戏运行未确认不写。
/// </summary>
public sealed class GenericConfigService : IGenericConfigService
{
    private const string TimestampFormat = "yyyyMMdd-HHmmss";
    private const int MaxBackupAttempts = 20;

    private static readonly ILog Log = LogManager.GetLogger(typeof(GenericConfigService));

    private readonly IInstanceService? _instances;
    private readonly IGameRunningProbe? _gameRunningProbe;
    private readonly Func<DateTime> _clock;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public GenericConfigService(
        IInstanceService? instances = null,
        IGameRunningProbe? gameRunningProbe = null,
        Func<DateTime>? clock = null)
    {
        _instances = instances;
        _gameRunningProbe = gameRunningProbe;
        _clock = clock ?? (static () => DateTime.Now);
    }

    /// <inheritdoc />
    public Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
        string gameDir,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = ResolveGameDirectory(gameDir, instanceName);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return Task.FromResult(OperationResult<GenericConfigCatalog>.Fail(
                    "没有可用的游戏目录：调用方没有传 gameDir，也找不到对应的游戏实例。",
                    retryable: true));
            }

            return Task.FromResult(GenericConfigScanner.Scan(resolved, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<GenericConfigCatalog>.Fail("扫描已取消。", detail: "cancelled"));
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Log.Warn($"扫描通用配置失败：{ex.Message}", ex);
            return Task.FromResult(OperationResult<GenericConfigCatalog>.Fail(
                $"扫描配置失败：{ex.Message}",
                detail: ex.GetType().Name));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<GenericConfigDocument>> LoadAsync(
        string gameDir,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryResolveGameDirectory(gameDir, out var rootDirectory, out var directoryError))
            {
                return Task.FromResult(OperationResult<GenericConfigDocument>.Fail(directoryError));
            }

            if (!TryResolveFile(rootDirectory, filePath, out var targetPath, out var pathError))
            {
                return Task.FromResult(OperationResult<GenericConfigDocument>.Fail(pathError));
            }

            if (!File.Exists(targetPath))
            {
                return Task.FromResult(OperationResult<GenericConfigDocument>.Fail($"文件不存在：{targetPath}"));
            }

            var document = ConfigNodeDocument.ParseFile(targetPath);
            var reason = DetermineReadonlyReason(targetPath, document);

            return Task.FromResult(OperationResult<GenericConfigDocument>.Ok(new GenericConfigDocument(
                targetPath,
                ToRelativePath(rootDirectory, targetPath),
                document,
                reason is null,
                reason)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<GenericConfigDocument>.Fail("加载已取消。", detail: "cancelled"));
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Log.Warn($"加载通用配置失败（{filePath}）：{ex.Message}", ex);
            return Task.FromResult(OperationResult<GenericConfigDocument>.Fail(
                $"读取配置失败：{ex.Message}",
                detail: ex.GetType().Name));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<GenericConfigSaveResult>> SaveAsync(
        string gameDir,
        string instanceName,
        string filePath,
        ConfigNodeDocument document,
        bool confirmedGameRunning,
        CancellationToken cancellationToken = default)
    {
        if (document is null)
        {
            return OperationResult<GenericConfigSaveResult>.Fail("没有要写入的文档。");
        }

        try
        {
            await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<GenericConfigSaveResult>.Fail("写入已取消。", detail: "cancelled");
        }

        try
        {
            return SaveCore(gameDir, instanceName, filePath, document, confirmedGameRunning, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<GenericConfigSaveResult>.Fail("写入已取消。", detail: "cancelled");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private OperationResult<GenericConfigSaveResult> SaveCore(
        string gameDir,
        string instanceName,
        string filePath,
        ConfigNodeDocument document,
        bool confirmedGameRunning,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveGameDirectory(gameDir, out var rootDirectory, out var directoryError))
        {
            return OperationResult<GenericConfigSaveResult>.Fail(directoryError);
        }

        if (!TryResolveFile(rootDirectory, filePath, out var targetPath, out var pathError))
        {
            return OperationResult<GenericConfigSaveResult>.Fail(pathError);
        }

        var changedLineCount = document.PendingEdits.Count;
        if (changedLineCount == 0)
        {
            return OperationResult<GenericConfigSaveResult>.Fail("文档没有待写入的改动。");
        }

        if (document.HasParseErrors)
        {
            return OperationResult<GenericConfigSaveResult>.Fail(
                $"文档有 {document.ParseErrorCount} 个 ConfigNode 解析错误，写回会破坏原文件，已拒绝。",
                detail: "parse-errors");
        }

        if (document.IsTextLossy)
        {
            return OperationResult<GenericConfigSaveResult>.Fail(
                "文档含非法 UTF-8 字节，写回会把坏字节洗成替换字符，已拒绝。",
                detail: "lossy-text");
        }

        bool fileExists;
        try
        {
            fileExists = File.Exists(targetPath);
            if (fileExists && (File.GetAttributes(targetPath) & FileAttributes.ReadOnly) != 0)
            {
                return OperationResult<GenericConfigSaveResult>.Fail(
                    $"文件带只读属性，写入前需要先去掉只读：{targetPath}",
                    detail: "readonly");
            }

            if (!fileExists)
            {
                var parent = Path.GetDirectoryName(targetPath);
                if (parent is null || !Directory.Exists(parent))
                {
                    return OperationResult<GenericConfigSaveResult>.Fail($"目标目录不存在：{parent}");
                }
            }
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Log.Warn($"检查目标文件失败（{targetPath}）：{ex.Message}", ex);
            return OperationResult<GenericConfigSaveResult>.Fail($"检查目标文件失败：{ex.Message}");
        }

        if (!confirmedGameRunning && SafeIsGameRunning(instanceName, rootDirectory))
        {
            return OperationResult<GenericConfigSaveResult>.Fail(
                "KSP 正在运行。写入配置需要重启 KSP 才生效，且可能与游戏自身的写盘竞争；请先关闭 KSP，或在提示里确认后再写入。",
                detail: "game-running",
                retryable: true);
        }

        // 1) 备份：只在目标已存在时做。备份失败就不写——宁可不改，也不能改坏了没退路。
        var backupPath = string.Empty;
        if (fileExists)
        {
            try
            {
                backupPath = CreateBackup(targetPath);
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                Log.Warn($"备份失败（{targetPath}）：{ex.Message}", ex);
                return OperationResult<GenericConfigSaveResult>.Fail(
                    $"写前备份失败，已放弃写入：{ex.Message}",
                    detail: "backup-failed");
            }
        }

        // 2) 写同目录临时文件，再原子替换。File.Move 是最后一步：目标要么是旧内容，要么是新内容。
        var directory = Path.GetDirectoryName(targetPath) ?? rootDirectory;
        var tempPath = Path.Combine(
            directory,
            Path.GetFileName(targetPath) + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            File.WriteAllBytes(tempPath, document.RenderBytes());
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            TryDeleteFile(tempPath);

            // 目标原本不存在时，Move 失败若已留下半成品就删掉（回滚到"没有这个文件"）。
            var rolledBack = false;
            if (!fileExists)
            {
                rolledBack = TryDeleteFile(targetPath);
            }

            Log.Warn($"写入失败（{targetPath}）：{ex.Message}", ex);
            var backupNote = backupPath.Length > 0
                ? $"写前备份仍在：{backupPath}"
                : rolledBack ? "目标文件原本不存在，已回滚。" : string.Empty;
            return OperationResult<GenericConfigSaveResult>.Fail(
                backupNote.Length > 0
                    ? $"写入 {targetPath} 失败：{ex.Message}（{backupNote}）"
                    : $"写入 {targetPath} 失败：{ex.Message}",
                detail: "write-failed");
        }
        finally
        {
            TryDeleteFile(tempPath);
        }

        return OperationResult<GenericConfigSaveResult>.Ok(new GenericConfigSaveResult(
            targetPath,
            backupPath,
            changedLineCount,
            !fileExists));
    }

    private string CreateBackup(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileName(path);
        var stamp = _clock().ToString(TimestampFormat, CultureInfo.InvariantCulture);

        for (var attempt = 0; attempt <= MaxBackupAttempts; attempt++)
        {
            var suffix = attempt == 0
                ? string.Empty
                : "-" + attempt.ToString(CultureInfo.InvariantCulture);
            var candidate = Path.Combine(directory, fileName + ".bak-" + stamp + suffix);
            if (File.Exists(candidate))
            {
                continue;
            }

            File.Copy(path, candidate, overwrite: false);
            return candidate;
        }

        throw new IOException($"同一秒内已有 {MaxBackupAttempts + 1} 个同名备份，无法为 {fileName} 创建新备份。");
    }

    private bool SafeIsGameRunning(string instanceName, string gameDir)
    {
        if (_gameRunningProbe is null)
        {
            return false;
        }

        try
        {
            return _gameRunningProbe.IsGameRunning(instanceName ?? string.Empty, gameDir);
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            // 探测不到就按"没在运行"处理：宁可少拦截，不要挡住正常写盘。
            Log.Warn($"探测游戏运行状态失败：{ex.Message}", ex);
            return false;
        }
    }

    private string? ResolveGameDirectory(string gameDir, string instanceName)
    {
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            return gameDir.Trim();
        }

        if (_instances is null)
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(instanceName))
            {
                foreach (var instance in _instances.Instances)
                {
                    if (string.Equals(instance.Name, instanceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return instance.GameDir;
                    }
                }

                return null;
            }

            return _instances.Current?.GameDir;
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Log.Warn($"读取游戏实例失败：{ex.Message}", ex);
            return null;
        }
    }

    private static string? DetermineReadonlyReason(string path, ConfigNodeDocument document)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
            {
                return "文件带只读属性，写入前需要先去掉只读。";
            }
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Log.Warn($"读取文件属性失败（{path}）：{ex.Message}", ex);
            return $"读取文件属性失败：{ex.Message}";
        }

        if (document.HasParseErrors)
        {
            var first = document.ParseDiagnostics
                .FirstOrDefault(static diagnostic => diagnostic.Severity == CkanUI.Sfs.SfsSeverity.Error);
            return first is null
                ? $"ConfigNode 解析有 {document.ParseErrorCount} 个错误，重写会破坏原有内容。"
                : $"ConfigNode 解析有 {document.ParseErrorCount} 个错误（第 {first.Line} 行：{first.Message}），重写会破坏原有内容。";
        }

        return document.IsTextLossy
            ? "文件含非法 UTF-8 字节，重写会丢掉坏字节。"
            : null;
    }

    private static bool IsExpectedFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or System.Security.SecurityException;

    private static bool TryResolveGameDirectory(string gameDir, out string rootDirectory, out string error)
    {
        rootDirectory = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            error = "游戏目录为空。";
            return false;
        }

        try
        {
            rootDirectory = Path.GetFullPath(gameDir.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            error = $"游戏目录不合法：{gameDir}（{ex.Message}）";
            return false;
        }

        if (!Directory.Exists(rootDirectory))
        {
            error = $"游戏目录不存在：{gameDir}";
            return false;
        }

        return true;
    }

    private static bool TryResolveFile(string rootDirectory, string filePath, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "文件路径为空。";
            return false;
        }

        try
        {
            var trimmed = filePath.Trim();
            var candidate = Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(rootDirectory, trimmed);
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            error = $"文件路径不合法：{filePath}（{ex.Message}）";
            return false;
        }

        if (!IsInsideDirectory(fullPath, rootDirectory))
        {
            error = $"目标文件不在游戏目录内，已拒绝：{filePath}";
            return false;
        }

        return true;
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var prefix = directory + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRelativePath(string rootDirectory, string fullPath)
    {
        var gameDataDirectory = Path.Combine(rootDirectory, GenericConfigScanner.GameDataDirectoryName);
        var prefix = gameDataDirectory + Path.DirectorySeparatorChar;
        var baseDirectory = fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? gameDataDirectory
            : rootDirectory;
        return Path.GetRelativePath(baseDirectory, fullPath).Replace('\\', '/');
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Log.Warn($"删除临时文件失败（{path}）：{ex.Message}", ex);
            return false;
        }
    }
}
