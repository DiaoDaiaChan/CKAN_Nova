using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.ModConfig;

/// <summary>快照恢复结果：成功的原文件路径 + 逐个失败原因。</summary>
/// <param name="RestoredPaths">已恢复的原配置文件路径。</param>
/// <param name="Failures">失败项的中文说明（每项一句，含文件名与原因）。</param>
public sealed record ModConfigRestoreResult(
    IReadOnlyList<string> RestoredPaths,
    IReadOnlyList<string> Failures);

/// <summary>
/// .bak 快照恢复入口（CFG-04 的回滚动作）。
/// </summary>
/// <remarks>
/// 写盘层（IAdapterWriter）只写不还原：它负责写前快照（&lt;文件&gt;.ckan-ui-yyyyMMdd-HHmmss.bak）与
/// 写后自校验，失败时自己回滚；但"用户想把上一次写盘整个撤掉"需要页面侧提供一个显式入口，
/// 所以恢复逻辑单独抽成一个可替换的小服务（测试注入假实现，页面 VM 不直接碰 File）。
/// </remarks>
public interface IModConfigBackupRestorer
{
    /// <summary>把给定快照文件恢复到它们各自的原配置文件（原子替换）。</summary>
    /// <param name="backupPaths">写盘返回的快照路径集合（.bak）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<ModConfigRestoreResult>> RestoreAsync(
        IReadOnlyList<string> backupPaths,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 默认实现：从 &lt;文件&gt;.ckan-ui-yyyyMMdd-HHmmss[-N].bak 反推原文件路径，按字节原子写回。
/// </summary>
/// <remarks>
/// 与 AdapterWriter 的写盘手法一致：先在同目录写临时文件再 File.Move(overwrite)，避免写一半留下半个配置。
/// 单个文件失败不影响其它文件；全部失败返回失败结果，部分成功在 <see cref="ModConfigRestoreResult.Failures"/> 里逐条报。
/// </remarks>
public sealed class ModConfigBackupRestorer : IModConfigBackupRestorer
{
    private const string BackupMarker = ".ckan-ui-";

    /// <inheritdoc />
    public async Task<OperationResult<ModConfigRestoreResult>> RestoreAsync(
        IReadOnlyList<string> backupPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backupPaths);
        if (backupPaths.Count == 0)
        {
            return OperationResult<ModConfigRestoreResult>.Fail("没有可恢复的快照。");
        }

        var restored = new List<string>();
        var failures = new List<string>();
        foreach (var backupPath in backupPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryResolveOriginalPath(backupPath, out var originalPath))
            {
                failures.Add(DisplayName(backupPath) + "：快照文件名不是 <文件>.ckan-ui-时间戳.bak 的格式，无法定位原文件。");
                continue;
            }

            if (!File.Exists(backupPath))
            {
                failures.Add(DisplayName(originalPath) + "：快照文件已不存在，无法恢复。");
                continue;
            }

            try
            {
                await RestoreOneAsync(backupPath, originalPath, cancellationToken).ConfigureAwait(false);
                restored.Add(originalPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
            {
                failures.Add(DisplayName(originalPath) + "：恢复失败（" + ex.Message + "）");
            }
        }

        if (restored.Count == 0)
        {
            return OperationResult<ModConfigRestoreResult>.Fail("恢复快照失败，没有文件被还原。", string.Join("；", failures));
        }

        return OperationResult<ModConfigRestoreResult>.Ok(new ModConfigRestoreResult(restored, failures));
    }

    /// <summary>从快照文件名反推原配置文件路径（规则与 AdapterWriter 的写前快照命名一致）。</summary>
    /// <param name="backupPath">快照文件路径。</param>
    /// <param name="originalPath">反推出来的原文件路径（成功时）。</param>
    /// <returns>文件名符合 <c>&lt;文件&gt;.ckan-ui-yyyyMMdd-HHmmss[-N].bak</c> 时为 true。</returns>
    public static bool TryResolveOriginalPath(string? backupPath, out string originalPath)
    {
        originalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return false;
        }

        var fileName = Path.GetFileName(backupPath);
        if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stem = fileName[..^4];
        var marker = stem.LastIndexOf(BackupMarker, StringComparison.Ordinal);
        if (marker <= 0)
        {
            return false;
        }

        if (!IsSnapshotStamp(stem[(marker + BackupMarker.Length)..]))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(backupPath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        originalPath = Path.Combine(directory, stem[..marker]);
        return true;
    }

    private static async Task RestoreOneAsync(string backupPath, string originalPath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(backupPath, cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(originalPath) ?? Path.GetDirectoryName(backupPath) ?? ".";
        var tempPath = Path.Combine(directory, Path.GetFileName(originalPath) + ".restore-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, originalPath, overwrite: true);
        }
        finally
        {
            // 原子替换成功后临时文件已经被移走；中途失败时把残留清掉，别在游戏目录里留垃圾。
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool IsSnapshotStamp(string stamp)
    {
        // yyyyMMdd-HHmmss，重名时再带 -N（AdapterWriter 用 -2…-20 规避覆盖）。
        if (stamp.Length < 15 || stamp[8] != '-')
        {
            return false;
        }

        for (var index = 0; index < 15; index++)
        {
            if (index != 8 && !char.IsAsciiDigit(stamp[index]))
            {
                return false;
            }
        }

        if (stamp.Length == 15)
        {
            return true;
        }

        if (stamp[15] != '-')
        {
            return false;
        }

        for (var index = 16; index < stamp.Length; index++)
        {
            if (!char.IsAsciiDigit(stamp[index]))
            {
                return false;
            }
        }

        return stamp.Length > 16;
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
