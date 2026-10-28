using System.Globalization;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 写盘唯一入口（CFG-05，§3 / R-35）。
/// <para>
/// 顺序是硬约束，任何一步失败都必须**没有副作用**或**已回滚**：
/// 校验 → 快照（备份原件）→ 原子替换（同目录临时文件 + 覆盖改名）→ 写后重新解析自校验 → 失败回滚。
/// 之所以要"写后重新解析"：磁盘写入成功不代表内容还是我们以为的那份
/// （编码、截断、被别的进程踩踏都可能发生），只有当重新解析出来的值等于期望值才算真的写成功。
/// </para>
/// </summary>
public sealed class AdapterWriter : IAdapterWriter
{
    /// <summary>备份文件名里的时间戳格式（与 Saves/SaveFileWriter 的快照命名风格保持一致）。</summary>
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    /// <summary>最多尝试的备份序号，避免同名备份覆盖掉真正的原件。</summary>
    private const int MaxBackupAttempts = 20;

    private readonly IAdapterCatalog _catalog;
    private readonly IGameRunningProbe _gameRunningProbe;
    private readonly Func<DateTime> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>构造。</summary>
    /// <param name="catalog">适配器目录（用于校验键名与文件声明）。</param>
    /// <param name="gameRunningProbe">游戏运行探测；为 null 时用真实实现。</param>
    /// <param name="clock">时间源（测试可注入固定时间，保证备份名可预期）。</param>
    public AdapterWriter(
        IAdapterCatalog catalog,
        IGameRunningProbe? gameRunningProbe = null,
        Func<DateTime>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _gameRunningProbe = gameRunningProbe ?? new InstanceGameRunningProbe();
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AdapterWriteResult>> WriteAsync(
        AdapterWriteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 同一进程内串行化：两个页面同时按"写入"会互相踩备份与临时文件。
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return WriteCore(request, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private OperationResult<AdapterWriteResult> WriteCore(AdapterWriteRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (request.Changes.Count == 0)
        {
            return OperationResult<AdapterWriteResult>.Fail("没有要写入的变更。");
        }

        var adapter = FindAdapter(request.AdapterId);
        if (adapter is null)
        {
            return OperationResult<AdapterWriteResult>.Fail(
                $"找不到 id 为「{request.AdapterId}」的适配器，请刷新适配器列表后重试。");
        }

        if (string.IsNullOrWhiteSpace(request.GameDir) || !Directory.Exists(request.GameDir))
        {
            return OperationResult<AdapterWriteResult>.Fail($"实例游戏目录不存在：{request.GameDir}");
        }

        var gameDir = NormalizePath(request.GameDir);

        if (_gameRunningProbe.IsGameRunning(request.InstanceName, gameDir) && !request.ConfirmedGameRunning)
        {
            // §7 Q1=(b)：运行中允许写，但必须二次确认，且要告知"需要重启 KSP 才生效"。
            return OperationResult<AdapterWriteResult>.Fail(
                "KSP 正在运行。写入配置需要重启 KSP 才生效，且可能与游戏自身的写盘竞争；请先关闭 KSP，或在提示里确认后再写入。",
                detail: "game-running",
                retryable: true);
        }

        var plan = new List<FilePlan>();
        foreach (var change in request.Changes)
        {
            if (string.IsNullOrWhiteSpace(change.Key))
            {
                return OperationResult<AdapterWriteResult>.Fail("变更里的键名不能为空。");
            }

            if (change.NewValue is null)
            {
                return OperationResult<AdapterWriteResult>.Fail($"键「{change.Key}」的新值为空。");
            }

            if (change.NewValue.Contains('\n') || change.NewValue.Contains('\r'))
            {
                return OperationResult<AdapterWriteResult>.Fail($"键「{change.Key}」的新值不能包含换行。");
            }

            var targetPath = NormalizePath(change.FilePath);
            if (!IsInsideDirectory(targetPath, gameDir))
            {
                return OperationResult<AdapterWriteResult>.Fail(
                    $"目标文件不在实例游戏目录内，已拒绝写入：{change.FilePath}");
            }

            var file = FindFile(adapter, targetPath, gameDir);
            if (file is null)
            {
                return OperationResult<AdapterWriteResult>.Fail(
                    $"适配器「{adapter.Id}」没有声明文件 {targetPath}，拒绝写入未知文件。");
            }

            if (!file.IsWritable)
            {
                var reason = string.IsNullOrWhiteSpace(file.ReadonlyReason)
                    ? "该文件在适配器里被声明为只读"
                    : file.ReadonlyReason;
                return OperationResult<AdapterWriteResult>.Fail($"文件 {file.Path} 不可写：{reason}");
            }

            var field = FindField(adapter, file, change.Key);
            if (field is null)
            {
                return OperationResult<AdapterWriteResult>.Fail(
                    $"适配器「{adapter.Id}」没有声明键「{change.Key}」，拒绝写入未知键。");
            }

            if (!ModConfigDetector.ValidateFieldValue(field, change.NewValue, out var valueError))
            {
                return OperationResult<AdapterWriteResult>.Fail(valueError ?? $"键「{change.Key}」的值不合法。");
            }

            if (field.Risk == ModConfigFieldRisk.High && !request.ConfirmedRiskHigh)
            {
                return OperationResult<AdapterWriteResult>.Fail(
                    $"键「{change.Key}」是高风险字段（可能影响存档或性能），需要二次确认后再写入。",
                    detail: "risk-high",
                    retryable: true);
            }

            var existing = plan.Find(entry => string.Equals(entry.FilePath, targetPath, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new FilePlan(file, targetPath);
                plan.Add(existing);
            }

            existing.Changes.Add((field, change.NewValue));
        }

        var applied = new List<ModConfigAppliedChange>();
        var backups = new List<string>();
        var requiresRestart = 0;

        foreach (var entry in plan)
        {
            var outcome = ApplyToFile(adapter, entry, gameDir, ct);
            if (!outcome.IsSuccess)
            {
                // 已经写好的前几个文件不回滚：它们各自都是完整成功的（原子写 + 自校验通过），
                // 这里只保证"失败的那个文件保持原状"，并把已完成部分原样报告给调用方。
                return OperationResult<AdapterWriteResult>.Fail(
                    outcome.Message ?? "写入失败。",
                    detail: string.Join("; ", backups),
                    retryable: outcome.Retryable);
            }

            var result = outcome.Value!;
            applied.AddRange(result.Changes);
            if (result.BackupPath is not null)
            {
                backups.Add(result.BackupPath);
            }

            requiresRestart += result.RequiresRestartCount;
        }

        return OperationResult<AdapterWriteResult>.Ok(new AdapterWriteResult(applied, backups, requiresRestart));
    }

    private OperationResult<FileApplication> ApplyToFile(
        ModConfigAdapterDescription adapter,
        FilePlan plan,
        string gameDir,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var file = plan.File;
        var targetPath = plan.FilePath;
        var existed = File.Exists(targetPath);

        ConfigNodeDocument document;
        if (existed)
        {
            try
            {
                document = ConfigNodeDocument.ParseFile(targetPath);
            }
            catch (IOException ex)
            {
                return OperationResult<FileApplication>.Fail($"读取 {file.Path} 失败：{ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return OperationResult<FileApplication>.Fail($"读取 {file.Path} 被拒绝：{ex.Message}");
            }

            if (document.IsTextLossy)
            {
                // 非 UTF-8 文件按 UTF-8 重写会静默破坏内容，这里宁可不写。
                return OperationResult<FileApplication>.Fail(
                    $"{file.Path} 不是合法的 UTF-8 文本，为避免写坏已取消写入。");
            }
        }
        else
        {
            var created = TryCreateFromTemplate(adapter, file, targetPath, out var createError, out document);
            if (!created)
            {
                return OperationResult<FileApplication>.Fail(createError ?? $"{file.Path} 不存在，且无法从模板创建。");
            }
        }

        var nodePath = ResolveNodePath(file);
        var applied = new List<ModConfigAppliedChange>();
        var requiresRestart = 0;

        foreach (var (field, newValue) in plan.Changes)
        {
            var before = document.FindPair(field.Key, nodePath);
            if (before is null)
            {
                return OperationResult<FileApplication>.Fail(
                    $"{file.Path} 里找不到键「{field.Key}」（节点 {DescribeNodePath(nodePath)}），已取消写入。");
            }

            if (!document.TrySetValue(field.Key, newValue, nodePath, out var setError))
            {
                return OperationResult<FileApplication>.Fail(setError ?? $"改写键「{field.Key}」失败。");
            }

            applied.Add(new ModConfigAppliedChange(targetPath, field.Key, before.Value, newValue));
            if (field.RequiresRestart)
            {
                requiresRestart++;
            }
        }

        if (applied.Count == 0)
        {
            return OperationResult<FileApplication>.Ok(new FileApplication([], null, 0));
        }

        string? backupPath = null;
        byte[] originalBytes = [];
        if (existed)
        {
            try
            {
                originalBytes = File.ReadAllBytes(targetPath);
                backupPath = CreateSnapshot(targetPath);
            }
            catch (IOException ex)
            {
                return OperationResult<FileApplication>.Fail($"创建 {file.Path} 的快照失败：{ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return OperationResult<FileApplication>.Fail($"创建 {file.Path} 的快照被拒绝：{ex.Message}");
            }
        }

        byte[] newBytes;
        try
        {
            newBytes = document.RenderBytes();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return OperationResult<FileApplication>.Fail($"生成 {file.Path} 的新内容失败：{ex.Message}");
        }

        try
        {
            WriteAtomic(targetPath, newBytes);
        }
        catch (IOException ex)
        {
            return OperationResult<FileApplication>.Fail($"写入 {file.Path} 失败：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<FileApplication>.Fail($"写入 {file.Path} 被拒绝：{ex.Message}");
        }

        // 写后自校验：重新从磁盘解析，逐个确认值确实变成了新值。
        if (!VerifyWritten(targetPath, plan.Changes, nodePath, out var verifyError))
        {
            var rolledBack = Rollback(targetPath, existed, originalBytes);
            return OperationResult<FileApplication>.Fail(
                rolledBack
                    ? $"{verifyError}（已回滚到写前状态）"
                    : $"{verifyError}，且回滚失败，请从快照恢复：{backupPath}");
        }

        return OperationResult<FileApplication>.Ok(new FileApplication(applied, backupPath, requiresRestart));
    }

    private bool TryCreateFromTemplate(
        ModConfigAdapterDescription adapter,
        ModConfigFileDescription file,
        string targetPath,
        out string? error,
        out ConfigNodeDocument document)
    {
        error = null;
        document = null!;

        if (file.OnMissing != ModConfigMissingFilePolicy.CreateFromTemplate || string.IsNullOrWhiteSpace(file.Template))
        {
            error = $"{file.Path} 不存在，且适配器没有为它提供模板";
            return false;
        }

        var adapterDirectory = adapter.SourcePath is null
            ? null
            : Path.GetDirectoryName(adapter.SourcePath);
        if (adapterDirectory is null)
        {
            error = $"{file.Path} 不存在，且无法定位适配器目录来读取模板";
            return false;
        }

        var templatePath = Path.Combine(adapterDirectory, file.Template);
        if (!File.Exists(templatePath))
        {
            error = $"{file.Path} 不存在，模板 {file.Template} 也找不到";
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(templatePath);
        }
        catch (IOException ex)
        {
            error = $"读取模板 {file.Template} 失败：{ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"读取模板 {file.Template} 被拒绝：{ex.Message}";
            return false;
        }

        document = ConfigNodeDocument.ParseBytes(bytes, targetPath);
        if (document.HasParseErrors)
        {
            error = $"模板 {file.Template} 本身无法解析，拒绝用坏模板创建配置";
            return false;
        }

        return true;
    }

    private static bool VerifyWritten(
        string path,
        IReadOnlyList<(ModConfigFieldDescription Field, string NewValue)> changes,
        string nodePath,
        out string? error)
    {
        error = null;
        ConfigNodeDocument check;
        try
        {
            check = ConfigNodeDocument.ParseFile(path);
        }
        catch (IOException ex)
        {
            error = $"写后重新读取失败：{ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"写后重新读取被拒绝：{ex.Message}";
            return false;
        }

        foreach (var (field, newValue) in changes)
        {
            var pair = check.FindPair(field.Key, nodePath);
            if (pair is null)
            {
                error = $"写后重新解析找不到键「{field.Key}」";
                return false;
            }

            if (!string.Equals(Unquote(pair.Value), newValue, StringComparison.Ordinal))
            {
                error = $"写后重新解析得到「{pair.Value}」，期望「{newValue}」";
                return false;
            }
        }

        return true;
    }

    private static bool Rollback(string path, bool existed, byte[] originalBytes)
    {
        try
        {
            if (existed)
            {
                WriteAtomic(path, originalBytes);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string CreateSnapshot(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var fileName = Path.GetFileName(path);
        var stamp = _clock().ToString(TimestampFormat, CultureInfo.InvariantCulture);

        for (var attempt = 1; attempt <= MaxBackupAttempts; attempt++)
        {
            // 快照只增不改：重名就换序号，绝不覆盖上一份原件。
            var candidate = attempt == 1
                ? Path.Combine(directory, fileName + ".ckan-ui-" + stamp + ".bak")
                : Path.Combine(directory, fileName + ".ckan-ui-" + stamp + "-" + attempt.ToString(CultureInfo.InvariantCulture) + ".bak");

            if (File.Exists(candidate))
            {
                continue;
            }

            File.Copy(path, candidate, overwrite: false);
            return candidate;
        }

        throw new IOException($"无法为 {fileName} 创建不重名的快照");
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var temp = Path.Combine(
            directory,
            Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            // 同目录写临时文件再改名：跨卷改名不是原子的，同目录才是（T-CFG-07）。
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // 清理残留临时文件失败不影响已经完成的写入。
                }
            }
        }
    }

    private ModConfigAdapterDescription? FindAdapter(string? adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            return null;
        }

        foreach (var adapter in _catalog.Adapters)
        {
            if (string.Equals(adapter.Id, adapterId, StringComparison.OrdinalIgnoreCase))
            {
                return adapter;
            }
        }

        return null;
    }

    /// <summary>
    /// 在 adapter 声明的文件里找目标文件。
    /// </summary>
    /// <remarks>
    /// adapter 里的 path 一律是"相对游戏目录"的写法（与读侧 ModConfigService 的解析一致），
    /// 所以必须先和 gameDir 拼成绝对路径再比较；否则相对路径被 Path.GetFullPath 解析到进程当前目录，
    /// 永远匹配不上真实目标文件，所有写入都会被判成"未声明文件"。
    /// </remarks>
    private static ModConfigFileDescription? FindFile(
        ModConfigAdapterDescription adapter,
        string targetPath,
        string gameDir)
    {
        foreach (var file in adapter.Files)
        {
            var declared = NormalizePath(
                Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(gameDir, file.Path));
            if (string.Equals(declared, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private static ModConfigFieldDescription? FindField(
        ModConfigAdapterDescription adapter,
        ModConfigFileDescription file,
        string key)
    {
        foreach (var field in adapter.Fields)
        {
            if (field.FileIndex == file.Index && string.Equals(field.Key, key, StringComparison.Ordinal))
            {
                return field;
            }
        }

        foreach (var field in adapter.Fields)
        {
            if (field.FileIndex == file.Index && string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>node 布局要限定在声明的根节点里；flat 布局的键值对在根层（nodePath 为空串）。</summary>
    private static string ResolveNodePath(ModConfigFileDescription file)
        => file.Layout == ModConfigFileLayout.Node ? file.RootNode ?? string.Empty : string.Empty;

    private static string DescribeNodePath(string nodePath)
        => nodePath.Length == 0 ? "根层" : $"{nodePath}";

    /// <summary>去掉本写盘器自己加的引号（只剥一层，不处理转义，因为写入时从不加转义）。</summary>
    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        if (string.Equals(path, directory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = directory + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>单个文件的写入计划。</summary>
    private sealed class FilePlan
    {
        public FilePlan(ModConfigFileDescription file, string filePath)
        {
            File = file;
            FilePath = filePath;
        }

        public ModConfigFileDescription File { get; }

        public string FilePath { get; }

        public List<(ModConfigFieldDescription Field, string NewValue)> Changes { get; } = [];
    }

    /// <summary>单个文件写完后的结果。</summary>
    private sealed record FileApplication(
        IReadOnlyList<ModConfigAppliedChange> Changes,
        string? BackupPath,
        int RequiresRestartCount);
}
