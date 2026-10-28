namespace CkanUI.Services.Abstractions.Commands;

/// <summary>
/// 命令目录（UI-40）：外壳与插件把可执行的命令注册进来，命令面板据此检索。
/// </summary>
/// <remarks>
/// <para>目录只保存命令描述，不执行命令：执行由宿主完成（面板 VM 把 Id 抛给外壳，外壳映射到导航或既有命令）。
/// 这样命令面板不需要知道 ShellViewModel / 服务层的任何具体类型。</para>
/// <para>注册失败（Id 重复、批量里有坏数据）返回 <see cref="OperationResult"/>，不向调用方抛异常（服务层统一口径）。</para>
/// </remarks>
public interface ICommandCatalog
{
    /// <summary>当前目录快照（注册顺序，只读副本；调用方可以安全地持有）。</summary>
    IReadOnlyList<CommandDescriptor> Snapshot();

    /// <summary>注册一条命令；Id 已存在时返回失败（幂等保护，不覆盖既有命令）。</summary>
    /// <param name="command">待注册命令。</param>
    OperationResult Register(CommandDescriptor command);

    /// <summary>批量注册：先整体校验，任何一条冲突或重复则整批不生效（all-or-nothing），避免半批注册导致状态不明。</summary>
    /// <param name="commands">待注册命令集合。</param>
    OperationResult Register(IEnumerable<CommandDescriptor> commands);

    /// <summary>检索命令，按相关度降序返回前 <paramref name="limit"/> 条；空白查询返回默认顺序。</summary>
    /// <param name="query">用户输入。</param>
    /// <param name="limit">最多返回多少条；小于等于 0 时按 50 处理。</param>
    IReadOnlyList<CommandMatch> Search(string? query, int limit = 50);
}
