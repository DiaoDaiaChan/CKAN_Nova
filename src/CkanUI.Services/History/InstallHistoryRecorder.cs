using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.History;

/// <summary>
/// 把"当前实例"翻译成历史文件路径、并收口追加写盘（12 篇 F-44）。
/// </summary>
/// <remarks>
/// 单独抽出来是为了打破依赖环：<see cref="HistoryRecordingInstaller"/> 需要写历史，
/// 而 <see cref="InstallHistoryService"/> 需要 <see cref="IInstallerService"/> 来做重装；
/// 若装饰器直接依赖 <see cref="IInstallHistoryService"/>，容器解析时会成环。
/// 装饰器只依赖本类（不依赖任何历史服务），环就断在这里。
/// </remarks>
public sealed class InstallHistoryRecorder
{
    /// <summary>历史文件相对游戏目录的子路径：<c>&lt;GameDir&gt;/CKAN/install_history.jsonl</c>。</summary>
    public const string RelativeDirectory = "CKAN";

    private readonly IInstanceService _instances;
    private readonly int _maxLines;
    private readonly Func<DateTimeOffset> _clock;

    public InstallHistoryRecorder(
        IInstanceService instances,
        int maxLines = InstallHistoryStore.DefaultMaxLines,
        Func<DateTimeOffset>? clock = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _maxLines = maxLines;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>当前实例名；没有实例时为 null。</summary>
    public string? InstanceName => _instances.Current?.Name;

    /// <summary>当前实例对应的历史文件路径；没有可用实例时为 null。</summary>
    public string? FilePath
    {
        get
        {
            var current = _instances.Current;
            if (current is null || string.IsNullOrWhiteSpace(current.GameDir))
            {
                return null;
            }

            return Path.Combine(current.GameDir, RelativeDirectory, InstallHistoryStore.FileName);
        }
    }

    /// <summary>打开当前实例的历史文件；没有实例时返回 null。上限取构造时传入的值。</summary>
    public InstallHistoryStore? TryOpenStore()
    {
        var path = FilePath;
        return path is null ? null : new InstallHistoryStore(path, _maxLines);
    }

    /// <summary>追加若干行；返回写入后总行数，失败时给出中文原因（可重试）。</summary>
    public OperationResult<int> Append(IReadOnlyList<InstallHistoryLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var store = TryOpenStore();
        if (store is null)
        {
            return OperationResult<int>.Fail("当前没有可用的游戏实例，无法写入安装历史。");
        }

        try
        {
            return OperationResult<int>.Ok(store.Append(lines));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<int>.Fail(
                "写入安装历史失败，请检查实例目录的写权限后重试。",
                ex.Message,
                retryable: true);
        }
    }

    /// <summary>
    /// 追加一条历史（条目行 + 快照行，同一时间戳）。时间戳由本类取，保证两行严格一致。
    /// </summary>
    /// <param name="request">条目内容。</param>
    /// <param name="modules">写入快照的已装集合；为空则不写快照行（该条历史不可作为重装目标）。</param>
    public OperationResult<InstallHistoryEntry> Record(
        InstallHistoryRecordRequest request,
        IReadOnlyList<InstalledModuleVersion>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instance = InstanceName ?? string.Empty;
        var at = _clock();
        var lines = new List<InstallHistoryLine>(2)
        {
            InstallHistoryLine.Entry(
                at,
                instance,
                request.Operation,
                request.Outcome,
                request.Changes,
                request.Message),
        };

        InstallHistorySnapshot? snapshot = null;
        if (modules is { Count: > 0 })
        {
            lines.Add(InstallHistoryLine.Snapshot(at, instance, modules));
            snapshot = new InstallHistorySnapshot(
                at,
                instance,
                modules.Select(module => new InstallHistoryModule(module.Identifier, module.Version)).ToList());
        }

        var appended = Append(lines);
        if (!appended.IsSuccess)
        {
            return OperationResult<InstallHistoryEntry>.Fail(
                appended.Message ?? "写入安装历史失败。",
                appended.Detail,
                appended.Retryable);
        }

        var entry = new InstallHistoryEntry(
            at,
            instance,
            request.Operation,
            request.Outcome,
            request.Changes,
            request.Message)
        {
            Snapshot = snapshot,
        };

        return OperationResult<InstallHistoryEntry>.Ok(entry);
    }
}
