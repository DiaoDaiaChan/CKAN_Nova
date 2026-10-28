using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;

using log4net;

namespace CkanUI.Services.Unmanaged;

/// <summary>
/// 唯一会写注册表的接管件（12 篇 F-42 / INT-09）：把「手工安装的 DLL」登记进 <c>installed_dlls</c>，
/// 语义与上游 <c>RegistryManager.ScanUnmanagedFiles</c> 一致（标识符 → GameDir 相对路径）。
/// </summary>
/// <remarks>
/// <para>上游 <c>Registry.SetDlls</c> 是整体替换：直接传新映射会把其它手工安装的 DLL 全清掉，
/// 所以这里先读出既有映射合并再写（<see cref="ManagedDllMerge.Merge"/>）。</para>
/// <para>写之前把 <c>registry.json</c> 复制成 <c>registry.json.bak-yyyyMMdd-HHmmss</c>；保存失败时同时还原磁盘与内存，
/// 避免内存里的注册表停在「已改但没落盘」的脏状态。</para>
/// </remarks>
public sealed class CkanManagedFileRegistrar : IManagedFileRegistrar
{
    private const string RegistryFileName = "registry.json";

    private static readonly ILog Log = LogManager.GetLogger(typeof(CkanManagedFileRegistrar));

    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;

    public CkanManagedFileRegistrar(GameInstanceManager instances, RepositoryDataManager repositoryData)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyDictionary<string, string>> GetInstalledDlls(InstanceInfo instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        try
        {
            if (!_instances.Instances.TryGetValue(instance.Name, out var gameInstance))
            {
                return OperationResult<IReadOnlyDictionary<string, string>>.Fail($"找不到游戏实例“{instance.Name}”。");
            }

            var registry = RegistryManager.Instance(gameInstance, _repositoryData).registry;
            return OperationResult<IReadOnlyDictionary<string, string>>.Ok(ReadDlls(registry));
        }
        catch (RegistryInUseKraken ex)
        {
            return OperationResult<IReadOnlyDictionary<string, string>>.Fail(
                "注册表正被其它 CKAN 进程占用，请关闭那个进程后重试。", ex.Message, retryable: false);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyDictionary<string, string>>.Fail(
                "读取注册表失败，无法判断已登记的手工安装 DLL。", ex.ToString(), retryable: true);
        }
    }

    /// <inheritdoc />
    public OperationResult<DllMergeResult> MergeDlls(
        InstanceInfo instance,
        IReadOnlyDictionary<string, string> dlls,
        bool takeSnapshot = true)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (dlls is null || dlls.Count == 0)
        {
            return OperationResult<DllMergeResult>.Ok(new DllMergeResult(false, null));
        }

        GameInstance gameInstance;
        RegistryManager manager;
        CKAN.Registry registry;
        try
        {
            if (!_instances.Instances.TryGetValue(instance.Name, out var located) || located is null)
            {
                return OperationResult<DllMergeResult>.Fail($"找不到游戏实例“{instance.Name}”。");
            }

            gameInstance = located;
            manager = RegistryManager.Instance(gameInstance, _repositoryData);
            registry = manager.registry;
        }
        catch (RegistryInUseKraken ex)
        {
            return OperationResult<DllMergeResult>.Fail(
                "注册表正被其它 CKAN 进程占用，请关闭那个进程后重试。", ex.Message, retryable: false);
        }
        catch (Exception ex)
        {
            return OperationResult<DllMergeResult>.Fail(
                "读取注册表失败，无法写入手工安装的 DLL。", ex.ToString(), retryable: true);
        }

        var existing = ReadDlls(registry);
        var merged = ManagedDllMerge.Merge(existing, dlls);

        string? backup = null;
        if (takeSnapshot)
        {
            var snapshot = TakeSnapshot(gameInstance.CkanDir);
            if (!snapshot.IsSuccess)
            {
                return OperationResult<DllMergeResult>.Fail(
                    snapshot.Message ?? "备份注册表失败，已中止接管。", snapshot.Detail, snapshot.Retryable);
            }

            backup = snapshot.Value;
        }

        bool changed;
        try
        {
            changed = registry.SetDlls(ToDictionary(merged));
        }
        catch (Exception ex)
        {
            return OperationResult<DllMergeResult>.Fail(
                "写入注册表失败，未做任何改动。", ex.ToString(), retryable: true);
        }

        if (!changed)
        {
            return OperationResult<DllMergeResult>.Ok(new DllMergeResult(false, backup));
        }

        try
        {
            manager.Save();
        }
        catch (Exception ex)
        {
            TryRestore(registry, existing, backup, gameInstance.CkanDir);
            return OperationResult<DllMergeResult>.Fail(
                "保存注册表失败，已还原到写前状态。", ex.ToString(), retryable: true);
        }

        return OperationResult<DllMergeResult>.Ok(new DllMergeResult(true, backup));
    }

    /// <inheritdoc />
    public string NormalizeIdentifier(string raw)
        => string.IsNullOrWhiteSpace(raw) ? string.Empty : Identifier.Sanitize(raw);

    /// <summary>把注册表里的手工安装 DLL 摊平成「标识符 → 路径」（只取已记录路径的条目）。</summary>
    public static IReadOnlyDictionary<string, string> ReadDlls(CKAN.Registry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var identifier in registry.InstalledDlls)
        {
            var path = registry.DllPath(identifier);
            if (string.IsNullOrWhiteSpace(path)) continue;
            map[identifier] = path;
        }

        return map;
    }

    /// <summary>把 <c>registry.json</c> 复制成带时间戳的快照；文件不存在时返回 <c>null</c>（新实例还没写过注册表）。</summary>
    private static OperationResult<string?> TakeSnapshot(string ckanDir)
    {
        try
        {
            var source = Path.Combine(ckanDir, RegistryFileName);
            if (!File.Exists(source))
            {
                return OperationResult<string?>.Ok(null);
            }

            var backup = Path.Combine(ckanDir, ManagedDllMerge.SnapshotFileName(DateTime.Now));
            File.Copy(source, backup, overwrite: true);
            return OperationResult<string?>.Ok(backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string?>.Fail("备份注册表失败，已中止接管。", ex.Message, retryable: true);
        }
    }

    /// <summary>Core 的 <c>SetDlls</c> 收 <see cref="IDictionary{TKey,TValue}"/>，只读视图要先转成可变副本。</summary>
    private static Dictionary<string, string> ToDictionary(IReadOnlyDictionary<string, string> dlls)
        => new(dlls, StringComparer.Ordinal);

    /// <summary>保存失败后把内存与磁盘都拉回写前状态（还原本身再失败只记日志，不覆盖原始错误）。</summary>
    private static void TryRestore(
        CKAN.Registry registry,
        IReadOnlyDictionary<string, string> existing,
        string? backup,
        string ckanDir)
    {
        try
        {
            registry.SetDlls(ToDictionary(existing));
        }
        catch (Exception ex)
        {
            Log.Warn("还原内存中的 registered_dlls 失败。", ex);
        }

        if (backup is null || !File.Exists(backup)) return;

        try
        {
            File.Copy(backup, Path.Combine(ckanDir, RegistryFileName), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("还原注册表快照失败：" + backup, ex);
        }
    }
}
