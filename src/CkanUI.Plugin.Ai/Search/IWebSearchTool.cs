namespace CkanUI.Plugin.Ai.Search;

/// <summary>网页搜索的一条结果（只保留模型真正会引用的三段）。</summary>
/// <param name="Title">标题；拿不到时为空串。</param>
/// <param name="Url">链接；拿不到时为空串。</param>
/// <param name="Snippet">摘要片段。</param>
public sealed record WebSearchHit(string Title, string Url, string Snippet);

/// <summary>一次网页搜索的结果：成功给命中列表，失败给原因（不抛异常）。</summary>
/// <param name="Ok">搜索是否成功。</param>
/// <param name="Hits">命中列表（<c>Ok</c> 为假时是空表）。</param>
/// <param name="Error">失败原因（<c>Ok</c> 为真时为 <c>null</c>）。</param>
public sealed record WebSearchResult(bool Ok, IReadOnlyList<WebSearchHit> Hits, string? Error)
{
    /// <summary>构造一条「不可用」结果。</summary>
    /// <param name="error">原因。</param>
    /// <returns>失败结果。</returns>
    public static WebSearchResult Unavailable(string error)
        => new(false, System.Array.Empty<WebSearchHit>(), error);

    /// <summary>构造一条成功结果。</summary>
    /// <param name="hits">命中列表。</param>
    /// <returns>成功结果。</returns>
    public static WebSearchResult Success(IReadOnlyList<WebSearchHit> hits)
        => new(true, hits ?? System.Array.Empty<WebSearchHit>(), null);
}

/// <summary>
/// 网页搜索的端口：模型调用 <c>web_search</c> 时走它。
/// </summary>
/// <remarks>
/// 做成接口的理由是降级路径要能被测试：真实实现对着一台 MCP 服务器，单测只需要一个「永远说不可用」的假实现，
/// 就能断言模型拿到的是结构化错误而不是异常。
/// </remarks>
public interface IWebSearchTool
{
    /// <summary>搜索。</summary>
    /// <param name="query">查询词。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果；失败也走返回值，不抛异常。</returns>
    Task<WebSearchResult> SearchAsync(string query, CancellationToken cancellationToken = default);
}
