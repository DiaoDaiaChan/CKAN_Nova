using System.Text.Json;

namespace CkanUI.ModText;

/// <summary>
/// 元数据译文黑名单（17 篇 MTI-07）：命中的 mod 一律不展示译文。
/// </summary>
/// <remarks>
/// 为什么需要：某些 mod 的作者明确不希望译文替换原文，另一些 mod 的译文会让日志/报错
/// 与官方 Wiki 对不上，玩家排查问题时会误判。名单做成数据文件（blocklist.json）而不是硬编码，
/// 才能在不动代码的情况下补一条。
/// 解析一律"最省事且不抛"：坏 JSON 得到空名单，宁可多显示也不让界面崩。
/// </remarks>
public sealed class ModTextBlocklist
{
    /// <summary>空名单。</summary>
    public static ModTextBlocklist Empty { get; } = new([]);

    private readonly HashSet<string> _identifiers;

    private ModTextBlocklist(IEnumerable<string> identifiers) =>
        _identifiers = new HashSet<string>(identifiers, StringComparer.OrdinalIgnoreCase);

    /// <summary>名单条数。</summary>
    public int Count => _identifiers.Count;

    /// <summary>该 mod 是否被屏蔽（忽略大小写；空 identifier 不算命中）。</summary>
    /// <param name="identifier">mod 标识符。</param>
    public bool IsBlocked(string? identifier) =>
        !string.IsNullOrEmpty(identifier) && _identifiers.Contains(identifier);

    /// <summary>名单里的全部标识符（诊断/导出用）。</summary>
    public IReadOnlyCollection<string> Identifiers => _identifiers;

    /// <summary>
    /// 解析名单：接受 <c>["A","B"]</c>、<c>{"blocked":["A"]}</c> 或 <c>{"identifiers":["A"]}</c> 三种形状。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="blocklist">解析结果；失败时为空名单。</param>
    /// <param name="error">失败原因；成功时为 null。</param>
    public static bool TryParse(string? json, out ModTextBlocklist blocklist, out string? error)
    {
        blocklist = Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                blocklist = new ModTextBlocklist(ReadStrings(root));
                return true;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "blocked", "identifiers" })
                {
                    if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                    {
                        blocklist = new ModTextBlocklist(ReadStrings(array));
                        return true;
                    }
                }

                error = "missing-blocked-array";
                return false;
            }

            error = "root-not-array-or-object";
            return false;
        }
        catch (JsonException ex)
        {
            error = "invalid-json: " + ex.Message;
            return false;
        }
    }

    /// <summary>从文件读取名单；文件不存在或解析失败返回空名单，绝不抛。</summary>
    /// <param name="path">blocklist.json 路径。</param>
    public static ModTextBlocklist LoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Empty;
        }

        try
        {
            return TryParse(File.ReadAllText(path), out var blocklist, out _) ? blocklist : Empty;
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    private static IEnumerable<string> ReadStrings(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value.Trim();
            }
        }
    }
}
