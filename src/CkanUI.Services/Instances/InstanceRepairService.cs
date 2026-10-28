using System.Globalization;
using System.Text.Json;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Instances;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Instances;

/// <summary>
/// F-08 实例可靠性处置的实现：只读自检 + 受控的强制解锁 / 注册表隔离。
/// </summary>
/// <remarks>
/// <para>
/// 判据全部来自磁盘事实与上游 CKAN 的既有约定：锁就是 <c>CKAN/registry.locked</c>（上游
/// <c>RegistryManager.InstanceRegistryLockPath</c>），注册表就是 <c>CKAN/registry.json</c>，
/// 版本上限对应上游 <c>Registry.LATEST_REGISTRY_VERSION</c>（私有常量，这里显式复制成
/// <see cref="SupportedRegistryVersion"/>，上游升到 4 时必须同步改这里并让版本过旧用例跟着变）。
/// </para>
/// <para>
/// 破坏性动作只有两个，且都留痕：强制解锁会追加一行 JSONL 操作记录，隔离注册表只是改名
/// （内容一个字节都不动）。任何失败都以中文消息 + 可重试标记返回，不向界面抛异常。
/// </para>
/// </remarks>
public sealed class InstanceRepairService : IInstanceRepairService
{
    /// <summary>本程序支持的最高 registry_version（对应上游 Registry.LATEST_REGISTRY_VERSION = 3）。</summary>
    public const int SupportedRegistryVersion = 3;

    private const string CkanDirectoryName = "CKAN";
    private const string RegistryFileName = "registry.json";
    private const string LockFileName = "registry.locked";
    private const string CorruptTag = ".corrupt-";
    private const string JournalFileName = "instance-repair-journal.jsonl";
    private const string RequiredKspVersionProperty = "compatible_ksp_version";
    private const string RegistryVersionProperty = "registry_version";

    private static readonly JsonSerializerOptions JournalJsonOptions = new() { WriteIndented = false };

