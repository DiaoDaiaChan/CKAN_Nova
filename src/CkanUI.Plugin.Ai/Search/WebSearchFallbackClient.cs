using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.Plugin.Ai.Search;

/// <summary>
/// 当未配置或不可用外部 MCP 搜索服务器时，内置的备用网页与模组搜索引擎。
/// 优先检索 SpaceDock (KSP 官方/社区模组主站) 与开放网络搜索引擎，保证问答永远可用。
/// </summary>
public sealed class WebSearchFallbackClient : IWebSearchTool, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public WebSearchFallbackClient(HttpClient? http = null)
    {
        if (http is not null)
        {
            _http = http;
            _ownsClient = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectTimeout = TimeSpan.FromSeconds(8),
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 CkanUI-Ai/1.0");
            _ownsClient = true;
        }
    }

    public async Task<WebSearchResult> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var cleanQuery = (query ?? string.Empty).Trim();
        if (cleanQuery.Length == 0)
        {
            return WebSearchResult.Unavailable("搜索查询词为空。");
        }

        var hits = new List<WebSearchHit>();

        // 1. 尝试 SpaceDock 模组搜索
        try
        {
            var spaceDockHits = await SearchSpaceDockAsync(cleanQuery, cancellationToken).ConfigureAwait(false);
            if (spaceDockHits.Count > 0)
            {
                hits.AddRange(spaceDockHits);
            }
        }
        catch
        {
            // 忽略单一搜索引擎失败
        }

        // 2. 尝试网络搜索 (DuckDuckGo Lite)
        try
        {
            var webHits = await SearchDuckDuckGoLiteAsync(cleanQuery, cancellationToken).ConfigureAwait(false);
            if (webHits.Count > 0)
            {
                foreach (var hit in webHits)
                {
                    if (!hits.Exists(h => string.Equals(h.Url, hit.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        hits.Add(hit);
                    }
                }
            }
        }
        catch
        {
            // 忽略单一搜索引擎失败
        }

        if (hits.Count > 0)
        {
            return WebSearchResult.Success(hits);
        }

        return WebSearchResult.Unavailable($"未检索到与“{cleanQuery}”相关的网页结果，请检查网络或外部搜索服务。");
    }

    private async Task<List<WebSearchHit>> SearchSpaceDockAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<WebSearchHit>();
        var url = $"https://spacedock.info/api/search/mod?query={Uri.EscapeDataString(query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return results;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        var count = 0;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (count++ >= 8) break;

            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var desc = item.TryGetProperty("short_description", out var descProp) ? descProp.GetString() : null;
            var author = item.TryGetProperty("author", out var authorProp) ? authorProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(name) || id <= 0) continue;

            var modUrl = $"https://spacedock.info/mod/{id}";
            var snippet = string.IsNullOrWhiteSpace(author) ? desc ?? "" : $"作者: {author} | {desc}";
            results.Add(new WebSearchHit($"[SpaceDock] {name}", modUrl, snippet));
        }

        return results;
    }

    private async Task<List<WebSearchHit>> SearchDuckDuckGoLiteAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<WebSearchHit>();
        var url = "https://html.duckduckgo.com/html/";

        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", query),
            new KeyValuePair<string, string>("b", ""),
            new KeyValuePair<string, string>("kl", "wt-wt"),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return results;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return results;
        }

        // 解析 DuckDuckGo HTML 结果结构
        // <a class="result__snippet" ...> / <a class="result__url" ...> / <h2 class="result__title">
        var resultMatches = Regex.Matches(html, @"<div[^>]*class=""[^""]*result__body[^""]*""[^>]*>(.*?)</div>s*</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var count = 0;
        foreach (Match match in resultMatches)
        {
            if (count++ >= 6) break;

            var body = match.Groups[1].Value;

            // 提取标题与链接
            var titleMatch = Regex.Match(body, @"<a[^>]*class=""[^""]*result__url[^""]*""[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!titleMatch.Success)
            {
                titleMatch = Regex.Match(body, @"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }

            var rawHref = titleMatch.Success ? titleMatch.Groups[1].Value : "";
            var title = titleMatch.Success ? StripHtml(titleMatch.Groups[2].Value) : "";

            // 处理 DuckDuckGo 跳转重定向链接 /l/?uddg=...
            var href = ExtractRealUrl(rawHref);

            // 提取摘要
            var snippetMatch = Regex.Match(body, @"<a[^>]*class=""[^""]*result__snippet[^""]*""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var snippet = snippetMatch.Success ? StripHtml(snippetMatch.Groups[1].Value) : "";

            if (!string.IsNullOrWhiteSpace(href) && (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(snippet)))
            {
                results.Add(new WebSearchHit(title, href, snippet));
            }
        }

        return results;
    }

    private static string ExtractRealUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return "";
        if (href.StartsWith("//")) href = "https:" + href;
        if (href.Contains("uddg="))
        {
            var match = Regex.Match(href, @"uddg=([^&]+)");
            if (match.Success)
            {
                try
                {
                    return Uri.UnescapeDataString(match.Groups[1].Value);
                }
                catch
                {
                    // Ignore decoding error
                }
            }
        }
        return href;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var clean = Regex.Replace(html, @"<[^>]+>", " ");
        clean = System.Net.WebUtility.HtmlDecode(clean);
        return Regex.Replace(clean, @"s+", " ").Trim();
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
