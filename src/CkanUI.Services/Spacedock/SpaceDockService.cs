using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Spacedock;

/// <summary>
/// SpaceDock 公共 API 客户端（https://spacedock.info/api，2026-11 的 v1 接口）。
/// </summary>
/// <remarks>
/// 这里刻意只用 <see cref="JsonDocument"/>，不引入一套与远端字段一一对应的 DTO：
/// SpaceDock 的浏览行与详情行字段并不一致（详情多 description，搜索行少描述），
/// 用宽容读取比让反序列化在缺字段时整页失败更符合实际。
/// </remarks>
public sealed class SpaceDockService : ISpaceDockService, IDisposable
{
    private const string BaseUrl = "https://spacedock.info";
    private const int MaxPageSize = 500;

    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    /// <summary>默认构造：直连 SpaceDock。</summary>
    public SpaceDockService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsClient: true)
    {
    }

    /// <summary>测试构造：注入 fake handler，验证请求形状与 JSON 容错。</summary>
    public SpaceDockService(HttpMessageHandler handler)
        : this(new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) }, ownsClient: true)
    {
    }

    /// <summary>宿主构造：复用外部 HttpClient（生命周期由调用方管理）。</summary>
    public SpaceDockService(HttpClient client)
        : this(client, ownsClient: false)
    {
    }

    private SpaceDockService(HttpClient client, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _ownsClient = ownsClient;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SpaceDockBrowsePage>> BrowseAsync(
        int gameId,
        int page,
        int pageSize,
        SpaceDockSortOrder order,
        bool ascending,
        CancellationToken ct = default)
    {
        if (gameId <= 0)
        {
            return OperationResult<SpaceDockBrowsePage>.Fail("SpaceDock 游戏 ID 无效", $"gameId={gameId}");
        }

        if (page < 1)
        {
            return OperationResult<SpaceDockBrowsePage>.Fail("页码必须从 1 开始", $"page={page}");
        }

        var count = Math.Clamp(pageSize, 1, MaxPageSize);
        var orderBy = order switch
        {
            SpaceDockSortOrder.Name => "name",
            SpaceDockSortOrder.Updated => "updated",
            _ => "created",
        };
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{BaseUrl}/api/browse?game_id={gameId}&page={page}&count={count}&orderby={orderBy}&order={(ascending ? "asc" : "desc")}");

        var body = await GetStringAsync(url, ct).ConfigureAwait(false);
        if (!body.IsSuccess)
        {
            return OperationResult<SpaceDockBrowsePage>.Fail(body.Message ?? "SpaceDock 请求失败", body.Detail, body.Retryable);
        }

        try
        {
            using var document = JsonDocument.Parse(body.Value!);
            var root = document.RootElement;
            var mods = ReadModArray(root, "result");
            return OperationResult<SpaceDockBrowsePage>.Ok(new SpaceDockBrowsePage(
                GetInt32(root, "total"),
                GetInt32(root, "page") is var current && current > 0 ? current : page,
                Math.Max(GetInt32(root, "pages"), 1),
                mods));
        }
        catch (JsonException ex)
        {
            return OperationResult<SpaceDockBrowsePage>.Fail("SpaceDock 返回了无法解析的 JSON", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SpaceDockModInfo>>> SearchAsync(
        string query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return OperationResult<IReadOnlyList<SpaceDockModInfo>>.Ok(Array.Empty<SpaceDockModInfo>());
        }

        var url = $"{BaseUrl}/api/search/mod?query={Uri.EscapeDataString(query.Trim())}";
        var body = await GetStringAsync(url, ct).ConfigureAwait(false);
        if (!body.IsSuccess)
        {
            return OperationResult<IReadOnlyList<SpaceDockModInfo>>.Fail(body.Message ?? "SpaceDock 搜索失败", body.Detail, body.Retryable);
        }

        try
        {
            using var document = JsonDocument.Parse(body.Value!);
            return OperationResult<IReadOnlyList<SpaceDockModInfo>>.Ok(ReadModArray(document.RootElement, null));
        }
        catch (JsonException ex)
        {
            return OperationResult<IReadOnlyList<SpaceDockModInfo>>.Fail("SpaceDock 搜索返回了无法解析的 JSON", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<SpaceDockModInfo>> GetModAsync(long modId, CancellationToken ct = default)
    {
        if (modId <= 0)
        {
            return OperationResult<SpaceDockModInfo>.Fail("SpaceDock 模组 ID 无效", $"modId={modId}");
        }

        var url = string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/api/mod/{modId}");
        var body = await GetStringAsync(url, ct).ConfigureAwait(false);
        if (!body.IsSuccess)
        {
            return OperationResult<SpaceDockModInfo>.Fail(body.Message ?? "SpaceDock 详情请求失败", body.Detail, body.Retryable);
        }

        try
        {
            using var document = JsonDocument.Parse(body.Value!);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OperationResult<SpaceDockModInfo>.Fail("SpaceDock 详情响应格式不正确");
            }

            return OperationResult<SpaceDockModInfo>.Ok(MapMod(document.RootElement));
        }
        catch (JsonException ex)
        {
            return OperationResult<SpaceDockModInfo>.Fail("SpaceDock 详情返回了无法解析的 JSON", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SpaceDockGameInfo>>> GetGamesAsync(CancellationToken ct = default)
    {
        var body = await GetStringAsync($"{BaseUrl}/api/games", ct).ConfigureAwait(false);
        if (!body.IsSuccess)
        {
            return OperationResult<IReadOnlyList<SpaceDockGameInfo>>.Fail(body.Message ?? "SpaceDock 游戏列表请求失败", body.Detail, body.Retryable);
        }

        try
        {
            using var document = JsonDocument.Parse(body.Value!);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return OperationResult<IReadOnlyList<SpaceDockGameInfo>>.Fail("SpaceDock 游戏列表格式不正确");
            }

            var games = new List<SpaceDockGameInfo>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = GetInt32(item, "id");
                var name = GetString(item, "name");
                if (id <= 0 || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                games.Add(new SpaceDockGameInfo(id, name, GetString(item, "short") ?? string.Empty));
            }

            return OperationResult<IReadOnlyList<SpaceDockGameInfo>>.Ok(games);
        }
        catch (JsonException ex)
        {
            return OperationResult<IReadOnlyList<SpaceDockGameInfo>>.Fail("SpaceDock 游戏列表返回了无法解析的 JSON", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private async Task<OperationResult<string>> GetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                                (int)response.StatusCode >= 500;
                return OperationResult<string>.Fail(
                    $"SpaceDock 请求失败（HTTP {(int)response.StatusCode}）",
                    url,
                    retryable);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body)
                ? OperationResult<string>.Fail("SpaceDock 返回了空响应", url, retryable: true)
                : OperationResult<string>.Ok(body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return OperationResult<string>.Fail("SpaceDock 请求超时", url, retryable: true);
        }
        catch (HttpRequestException ex)
        {
            return OperationResult<string>.Fail("无法连接 SpaceDock", ex.Message, retryable: true);
        }
    }

    private static IReadOnlyList<SpaceDockModInfo> ReadModArray(JsonElement root, string? propertyName)
    {
        var array = root;
        if (propertyName is not null)
        {
            if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SpaceDockModInfo>();
            }

            array = property;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SpaceDockModInfo>();
        }

        var mods = new List<SpaceDockModInfo>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                mods.Add(MapMod(item));
            }
        }

        return mods;
    }

    private static SpaceDockModInfo MapMod(JsonElement item)
    {
        var id = GetInt64(item, "id");
        var url = MakeAbsolute(GetString(item, "url"));
        var versions = new List<SpaceDockVersionInfo>();
        if (item.TryGetProperty("versions", out var versionArray) && versionArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var version in versionArray.EnumerateArray())
            {
                if (version.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var downloadPath = GetString(version, "download_path");
                if (string.IsNullOrWhiteSpace(downloadPath))
                {
                    continue;
                }

                versions.Add(new SpaceDockVersionInfo(
                    GetString(version, "friendly_version") ?? "未知版本",
                    GetString(version, "game_version"),
                    MakeAbsolute(downloadPath),
                    GetString(version, "changelog"),
                    GetInt64(version, "downloads"),
                    GetDate(version, "created")));
            }
        }

        return new SpaceDockModInfo(
            id,
            GetString(item, "name") ?? $"SpaceDock #{id}",
            GetString(item, "author") ?? string.Empty,
            GetString(item, "short_description") ?? string.Empty,
            GetString(item, "description") ?? string.Empty,
            GetInt64(item, "downloads"),
            GetInt32(item, "followers"),
            GetString(item, "license"),
            MakeOptionalAbsolute(GetString(item, "website")),
            MakeOptionalAbsolute(GetString(item, "source_code")),
            MakeOptionalAbsolute(GetString(item, "background")),
            url,
            versions);
    }

    private static string MakeAbsolute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return trimmed;
        }

        return trimmed.StartsWith("//", StringComparison.Ordinal)
            ? $"https:{trimmed}"
            : $"{BaseUrl}/{trimmed.TrimStart('/')}";
    }

    private static string? MakeOptionalAbsolute(string? value)
    {
        var result = MakeAbsolute(value);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0,
        };
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        var number = GetInt64(element, propertyName);
        return number is > int.MaxValue or < int.MinValue ? 0 : (int)number;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string propertyName)
    {
        var text = GetString(element, propertyName);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }
}
