namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 诊断包里的「非日志事实」（14 篇 §5.1 的 env.txt / summary.json / registry.summary.json / config.redacted.json）。
/// 由 App 层提供：只有 App 知道版本、DPI、主题、实例数、仓库数；
/// 服务层只负责编排与脱敏，所以这里用契约把两者分开——这也是日志域不引用实例/注册表服务的实现依赖的原因。
/// </summary>
public sealed record DiagnosticFacts
{
    /// <summary>空事实（找不到提供者时的兜底，仍然能出包）。</summary>
    public static DiagnosticFacts Empty { get; } = new();

    /// <summary>env.txt 内容（键=值 行，已由提供者格式化）。</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>summary.json 的附加字段（版本/Avalonia/OS/架构/DPI/主题/实例数/仓库数…）。</summary>
    public IReadOnlyDictionary<string, object?> Summary { get; init; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>registry.summary.json 的内容（只版本号与计数，由提供者裁剪；服务层不再二次过滤）。</summary>
    public IReadOnlyDictionary<string, object?> Manifest { get; init; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>config.json 的键值（未脱敏；服务层用 <see cref="ILogRedactor"/> 脱敏后写出）。</summary>
    public IReadOnlyDictionary<string, string?> Config { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);
}

/// <summary>
/// 诊断事实提供者（App 层实现，注册为单例）。
/// 允许返回 null 表示「本次没有事实可给」，服务层照常出包。
/// </summary>
public interface IDiagnosticFactsProvider
{
    /// <summary>收集当前进程的事实快照。</summary>
    System.Threading.Tasks.Task<DiagnosticFacts?> CollectAsync(System.Threading.CancellationToken cancellationToken = default);
}
