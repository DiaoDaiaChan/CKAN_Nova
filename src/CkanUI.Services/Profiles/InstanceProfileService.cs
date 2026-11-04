using System.Globalization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Profiles;

/// <summary>
/// 实例配置档服务：用同卷目录换位让一个游戏安装承载多套互不干扰的 GameData/CKAN 状态。
/// </summary>
/// <remarks>
/// <para>
/// 目录布局：<c>&lt;GameDir&gt;/GameData</c>、<c>&lt;GameDir&gt;/CKAN</c> 是**当前生效**的那一份；
/// 其余配置档放在 <c>&lt;GameDir&gt;/CKANProfiles/&lt;名字&gt;/{GameData,CKAN}</c>。切换只做
/// <see cref="Directory.Move(string,string)"/>（同卷 rename），不复制任何游戏文件。
/// </para>
/// <para>
/// 安全边界（与接口注释一致）：写前查游戏进程；换位前先 <see cref="IRegistryService.CloseAsync"/>；
/// 名字做路径穿越校验；换位失败按相反顺序回滚，宁可回到原状也不留半个配置档。
/// 所有公开方法把异常收敛成 <see cref="OperationResult"/>.Fail，不向界面抛。
/// </para>
/// </remarks>
public sealed class InstanceProfileService : IInstanceProfileService
{
    private const string GameDataName = "GameData";
    private const string CkanDirectoryName = "CKAN";
    private const int MaxProfileNameLength = 64;
    private const string InvalidNameChars = "/\\:*?\"<>|";