    private readonly log4net.ILog _log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);
    private readonly IGameProcessProbe _processProbe;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _journalPath;

    /// <summary>默认构造：真实进程探测 + 系统时钟 + 默认操作记录路径。</summary>
    public InstanceRepairService() : this(null, null, null) { }

    /// <summary>注入依赖，供隔离测试使用（探测替身、可控时钟、独立操作记录文件）。</summary>
    /// <param name="processProbe">进程探测端口；<c>null</c> 表示用真实实现。</param>
    /// <param name="clock">时钟；<c>null</c> 表示用系统时钟。</param>
    /// <param name="journalPath">操作记录文件路径；<c>null</c> 表示写 <see cref="DefaultJournalPath"/>。</param>
    public InstanceRepairService(IGameProcessProbe? processProbe = null,
        Func<DateTimeOffset>? clock = null, string? journalPath = null)
    {
        _processProbe = processProbe ?? new GameProcessProbe();
        _clock = clock ?? (static () => DateTimeOffset.Now);
        _journalPath = string.IsNullOrWhiteSpace(journalPath) ? DefaultJournalPath : journalPath;
    }

    /// <summary>默认操作记录路径：%LocalAppData%/CKAN/instance-repair-journal.jsonl。</summary>
    public static string DefaultJournalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKAN",
        JournalFileName);

    /// <inheritdoc />
    public Task<OperationResult<InstanceRepairDiagnosis>> DiagnoseAsync(
        InstanceRepairTarget target, CancellationToken ct = default) =>
        GuardAsync(token => Diagnose(target, token), ct, "实例自检失败。");

    /// <inheritdoc />
    public Task<OperationResult<InstanceUnlockReport>> ForceUnlockAsync(
        InstanceRepairTarget target, string? reason = null, CancellationToken ct = default) =>
        GuardAsync(token => ForceUnlock(target, reason, token), ct, "强制解锁实例失败。");

    /// <inheritdoc />
    public Task<OperationResult<InstanceRegistryQuarantine>> QuarantineRegistryAsync(
        InstanceRepairTarget target, CancellationToken ct = default) =>
        GuardAsync(token => QuarantineRegistry(target, token), ct, "隔离注册表失败。");

    /// <inheritdoc />
    public Task<OperationResult<InstanceCompatibilityInfo>> CheckCompatibilityAsync(
        InstanceRepairTarget target, CancellationToken ct = default) =>
        GuardAsync(token => CheckCompatibility(target, token), ct, "版本兼容判定失败。");

    private OperationResult<InstanceRepairDiagnosis> Diagnose(InstanceRepairTarget target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Prepare(target, out var paths, out var failure) is false)
        {
            return OperationResult<InstanceRepairDiagnosis>.Fail(failure!);
        }

        var issues = new List<InstanceIssue>();
        var gameDirExists = Directory.Exists(paths.GameDir);
        if (!gameDirExists)
        {
            issues.Add(new InstanceIssue(InstanceIssueKind.GameDirectoryMissing,
                "游戏目录不存在",
                $"实例「{paths.InstanceName}」配置的游戏目录「{paths.GameDir}」找不到，所有实例操作都会失败。",
                paths.GameDir,
                "检查移动硬盘/清理工具是否删过目录；实在找不回就在实例页移除这条记录。",
                false));
        }

        var isGameRunning = IsGameRunning(paths.GameDir);
        var locks = ProbeLocks(paths.CkanDirectory, paths.GameDir);
        if (locks.Count > 0)
        {
            var hint = locks[0].OccupantHint;
            var message = isGameRunning
                ? $"{locks.Count} 处锁痕迹存在，而且游戏进程正在运行，强制解锁已禁用。"
                : $"{locks.Count} 处锁痕迹存在，CKAN 会拒绝打开该实例的注册表。{hint}";
            issues.Add(new InstanceIssue(InstanceIssueKind.RegistryLocked,
                "实例被锁定",
                message,
                string.Join(Environment.NewLine, locks.Select(static artifact => artifact.Summary)),
                "确认没有其他 CKAN 进程在用该实例、且游戏已退出后，点「强制解锁」。",
                !isGameRunning));
        }

        var facts = ReadRegistryFacts(paths.RegistryPath);
        if (facts.Exists && !facts.Readable)
        {
            issues.Add(new InstanceIssue(InstanceIssueKind.RegistryCorrupt,
                "注册表无法解析",
                $"实例「{paths.InstanceName}」的 registry.json 读不成合法 JSON，CKAN 无法打开这个实例。",
                facts.Error,
                "点「隔离损坏注册表」把它改名成 registry.json.corrupt-时间戳（内容保留），CKAN 下次会重建空注册表。",
                true));
        }

        if (facts.Version is { } registryVersion && registryVersion > SupportedRegistryVersion)
        {
            issues.Add(new InstanceIssue(InstanceIssueKind.RegistryFromNewerCkan,
                "CKAN 版本过旧",
                $"该实例的注册表由更新版本的 CKAN 写入（registry_version={registryVersion}，本程序最高支持 {SupportedRegistryVersion}）。",
                paths.RegistryPath,
                "升级 CKAN 到最新版本后再打开该实例，否则读写注册表会失败。",
                false));
        }

        var requiredKsp = facts.RequiredKspVersion;
        if (!string.IsNullOrWhiteSpace(requiredKsp) && !string.IsNullOrWhiteSpace(target.GameVersion)
            && CompareDotted(target.GameVersion, requiredKsp) < 0)
        {
            issues.Add(new InstanceIssue(InstanceIssueKind.GameVersionTooOld,
                "游戏版本过旧",
                $"注册表声明兼容 KSP {requiredKsp}，而实例当前是 {target.GameVersion}，模组装上去会和游戏版本不匹配。",
                paths.RegistryPath,
                $"把游戏升级到 {requiredKsp} 或更新版本后再操作；不打算升级就别在这个实例上装模组。",
                false));
        }

        _log.InfoFormat("实例自检完成：{0}，问题 {1} 个，锁 {2} 处", paths.InstanceName, issues.Count, locks.Count);

        return OperationResult<InstanceRepairDiagnosis>.Ok(new InstanceRepairDiagnosis(
            paths.InstanceName,
            paths.GameDir,
            paths.CkanDirectory,
            paths.RegistryPath,
            gameDirExists,
            facts.Exists,
            facts.Readable,
            facts.Error,
            facts.Version,
            SupportedRegistryVersion,
            target.GameVersion,
            requiredKsp,
            isGameRunning,
            locks,
            issues));
    }

    private OperationResult<InstanceUnlockReport> ForceUnlock(InstanceRepairTarget target, string? reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Prepare(target, out var paths, out var failure) is false)
        {
            return OperationResult<InstanceUnlockReport>.Fail(failure!);
        }

        if (!Directory.Exists(paths.CkanDirectory))
        {
            return OperationResult<InstanceUnlockReport>.Fail(
                $"实例「{paths.InstanceName}」的 CKAN 目录不存在：「{paths.CkanDirectory}」。");
        }

        var locks = ProbeLocks(paths.CkanDirectory, paths.GameDir);
        if (locks.Count == 0)
        {
            return OperationResult<InstanceUnlockReport>.Fail(
                $"实例「{paths.InstanceName}」当前没有被锁定，无需解锁。");
        }

        if (IsGameRunning(paths.GameDir))
        {
            return OperationResult<InstanceUnlockReport>.Fail(
                $"游戏正在运行，无法解锁实例「{paths.InstanceName}」。请先退出游戏再重试。", null, true);
        }

        var removed = new List<string>();
        var skipped = new List<string>();
        foreach (var artifact in locks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (artifact.Kind == InstanceLockKind.RegistryLockDirectory)
                {
                    if (Directory.EnumerateFileSystemEntries(artifact.Path).Any())
                    {
                        skipped.Add(artifact.Path);
                        continue;
                    }

                    Directory.Delete(artifact.Path, false);
                }
                else
                {
                    File.Delete(artifact.Path);
                }

                removed.Add(artifact.Path);
            }
            catch (Exception ex)
            {
                _log.Warn($"删除锁失败：{artifact.Path}", ex);
                var detail = removed.Count == 0
                    ? ex.Message
                    : $"已删除：{string.Join("、", removed)}；失败于 {artifact.Path}：{ex.Message}";
                return OperationResult<InstanceUnlockReport>.Fail(
                    $"删除锁文件失败，实例「{paths.InstanceName}」仍处于锁定状态。", detail, true);
            }
        }

        if (removed.Count == 0)
        {
            return OperationResult<InstanceUnlockReport>.Fail(
                $"锁路径都删不掉，实例「{paths.InstanceName}」仍处于锁定状态。",
                $"跳过的路径：{string.Join("、", skipped)}（非空锁目录不做递归删除，请人工确认内容）。",
                false);
        }

        var timestamp = _clock();
        var journal = AppendJournal("force-unlock", paths, reason, removed, timestamp);
        _log.InfoFormat("强制解锁实例 {0}：删除 {1} 处锁，记录写入 {2}", paths.InstanceName, removed.Count, journal.Path);

        var message = skipped.Count == 0
            ? $"已删除 {removed.Count} 处锁痕迹，实例「{paths.InstanceName}」可以重新打开了。操作记录：{journal.Path}"
            : $"已删除 {removed.Count} 处锁痕迹，还有 {skipped.Count} 处没删（非空锁目录），操作记录：{journal.Path}";

        return OperationResult<InstanceUnlockReport>.Ok(new InstanceUnlockReport(
            removed, skipped, journal.Path, journal.Written, timestamp, message));
    }

    private OperationResult<InstanceRegistryQuarantine> QuarantineRegistry(InstanceRepairTarget target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Prepare(target, out var paths, out var failure) is false)
        {
            return OperationResult<InstanceRegistryQuarantine>.Fail(failure!);
        }

        if (!File.Exists(paths.RegistryPath))
        {
            return OperationResult<InstanceRegistryQuarantine>.Fail(
                $"实例「{paths.InstanceName}」没有 registry.json，无需隔离。");
        }

        var facts = ReadRegistryFacts(paths.RegistryPath);
        if (facts.Readable)
        {
            return OperationResult<InstanceRegistryQuarantine>.Fail(
                $"实例「{paths.InstanceName}」的 registry.json 可以正常解析，不需要隔离。");
        }

        long bytes;
        try
        {
            bytes = new FileInfo(paths.RegistryPath).Length;
        }
        catch (Exception)
        {
            bytes = 0;
        }

        var timestamp = _clock();
        var quarantinePath = BuildQuarantinePath(paths.RegistryPath, timestamp);
        try
        {
            File.Move(paths.RegistryPath, quarantinePath);
        }
        catch (Exception ex)
        {
            _log.Warn($"隔离注册表失败：{paths.RegistryPath} -> {quarantinePath}", ex);
            return OperationResult<InstanceRegistryQuarantine>.Fail(
                $"隔离实例「{paths.InstanceName}」的注册表失败，文件没有改动。", ex.ToString(), true);
        }

        var journal = AppendJournal("quarantine-registry", paths, facts.Error,
            new[] { paths.RegistryPath, quarantinePath }, timestamp);
        _log.InfoFormat("已隔离损坏注册表：{0} -> {1}", paths.RegistryPath, quarantinePath);

        var message = $"已把损坏的注册表隔离为「{Path.GetFileName(quarantinePath)}」（内容原样保留），"
                      + "CKAN 下次打开该实例时会自动创建空注册表。操作记录：" + journal.Path;

        return OperationResult<InstanceRegistryQuarantine>.Ok(new InstanceRegistryQuarantine(
            paths.RegistryPath, quarantinePath, bytes, timestamp, journal.Path, journal.Written, message));
    }

    private OperationResult<InstanceCompatibilityInfo> CheckCompatibility(InstanceRepairTarget target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Prepare(target, out var paths, out var failure) is false)
        {
            return OperationResult<InstanceCompatibilityInfo>.Fail(failure!);
        }

        var facts = ReadRegistryFacts(paths.RegistryPath);
        var ckanTooOld = facts.Version is { } version && version > SupportedRegistryVersion;
        var requiredKsp = facts.RequiredKspVersion;
        var gameTooOld = !string.IsNullOrWhiteSpace(requiredKsp)
                         && !string.IsNullOrWhiteSpace(target.GameVersion)
                         && CompareDotted(target.GameVersion, requiredKsp) < 0;

        var advice = ComposeAdvice(paths.InstanceName, target.GameVersion, requiredKsp,
            facts.Version, ckanTooOld, gameTooOld);

        return OperationResult<InstanceCompatibilityInfo>.Ok(new InstanceCompatibilityInfo(
            paths.InstanceName, target.GameVersion, requiredKsp, facts.Version,
            SupportedRegistryVersion, ckanTooOld, gameTooOld, advice));
    }

    private static string ComposeAdvice(string instanceName, string? kspVersion, string? requiredKsp,
        int? registryVersion, bool ckanTooOld, bool gameTooOld)
    {
        if (ckanTooOld && gameTooOld)
        {
            return $"实例「{instanceName}」需要同时升级 CKAN 和游戏：注册表版本 {registryVersion} 高于本程序支持的 "
                   + $"{SupportedRegistryVersion}；注册表声明兼容 KSP {requiredKsp}，实例当前是 {kspVersion}。"
                   + "请先升级 CKAN，再把游戏升到声明版本，然后重新打开该实例。";
        }

        if (ckanTooOld)
        {
            return $"实例「{instanceName}」的注册表由更新版本的 CKAN 写入（registry_version={registryVersion}，"
                   + $"本程序最高支持 {SupportedRegistryVersion}）。请升级 CKAN 到最新版本后再打开该实例，"
                   + "否则无法安全读写注册表。";
        }

        if (gameTooOld)
        {
            return $"实例「{instanceName}」的注册表声明兼容 KSP {requiredKsp}，而当前实例是 {kspVersion}。"
                   + $"请把游戏升级到 {requiredKsp} 或更新版本后再操作，否则模组会装到版本不匹配的游戏上。";
        }

        return string.Empty;
    }

    private bool Prepare(InstanceRepairTarget target, out RepairPaths paths, out string? failure)
    {
        paths = default!;
        failure = null;
        if (target is null)
        {
            failure = "必须提供要自检的实例。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(target.GameDir))
        {
            failure = "实例没有配置游戏目录，无法自检。";
            return false;
        }

        var instanceName = string.IsNullOrWhiteSpace(target.InstanceName) ? "未命名实例" : target.InstanceName;
        try
        {
            var gameDir = Path.GetFullPath(target.GameDir);
            var ckanDir = Path.Combine(gameDir, CkanDirectoryName);
            paths = new RepairPaths(instanceName, gameDir, ckanDir, Path.Combine(ckanDir, RegistryFileName));
            return true;
        }
        catch (Exception ex)
        {
            failure = $"实例「{instanceName}」的游戏目录路径无效：{ex.Message}";
            return false;
        }
    }

    private IReadOnlyList<InstanceLockArtifact> ProbeLocks(string ckanDirectory, string gameDir)
    {
        var lockPath = Path.Combine(ckanDirectory, LockFileName);
        InstanceLockKind kind;
        try
        {
            if (File.Exists(lockPath))
            {
                kind = InstanceLockKind.RegistryLockFile;
            }
            else if (Directory.Exists(lockPath))
            {
                kind = InstanceLockKind.RegistryLockDirectory;
            }
            else
            {
                return Array.Empty<InstanceLockArtifact>();
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"探测锁路径失败：{lockPath}", ex);
            return Array.Empty<InstanceLockArtifact>();
        }

        var hint = BuildOccupantHint(gameDir);
        return new[] { new InstanceLockArtifact(lockPath, kind, TryLastWriteUtc(lockPath), hint) };
    }

    private string BuildOccupantHint(string gameDir)
    {
        IReadOnlyList<string> images;
        try
        {
            images = _processProbe.GetProcessImagePaths();
        }
        catch (Exception)
        {
            images = Array.Empty<string>();
        }

        var ckanProcess = images.FirstOrDefault(static path => IsCkanProcessName(Path.GetFileNameWithoutExtension(path)));
        if (ckanProcess is not null)
        {
            return $"占用者疑似 CKAN 进程「{Path.GetFileNameWithoutExtension(ckanProcess)}」：{ckanProcess}";
        }

        var gameProcess = images.FirstOrDefault(path => GameProcessProbe.IsInsideGameDir(path, gameDir));
        if (gameProcess is not null)
        {
            return $"占用者疑似游戏进程「{Path.GetFileName(gameProcess)}」：{gameProcess}";
        }

        return "未定位到占用进程（更可能是上次异常退出留下的残留锁），只保留路径与最后写入时间";
    }

    private static bool IsCkanProcessName(string processName) =>
        processName.StartsWith("ckan", StringComparison.OrdinalIgnoreCase);

    private bool IsGameRunning(string gameDir)
    {
        try
        {
            return _processProbe.GetProcessImagePaths()
                .Any(path => GameProcessProbe.IsInsideGameDir(path, gameDir));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DateTimeOffset? TryLastWriteUtc(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            }

            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static RegistryFacts ReadRegistryFacts(string registryPath)
    {
        string text;
        try
        {
            if (!File.Exists(registryPath))
            {
                return new RegistryFacts(false, false, null, null, null);
            }

            text = File.ReadAllText(registryPath);
        }
        catch (Exception ex)
        {
            return new RegistryFacts(true, false, ex.Message, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new RegistryFacts(true, false, "registry.json 顶层不是 JSON 对象。", null, null);
            }

            int? version = null;
            if (root.TryGetProperty(RegistryVersionProperty, out var versionElement)
                && versionElement.ValueKind == JsonValueKind.Number
                && versionElement.TryGetInt32(out var parsedVersion))
            {
                version = parsedVersion;
            }

            string? requiredKsp = null;
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, RequiredKspVersionProperty, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    requiredKsp = property.Value.GetString();
                    break;
                }
            }

            return new RegistryFacts(true, true, null, version, requiredKsp);
        }
        catch (JsonException ex)
        {
            return new RegistryFacts(true, false, ex.Message, null, null);
        }
    }

    private (string Path, bool Written) AppendJournal(string action, RepairPaths paths, string? reason,
        IReadOnlyList<string> affectedPaths, DateTimeOffset timestamp)
    {
        var entry = new RepairJournalEntry(timestamp, action, paths.InstanceName, paths.GameDir, reason, affectedPaths);
        try
        {
            var directory = Path.GetDirectoryName(_journalPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(_journalPath, JsonSerializer.Serialize(entry, JournalJsonOptions) + Environment.NewLine);
            return (_journalPath, true);
        }
        catch (Exception ex)
        {
            _log.Warn($"写实例修复操作记录失败：{_journalPath}", ex);
            return (_journalPath, false);
        }
    }

    private static string BuildQuarantinePath(string registryPath, DateTimeOffset timestamp)
    {
        var stamp = timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = registryPath + CorruptTag + stamp;
        var index = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = registryPath + CorruptTag + stamp + "-" + index.ToString(CultureInfo.InvariantCulture);
            index++;
        }

        return candidate;
    }

    private static int CompareDotted(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        var leftParts = ParseVersionComponents(left);
        var rightParts = ParseVersionComponents(right);
        var length = Math.Max(leftParts.Count, rightParts.Count);
        for (var index = 0; index < length; index++)
        {
            var leftValue = index < leftParts.Count ? leftParts[index] : 0;
            var rightValue = index < rightParts.Count ? rightParts[index] : 0;
            if (leftValue != rightValue)
            {
                return leftValue < rightValue ? -1 : 1;
            }
        }

        return 0;
    }

    private static IReadOnlyList<int> ParseVersionComponents(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var components = new List<int>();
        foreach (var part in text.Split(new[] { '.', '-', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                break;
            }

            components.Add(number);
        }

        return components;
    }

    private static async Task<OperationResult<T>> GuardAsync<T>(Func<CancellationToken, OperationResult<T>> action,
        CancellationToken ct, string failureMessage)
    {
        try
        {
            return await Task.Run(() => action(ct), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            return OperationResult<T>.Fail("操作已取消。", ex.Message, true);
        }
        catch (Exception ex)
        {
            return OperationResult<T>.Fail(failureMessage, ex.ToString(), true);
        }
    }

    /// <summary>一次自检要用到的三条绝对路径。</summary>
    private sealed record RepairPaths(string InstanceName, string GameDir, string CkanDirectory, string RegistryPath);

    /// <summary>registry.json 的只读事实（存在性、可解析性、版本、声明的兼容 KSP 版本）。</summary>
    private sealed record RegistryFacts(bool Exists, bool Readable, string? Error, int? Version, string? RequiredKspVersion);

    /// <summary>操作记录里的一行（JSONL）。</summary>
    private sealed record RepairJournalEntry(
        DateTimeOffset Timestamp,
        string Action,
        string Instance,
        string GameDir,
        string? Reason,
        IReadOnlyList<string> Paths);
}
