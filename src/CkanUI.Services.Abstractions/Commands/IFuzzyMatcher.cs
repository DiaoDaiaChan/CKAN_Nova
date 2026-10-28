namespace CkanUI.Services.Abstractions.Commands;

/// <summary>
/// 命令匹配算法（UI-40）：把用户输入切成关键词，逐条命令算分并给出标题高亮。
/// </summary>
/// <remarks>
/// <para>抽成接口是为了让 ViewModels 只依赖契约：面板 VM 拿到的是 <see cref="CommandMatch"/>，
/// 不必引用实现所在的 Services（架构测试禁止 ViewModels 引用实现库）。</para>
/// <para>实现必须是**纯函数**：只读入参、不碰 UI、不抛异常（查询为空或无结果时返回空列表）。</para>
/// </remarks>
public interface IFuzzyMatcher
{
    /// <summary>
    /// 在 <paramref name="commands"/> 里检索 <paramref name="query"/>，按相关度降序返回前 <paramref name="limit"/> 条。
    /// </summary>
    /// <param name="commands">候选命令（顺序即稳定排序的兜底顺序）。</param>
    /// <param name="query">用户输入；null/空白表示不过滤（按分类 + 注册顺序）。</param>
    /// <param name="limit">最多返回多少条；小于等于 0 时按 50 处理。</param>
    /// <returns>命中结果；无命中返回空列表而不是 null。</returns>
    IReadOnlyList<CommandMatch> Search(IReadOnlyList<CommandDescriptor> commands, string? query, int limit = 50);

    /// <summary>判断单条命令是否命中 <paramref name="query"/>；命中返回结果（含分数与高亮），否则返回 null。</summary>
    /// <param name="command">待判定的命令。</param>
    /// <param name="query">用户输入；null/空白视为命中（分数 0、无高亮）。</param>
    CommandMatch? Match(CommandDescriptor command, string? query);
}