    private readonly log4net.ILog _log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);
    private readonly IInstanceService _instances;
    private readonly IRegistryService? _registry;

    /// <summary>注入实例解析与注册表服务；<paramref name="registry"/> 允许为 null（headless 测试只验目录换位）。</summary>
    public InstanceProfileService(IInstanceService instances, IRegistryService? registry = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _registry = registry;
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<InstanceProfileInfo>>> ListAsync(
        string instanceName, CancellationToken ct = default) =>
        GuardAsync(() => List(instanceName, ct), "读取配置档列表失败。");

    /// <inheritdoc />
    public Task<OperationResult<InstanceProfileInfo>> CreateAsync(
        string instanceName,
        string profileName,
        bool adoptCurrent = false,
        string? source = null,
        CancellationToken ct = default) =>
        GuardAsync(() => Create(instanceName, profileName, adoptCurrent, source, ct), "新建配置档失败。");

    /// <inheritdoc />
    public Task<OperationResult<InstanceProfileSwitchResult>> SwitchAsync(
        string instanceName, string profileName, CancellationToken ct = default) =>
        GuardAsync(() => Switch(instanceName, profileName, ct), "切换配置档失败。");

    /// <inheritdoc />
    public Task<OperationResult<bool>> DeleteAsync(
        string instanceName, string profileName, CancellationToken ct = default) =>
        GuardAsync(() => Delete(instanceName, profileName, ct), "删除配置档失败。");

    /// <inheritdoc />
    public Task<OperationResult<string?>> GetLaunchProfileAsync(
        string instanceName, CancellationToken ct = default) =>
        GuardAsync(() => GetLaunchProfile(instanceName, ct), "读取启动配置档失败。");

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetLaunchProfileAsync(
        string instanceName, string? profileName, CancellationToken ct = default) =>
        GuardAsync(() => SetLaunchProfile(instanceName, profileName, ct), "设置启动配置档失败。");

    /// <inheritdoc />
    public Task<OperationResult<bool>> ApplyLaunchProfileAsync(
        string instanceName, CancellationToken ct = default) =>
        GuardAsync(() => ApplyLaunchProfile(instanceName, ct), "应用启动配置档失败。");

    // ── 实现 ───────────────────────────────────────────────────────────────

    private OperationResult<IReadOnlyList<InstanceProfileInfo>> List(string instanceName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveInstance(instanceName) is not { } instance)
        {
            return OperationResult<IReadOnlyList<InstanceProfileInfo>>.Fail($"找不到实例「{instanceName}」。");
        }

        var manifest = ProfileManifest.Load(instance.GameDir);
        var rows = manifest.Profiles
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => BuildInfo(instance, manifest, entry))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return OperationResult<IReadOnlyList<InstanceProfileInfo>>.Ok(rows);
    }

    private OperationResult<InstanceProfileInfo> Create(
        string instanceName, string profileName, bool adoptCurrent, string? source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveInstance(instanceName) is not { } instance)
        {
            return OperationResult<InstanceProfileInfo>.Fail($"找不到实例「{instanceName}」。");
        }

        if (ValidateName(profileName) is { } invalid)
        {
            return OperationResult<InstanceProfileInfo>.Fail(invalid);
        }

        var gameDir = instance.GameDir;
        var manifest = ProfileManifest.Load(gameDir);
        if (manifest.Find(profileName) is not null)
        {
            return OperationResult<InstanceProfileInfo>.Fail($"配置档「{profileName}」已存在，换一个名字。");
        }

        var profileDir = ProfileManifest.ResolveProfileDirectory(gameDir, profileName);
        if (Directory.Exists(profileDir))
        {
            return OperationResult<InstanceProfileInfo>.Fail(
                $"目录「{profileDir}」已存在，拒绝覆盖。",
                "如果这是上次失败留下的残骸，先手动清理该目录再重试。");
        }

        if (adoptCurrent && _instances.IsGameRunning(instance.Name))
        {
            return OperationResult<InstanceProfileInfo>.Fail("游戏正在运行，先关闭游戏再接管当前模组状态。");
        }

        var moved = new List<(string From, string To)>();
        try
        {
            Directory.CreateDirectory(profileDir);
            if (adoptCurrent)
            {
                foreach (var name in new[] { GameDataName, CkanDirectoryName })
                {
                    var live = Path.Combine(gameDir, name);
                    if (!Directory.Exists(live))
                    {
                        continue;
                    }

                    var destination = Path.Combine(profileDir, name);
                    if (Directory.Exists(destination))
                    {
                        throw new IOException($"目标目录已存在：{destination}");
                    }

                    Directory.Move(live, destination);
                    moved.Add((live, destination));
                }
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(profileDir, GameDataName));
            }

            var now = DateTime.UtcNow;
            var entry = new ProfileManifestEntry
            {
                Name = profileName,
                CreatedUtc = now,
                LastUsedUtc = adoptCurrent ? now : null,
                Source = source,
                ModCount = 0,
            };
            manifest.Profiles.Add(entry);
            if (adoptCurrent)
            {
                manifest.ActiveProfile = profileName;
            }

            try
            {
                manifest.Save(gameDir);
            }
            catch
            {
                RollbackMoves(moved);
                throw;
            }

            _log.Info(FormattableString.Invariant(
                $"配置档已创建：{instance.Name}/{profileName}（adopt={adoptCurrent}，移动 {moved.Count} 个目录）。"));
            return OperationResult<InstanceProfileInfo>.Ok(BuildInfo(instance, manifest, entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RollbackMoves(moved);
            TryDeleteDirectory(profileDir);
            return OperationResult<InstanceProfileInfo>.Fail(
                $"新建配置档「{profileName}」失败。",
                ex.Message,
                retryable: true);
        }
    }

    private OperationResult<InstanceProfileSwitchResult> Switch(
        string instanceName, string profileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveInstance(instanceName) is not { } instance)
        {
            return OperationResult<InstanceProfileSwitchResult>.Fail($"找不到实例「{instanceName}」。");
        }

        if (ValidateName(profileName) is { } invalid)
        {
            return OperationResult<InstanceProfileSwitchResult>.Fail(invalid);
        }

        var gameDir = instance.GameDir;
        var manifest = ProfileManifest.Load(gameDir);
        var target = manifest.Find(profileName);
        if (target is null)
        {
            return OperationResult<InstanceProfileSwitchResult>.Fail($"配置档「{profileName}」不存在。");
        }

        var targetDir = ProfileManifest.ResolveProfileDirectory(gameDir, target.Name);
        if (!Directory.Exists(targetDir))
        {
            return OperationResult<InstanceProfileSwitchResult>.Fail(
                $"配置档「{target.Name}」的目录已丢失：{targetDir}",
                "目录被手动删除后清单会失真，删除该档再重建。");
        }

        var activeName = manifest.ActiveProfile;
        if (string.IsNullOrWhiteSpace(activeName))
        {
            return OperationResult<InstanceProfileSwitchResult>.Fail(
                "还没有接管当前模组状态，无法切换。",
                "先新建一个 adoptCurrent=true 的配置档把当前 GameData/CKAN 收进去。");
        }

        if (string.Equals(activeName, target.Name, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<InstanceProfileSwitchResult>.Ok(new InstanceProfileSwitchResult(
                instance.Name, activeName, activeName, AdoptedLiveState: false));
        }

        if (_instances.IsGameRunning(instance.Name))
        {
            return OperationResult<InstanceProfileSwitchResult>.Fail("游戏正在运行，先关闭游戏再切换配置档。");
        }

        var isCurrent = _instances.Current is { } current
            && string.Equals(current.Name, instance.Name, StringComparison.OrdinalIgnoreCase);

        if (isCurrent && _registry is not null)
        {
            var closed = _registry.CloseAsync(ct).GetAwaiter().GetResult();
            if (!closed.IsSuccess)
            {
                return OperationResult<InstanceProfileSwitchResult>.Fail(
                    "释放注册表锁失败，未做任何切换。",
                    closed.Message);
            }
        }

        var activeDir = ProfileManifest.ResolveProfileDirectory(gameDir, activeName);
        var liveGameData = Path.Combine(gameDir, GameDataName);
        var liveCkan = Path.Combine(gameDir, CkanDirectoryName);
        var activeGameData = Path.Combine(activeDir, GameDataName);
        var activeCkan = Path.Combine(activeDir, CkanDirectoryName);
        var targetGameData = Path.Combine(targetDir, GameDataName);
        var targetCkan = Path.Combine(targetDir, CkanDirectoryName);

        // 只有「live 里还有东西要挪进去、槽位里又已经有同名目录」才是真冲突：
        // 接管过的档位 live 本来就是空的、东西一直在自己槽位里，那是合法状态。
        if ((Directory.Exists(liveGameData) && Directory.Exists(activeGameData))
            || (Directory.Exists(liveCkan) && Directory.Exists(activeCkan)))
        {
            return OperationResult<InstanceProfileSwitchResult>.Fail(
                $"配置档「{activeName}」的槽位里已有 GameData/CKAN，拒绝覆盖。",
                "这通常是上次切换中断留下的残骸，先手动核对 CKANProfiles 下的目录。");
        }

        var moved = new List<(string From, string To)>();
        var adopted = false;
        try
        {
            Directory.CreateDirectory(activeDir);

            // ① live → 旧档槽位。
            adopted |= MoveIfExists(liveGameData, activeGameData, moved);
            adopted |= MoveIfExists(liveCkan, activeCkan, moved);

            // ② 目标档槽位 → live。
            MoveIfExists(targetGameData, liveGameData, moved);
            MoveIfExists(targetCkan, liveCkan, moved);

            // ③ CKAN 缺失就补空目录：两个方向都要有，否则下一次换位会错位。
            Directory.CreateDirectory(liveCkan);
            Directory.CreateDirectory(activeCkan);

            manifest.ActiveProfile = target.Name;
            target.LastUsedUtc = DateTime.UtcNow;
            try
            {
                manifest.Save(gameDir);
            }
            catch
            {
                RollbackMoves(moved);
                throw;
            }

            _log.Info(FormattableString.Invariant(
                $"配置档已切换：{instance.Name} {activeName} → {target.Name}（移动 {moved.Count} 个目录）。"));

            if (isCurrent && _registry is not null)
            {
                // 重建注册表投影/重新拿锁；失败只记警告，目录已经换好，不能把整个切换判失败。
                try
                {
                    var refreshed = _registry.RefreshAsync(ct).GetAwaiter().GetResult();
                    if (!refreshed.IsSuccess)
                    {
                        _log.Warn($"切换配置档后重建注册表失败：{refreshed.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("切换配置档后重建注册表异常。", ex);
                }
            }

            return OperationResult<InstanceProfileSwitchResult>.Ok(new InstanceProfileSwitchResult(
                instance.Name, activeName, target.Name, adopted));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var rollback = RollbackMoves(moved);
            var detail = rollback.Count == 0
                ? ex.Message
                : $"{ex.Message}；已回滚 {rollback.Count} 个目录，失败项：{string.Join("；", rollback)}";
            _log.Error($"切换配置档失败：{instance.Name} {activeName} → {target.Name}", ex);
            return OperationResult<InstanceProfileSwitchResult>.Fail(
                $"切换到「{target.Name}」失败，已尽力恢复原状。",
                detail,
                retryable: true);
        }
    }

    private OperationResult<bool> Delete(string instanceName, string profileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveInstance(instanceName) is not { } instance)
        {
            return OperationResult<bool>.Fail($"找不到实例「{instanceName}」。");
        }

        if (ValidateName(profileName) is { } invalid)
        {
            return OperationResult<bool>.Fail(invalid);
        }

        var gameDir = instance.GameDir;
        var manifest = ProfileManifest.Load(gameDir);
        var entry = manifest.Find(profileName);
        var profileDir = ProfileManifest.ResolveProfileDirectory(gameDir, profileName);
        if (entry is null && !Directory.Exists(profileDir))
        {
            return OperationResult<bool>.Fail($"配置档「{profileName}」不存在。");
        }

        var activeName = manifest.ActiveProfile;
        if (entry is not null && string.Equals(activeName, entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<bool>.Fail(
                $"配置档「{entry.Name}」正在生效，不能删除。",
                "先切换到别的配置档，再删除这一档。");
        }

        try
        {
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
            }

            if (entry is not null)
            {
                manifest.Profiles.Remove(entry);
            }

            if (manifest.LaunchProfile is { } launch
                && string.Equals(launch, profileName, StringComparison.OrdinalIgnoreCase))
            {
                manifest.LaunchProfile = null;
            }

            manifest.Save(gameDir);
            _log.Info($"配置档已删除：{instance.Name}/{profileName}。");
            return OperationResult<bool>.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Fail(
                $"删除配置档「{profileName}」失败。",
                ex.Message,
                retryable: true);
        }
    }

    private OperationResult<string?> GetLaunchProfile(string instanceName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveInstance(instanceName) is not { } instance)
        {
            return OperationResult<string?>.Fail($"找不到实例「{instanceName}」。");
        }

        return OperationResult<string?>.Ok(ProfileManifest.Load(instance.GameDir).LaunchProfile);
    }

    private OperationResult<bool> SetLaunchProfile(string instanceName, string? profileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveInstance(instanceName) is not { } instance)
        {
            return OperationResult<bool>.Fail($"找不到实例「{instanceName}」。");
        }

        var gameDir = instance.GameDir;
        var manifest = ProfileManifest.Load(gameDir);
        if (profileName is null)
        {
            manifest.LaunchProfile = null;
            return SaveManifest(instance, manifest, "已清除启动配置档。");
        }

        if (ValidateName(profileName) is { } invalid)
        {
            return OperationResult<bool>.Fail(invalid);
        }

        var entry = manifest.Find(profileName);
        if (entry is null)
        {
            return OperationResult<bool>.Fail($"配置档「{profileName}」不存在，不能设为启动档。");
        }

        manifest.LaunchProfile = entry.Name;
        return SaveManifest(instance, manifest, $"启动配置档已设为「{entry.Name}」。");
    }

    private OperationResult<bool> ApplyLaunchProfile(string instanceName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveInstance(instanceName) is not { } instance)
        {
            return OperationResult<bool>.Fail($"找不到实例「{instanceName}」。");
        }

        var manifest = ProfileManifest.Load(instance.GameDir);
        var launch = manifest.LaunchProfile;
        if (string.IsNullOrWhiteSpace(launch))
        {
            return OperationResult<bool>.Ok(true);
        }

        if (string.Equals(manifest.ActiveProfile, launch, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<bool>.Ok(true);
        }

        var switched = Switch(instance.Name, launch, ct);
        return switched.IsSuccess
            ? OperationResult<bool>.Ok(true)
            : OperationResult<bool>.Fail(
                $"启动前切换到「{launch}」失败：{switched.Message}",
                switched.Detail,
                switched.Retryable);
    }

    private OperationResult<bool> SaveManifest(InstanceInfo instance, ProfileManifest manifest, string successMessage)
    {
        try
        {
            manifest.Save(instance.GameDir);
            _log.Info($"{instance.Name}：{successMessage}");
            return OperationResult<bool>.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Fail("写入配置档清单失败。", ex.Message, retryable: true);
        }
    }

    // ── 目录与清单辅助 ─────────────────────────────────────────────────────

    private InstanceInfo? ResolveInstance(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return null;
        }

        return _instances.Instances.FirstOrDefault(instance =>
            string.Equals(instance.Name, instanceName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>名字校验：返回 null 表示合法，否则是可读原因。</summary>
    internal static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "配置档名不能为空。";
        }

        var trimmed = name.Trim();
        if (trimmed.Length != name.Length)
        {
            return "配置档名首尾不能有空白字符。";
        }

        if (name.Length > MaxProfileNameLength)
        {
            return $"配置档名最长 {MaxProfileNameLength} 个字符。";
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            return "配置档名不能包含「..」。";
        }

        if (name.IndexOfAny(InvalidNameChars.ToCharArray()) >= 0)
        {
            return "配置档名不能包含 / \\ : * ? \" < > | 这些字符。";
        }

        if (name is "." or "")
        {
            return "配置档名不能是「.」。";
        }

        return null;
    }

    private InstanceProfileInfo BuildInfo(
        InstanceInfo instance, ProfileManifest manifest, ProfileManifestEntry entry)
    {
        var directory = ProfileManifest.ResolveProfileDirectory(instance.GameDir, entry.Name);
        var gameData = Path.Combine(directory, GameDataName);
        var stats = ProfileDirectoryStats.Measure(gameData);
        var isActive = string.Equals(manifest.ActiveProfile, entry.Name, StringComparison.OrdinalIgnoreCase);

        return new InstanceProfileInfo(
            entry.Name,
            instance.Name,
            isActive,
            directory,
            gameData,
            stats.Bytes,
            stats.FileCount,
            entry.LastUsedUtc)
        {
            IsLaunchProfile = string.Equals(manifest.LaunchProfile, entry.Name, StringComparison.OrdinalIgnoreCase),
            Source = entry.Source,
            ModCount = entry.ModCount,
        };
    }

    private static bool MoveIfExists(string from, string to, List<(string From, string To)> moved)
    {
        if (!Directory.Exists(from))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(to);
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }

        if (Directory.Exists(to))
        {
            throw new IOException($"目标目录已存在：{to}");
        }

        Directory.Move(from, to);
        moved.Add((from, to));
        return true;
    }

    /// <summary>按相反顺序把已移动的目录移回去；返回失败项描述（空表示全部回滚成功）。</summary>
    private List<string> RollbackMoves(List<(string From, string To)> moved)
    {
        var failures = new List<string>();
        for (var index = moved.Count - 1; index >= 0; index--)
        {
            var (from, to) = moved[index];
            try
            {
                if (!Directory.Exists(to))
                {
                    continue;
                }

                if (Directory.Exists(from))
                {
                    failures.Add($"{from} 已被占用");
                    continue;
                }

                Directory.Move(to, from);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{from}：{ex.Message}");
                _log.Warn($"回滚目录失败：{to} → {from}", ex);
            }
        }

        return failures;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理残骸失败不影响结论：清单里没有这一档，下次新建会因目录已存在而明确失败。
        }
    }

    private async Task<OperationResult<T>> GuardAsync<T>(Func<OperationResult<T>> action, string failureMessage)
    {
        try
        {
            return await Task.Run(action).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _log.Warn($"{failureMessage}操作已取消。", ex);
            return OperationResult<T>.Fail("操作已取消。", ex.Message, retryable: true);
        }
        catch (Exception ex)
        {
            _log.Error(failureMessage, ex);
            return OperationResult<T>.Fail(failureMessage, ex.ToString(), retryable: true);
        }
    }
}

/// <summary>递归统计一个目录的字节数与文件数；读不到的条目跳过（不抛）。</summary>
internal static class ProfileDirectoryStats
{
    public static (long Bytes, int FileCount) Measure(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return (0, 0);
        }

        long bytes = 0;
        var files = 0;
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                try
                {
                    if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if (Directory.Exists(entry))
                    {
                        pending.Push(entry);
                        continue;
                    }

                    bytes += new FileInfo(entry).Length;
                    files++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 枚举与读取之间被删掉：忽略这一条。
                }
            }
        }

        return (bytes, files);
    }
}
