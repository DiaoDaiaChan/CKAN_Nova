using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;

namespace CkanUI.Services.Repositories;

/// <summary>
/// 仓库列表的纯逻辑层（06 篇 UI-06 / 03 篇 §6 V-04）：校验、排序、优先级重排与行投影。
/// </summary>
/// <remarks>
/// <para>所有 Core 依赖都被压到 <see cref="IRepositoryStore"/> 后面，因此本类可以完全离线单测
/// （用假存储模拟注册表内容、过期时长与刷新失败）。</para>
/// <para>失败语义（与 01 篇 §8 统一返回类型一致）：<see cref="OperationResult{T}.Retryable"/> 为真表示
/// 「同样的操作再点一次有意义」（网络/保存失败），为假表示「必须先改输入或先选实例」。</para>
/// </remarks>
public sealed class RepositoryCatalog
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IRepositoryStore _store;
    private readonly Dictionary<string, string?> _failures = new(StringComparer.Ordinal);

    /// <summary>构造时绑定存储端口。</summary>
    public RepositoryCatalog(IRepositoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>最近一次投影出来的行（按优先级升序）；未加载时为空。</summary>
    public IReadOnlyList<RepositoryEntryInfo> Snapshot { get; private set; } = RepositoryEntryInfo.Empty;

    /// <summary>读取当前仓库列表（不联网，不刷新索引）。无实例时返回空列表而不是错误。</summary>
    public OperationResult<IReadOnlyList<RepositoryEntryInfo>> Load()
        => Guard(() => Ok(Project(_store.Read())), "读取仓库列表失败。");

    /// <summary>新增仓库：追加到末尾（优先级最低），重名/地址重复/非法地址一律拒绝。</summary>
    public OperationResult<IReadOnlyList<RepositoryEntryInfo>> Add(string? name, string? uri)
        => Guard(() => AddCore(name, uri), "保存仓库列表失败，修改未生效。");

    /// <summary>移除仓库；找不到或只剩一个时拒绝（Core 的注册表至少要有一个仓库）。</summary>
    public OperationResult<IReadOnlyList<RepositoryEntryInfo>> Remove(string? name)
        => Guard(() => RemoveCore(name), "保存仓库列表失败，修改未生效。");

    /// <summary>按 <paramref name="delta"/> 交换相邻优先级；越界时原样返回成功（视图侧按钮本就禁用）。</summary>
    public OperationResult<IReadOnlyList<RepositoryEntryInfo>> Move(string? name, int delta)
        => Guard(() => MoveCore(name, delta), "保存仓库列表失败，修改未生效。");

    /// <summary>恢复官方默认仓库列表（需要联网取列表）；离线时返回可重试失败并保持原列表不变。</summary>
    public OperationResult<IReadOnlyList<RepositoryEntryInfo>> RestoreDefaults()
        => Guard(RestoreDefaultsCore, "恢复默认仓库失败。");

    /// <summary>记录某个仓库最近一次刷新失败（下一次成功刷新或编辑后自动清除）。</summary>
    public void MarkFailure(string name, string? message)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _failures[name] = string.IsNullOrWhiteSpace(message) ? "刷新失败。" : message;
    }

    /// <summary>清除某个仓库的失败标记。</summary>
    public void ClearFailure(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _failures.Remove(name);
        }
    }

    // ---- 内部实现 ---------------------------------------------------------

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> AddCore(string? name, string? uri)
    {
        if (!_store.IsAvailable)
        {
            return Unavailable();
        }

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return Fail("仓库名称不能为空。");
        }

        var trimmedUri = uri?.Trim();
        if (string.IsNullOrEmpty(trimmedUri))
        {
            return Fail("仓库地址不能为空。");
        }

        if (!TryParseUri(trimmedUri, out var parsed))
        {
            return Fail("仓库地址必须是 http 或 https 开头的完整链接。");
        }

        var entries = Order(_store.Read()).ToList();
        if (entries.Exists(entry => NameComparer.Equals(entry.Name, trimmedName)))
        {
            return Fail($"已存在同名仓库「{trimmedName}」，请换一个名称。");
        }

        if (entries.Exists(entry => SameUri(entry.Uri, parsed)))
        {
            return Fail($"该仓库地址已存在：{parsed.AbsoluteUri}");
        }

        entries.Add(new RepositoryEntry(trimmedName, parsed.AbsoluteUri, entries.Count));
        return Write(entries);
    }

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> RemoveCore(string? name)
    {
        if (!_store.IsAvailable)
        {
            return Unavailable();
        }

        var trimmed = name?.Trim();
        var entries = Order(_store.Read()).ToList();
        var index = entries.FindIndex(entry => NameComparer.Equals(entry.Name, trimmed));
        if (index < 0)
        {
            return Fail($"没有找到名为「{trimmed}」的仓库。");
        }

        if (entries.Count <= 1)
        {
            return Fail("至少要保留一个仓库，不能删除最后一个。");
        }

        _failures.Remove(entries[index].Name);
        entries.RemoveAt(index);
        return Write(entries);
    }

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> MoveCore(string? name, int delta)
    {
        if (!_store.IsAvailable)
        {
            return Unavailable();
        }

        var trimmed = name?.Trim();
        var entries = Order(_store.Read()).ToList();
        var index = entries.FindIndex(entry => NameComparer.Equals(entry.Name, trimmed));
        if (index < 0)
        {
            return Fail($"没有找到名为「{trimmed}」的仓库。");
        }

        var target = index + delta;
        if (delta == 0 || target < 0 || target >= entries.Count)
        {
            // 已在两端：不写盘、不报错（视图里对应的按钮本就禁用）。
            return Ok(Project(entries));
        }

        (entries[index], entries[target]) = (entries[target], entries[index]);
        return Write(entries);
    }

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> RestoreDefaultsCore()
    {
        if (!_store.IsAvailable)
        {
            return Unavailable();
        }

        var defaults = _store.ReadOfficialDefaults();
        if (defaults is null || defaults.Count == 0)
        {
            return Fail("无法获取官方仓库列表，请检查网络后重试。", retryable: true);
        }

        _failures.Clear();
        var entries = Order(defaults).ToList();
        return Write(entries);
    }

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> Write(IReadOnlyList<RepositoryEntry> entries)
    {
        // 顺序即优先级：下标 0 最优先。
        var normalized = new List<RepositoryEntry>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            normalized.Add(new RepositoryEntry(entries[index].Name, entries[index].Uri, index));
        }

        _store.Write(normalized);
        return Ok(Project(_store.Read()));
    }

    private IReadOnlyList<RepositoryEntryInfo> Project(IReadOnlyList<RepositoryEntry> entries)
    {
        var defaultName = _store.DefaultRepositoryName;
        var rows = new List<RepositoryEntryInfo>(entries.Count);
        foreach (var entry in Order(entries))
        {
            var age = _store.AgeOf(entry.Name);
            var hasFailure = _failures.TryGetValue(entry.Name, out var failureMessage);
            rows.Add(new RepositoryEntryInfo(
                entry.Name,
                entry.Uri,
                entry.Priority,
                age is { } known ? DateTime.UtcNow - known : null,
                age is { } stale && stale > RepositoryDataManager.TimeTillStale,
                age is { } veryStale && veryStale > RepositoryDataManager.TimeTillVeryStale,
                string.Equals(defaultName, entry.Name, StringComparison.Ordinal),
                hasFailure,
                hasFailure ? failureMessage : null));
        }

        Snapshot = rows;
        return rows;
    }

    private static IEnumerable<RepositoryEntry> Order(IReadOnlyList<RepositoryEntry> entries)
        => entries.OrderBy(entry => entry.Priority).ThenBy(entry => entry.Name, StringComparer.Ordinal);

    private static bool TryParseUri(string text, out Uri parsed)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            parsed = uri;
            return true;
        }

        parsed = null!;
        return false;
    }

    private static bool SameUri(string existing, Uri candidate)
        => Uri.TryCreate(existing, UriKind.Absolute, out var uri) && uri == candidate;

    private static OperationResult<IReadOnlyList<RepositoryEntryInfo>> Ok(IReadOnlyList<RepositoryEntryInfo> rows)
        => OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Ok(rows);

    private static OperationResult<IReadOnlyList<RepositoryEntryInfo>> Fail(
        string message, string? detail = null, bool retryable = false)
        => OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail(message, detail, retryable);

    private static OperationResult<IReadOnlyList<RepositoryEntryInfo>> Unavailable()
        => Fail("还没有选择游戏实例，请先选择一个实例再编辑仓库列表。");

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> Guard(
        Func<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> action, string fallback)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return Fail(fallback, ex.Message, retryable: true);
        }
    }
}
