using System.Text.Json;

namespace CkanUI.Localization;

/// <summary>
/// 用扁平 JSON（<c>{"Ui_X_Caption":"标题"}</c>）装配的元数据字符串来源（D11 的 L2）。
/// </summary>
/// <remarks>
/// 为什么单独一类而不是让元数据层走 <see cref="LanguagePackLoader"/>：元数据包（模组译文、
/// 术语表、人工覆盖）是按语言分目录的稀疏词表，只覆盖"界面里引用到的模组字段"那么几十个键，
/// 用语言包加载器会要求它声明 locale/nativeName 并进入覆盖面统计，反而把元数据当成"界面翻译"。
/// 这里只认键值，语言由文件名（或调用方显式给的 locale）决定。
/// </remarks>
public sealed class DictionaryStringSource : IStringSource
{
    private readonly IReadOnlyDictionary<string, string> _strings;

    /// <summary>创建来源。</summary>
    /// <param name="name">层名（诊断用，如 meta）。</param>
    /// <param name="strings">键值表（大小写敏感的 Ordinal 比较，与界面语言包一致）。</param>
    /// <param name="locale">只服务某个语言；null 表示任何语言都服务（如术语表）。</param>
    public DictionaryStringSource(string name, IReadOnlyDictionary<string, string> strings, string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(strings);

        Name = string.IsNullOrWhiteSpace(name) ? "meta" : name;
        Locale = string.IsNullOrWhiteSpace(locale) ? null : locale.Trim();
        _strings = strings;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>本来源服务的语言；null 表示不限语言。</summary>
    public string? Locale { get; }

    /// <summary>词条数（诊断用）。</summary>
    public int Count => _strings.Count;

    /// <inheritdoc />
    public bool TryGet(string locale, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (Locale is not null && !Locale.Equals(locale, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!_strings.TryGetValue(key, out var found) || string.IsNullOrEmpty(found))
        {
            return false;
        }

        value = found;
        return true;
    }

    /// <summary>
    /// 解析一份扁平 JSON；带 <c>strings</c> 子对象的语言包形状也接受
    /// （便于把随包语言包直接当元数据源试跑）。内容坏掉时返回 null 并记 Warn，不抛异常。
    /// </summary>
    /// <param name="name">层名。</param>
    /// <param name="json">JSON 文本。</param>
    /// <param name="locale">只服务某个语言；null 表示不限。</param>
    /// <param name="log">日志出口。</param>
    public static DictionaryStringSource? TryParse(string name, string json, string? locale = null, ILocalizationLog? log = null)
    {
        log ??= NullLocalizationLog.Instance;
        if (string.IsNullOrWhiteSpace(json))
        {
            log.Warn($"[i18n] 元数据字符串来源「{name}」内容为空，已跳过。");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                log.Warn($"[i18n] 元数据字符串来源「{name}」根节点不是对象，已跳过。");
                return null;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("strings", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                root = nested;
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = property.Value.GetString();
                if (!string.IsNullOrEmpty(property.Name) && !string.IsNullOrEmpty(text))
                {
                    strings[property.Name] = text;
                }
            }

            if (strings.Count == 0)
            {
                log.Warn($"[i18n] 元数据字符串来源「{name}」没有可用词条，已跳过。");
                return null;
            }

            return new DictionaryStringSource(name, strings, locale);
        }
        catch (JsonException ex)
        {
            log.Warn($"[i18n] 元数据字符串来源「{name}」JSON 解析失败：{ex.Message}；已跳过。");
            return null;
        }
    }

    /// <summary>载入目录下全部 <c>*.json</c>（文件名去扩展名即语言标识）；目录不存在时返回空集合并记 Warn。</summary>
    /// <param name="directory">目录（如 <c>i18n/meta</c>）。</param>
    /// <param name="layerName">层名前缀，最终层名为 <c>layerName:locale</c>。</param>
    /// <param name="log">日志出口。</param>
    public static IReadOnlyList<DictionaryStringSource> LoadDirectory(string directory, string? layerName = null, ILocalizationLog? log = null)
    {
        log ??= NullLocalizationLog.Instance;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            log.Warn($"[i18n] 元数据目录不存在：{directory}；该层将不生效。");
            return Array.Empty<DictionaryStringSource>();
        }

        var prefix = string.IsNullOrWhiteSpace(layerName) ? "meta" : layerName;
        var sources = new List<DictionaryStringSource>();
        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                log.Warn($"[i18n] 读取元数据文件失败：{path}（{ex.Message}）。");
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                log.Warn($"[i18n] 无权读取元数据文件：{path}（{ex.Message}）。");
                continue;
            }

            var locale = Path.GetFileNameWithoutExtension(path);
            var source = TryParse($"{prefix}:{locale}", json, locale, log);
            if (source is not null)
            {
                sources.Add(source);
            }
        }

        return sources;
    }
}
