using System.Text.Json;

namespace CkanUI.Localization;

/// <summary>
/// 语言包 JSON 加载器。格式：
/// <c>{ "locale": "zh-CN", "nativeName": "简体中文", "englishName": "Chinese, Simplified",
///      "strings": { "Ui_NavRail_ManageMods": "模组" } }</c>。
/// 关键约定：JSON 损坏或字段非法时**不抛异常**，返回 null 并记 Warn —— 语言包是随包数据，
/// 坏掉一个语言不该带崩界面（降级后走回退链，最终显示 key）。
/// </summary>
public static class LanguagePackLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>解析一段 JSON。</summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="localeHint">文件名推出的语言标识，用于 JSON 里没写 locale 或写坏时的兜底。</param>
    /// <param name="log">日志出口。</param>
    /// <returns>解析成功返回语言包；失败返回 null（已记 Warn）。</returns>
    public static LanguagePack? TryParse(string json, string localeHint, ILocalizationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (string.IsNullOrWhiteSpace(json))
        {
            log.Warn($"[i18n] 语言包内容为空（提示语言 {localeHint}），已跳过。");
            return null;
        }

        PackDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PackDto>(json, Options);
        }
        catch (JsonException ex)
        {
            log.Warn($"[i18n] 语言包 JSON 损坏（提示语言 {localeHint}）：{ex.Message}；已降级为回退链。");
            return null;
        }

        if (dto is null)
        {
            log.Warn($"[i18n] 语言包 JSON 为 null（提示语言 {localeHint}），已跳过。");
            return null;
        }

        var locale = string.IsNullOrWhiteSpace(dto.Locale) ? localeHint : dto.Locale!.Trim();
        if (string.IsNullOrWhiteSpace(locale))
        {
            log.Warn("[i18n] 语言包缺少 locale 且无文件名可推断，已跳过。");
            return null;
        }

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        var dropped = 0;
        foreach (var pair in dto.Strings ?? new Dictionary<string, string?>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                // 空键、null 值与纯空白值都视为缺译（16 篇 §9 上传前校验口径）。
                dropped++;
                continue;
            }

            strings[pair.Key.Trim()] = pair.Value;
        }

        if (dropped > 0)
        {
            log.Warn($"[i18n] 语言包 {locale} 有 {dropped} 条空键/空值，已跳过。");
        }

        return new LanguagePack(locale, dto.NativeName ?? locale, dto.EnglishName ?? locale, strings);
    }

    /// <summary>从文件读取一个语言包（文件名即语言标识，如 zh-CN.json）。</summary>
    /// <param name="path">JSON 文件路径。</param>
    /// <param name="log">日志出口。</param>
    /// <returns>成功返回语言包，失败返回 null（已记 Warn）。</returns>
    public static LanguagePack? TryLoadFile(string path, ILocalizationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("文件路径不能为空。", nameof(path));
        }

        try
        {
            var hint = Path.GetFileNameWithoutExtension(path);
            return TryParse(File.ReadAllText(path), hint, log);
        }
        catch (IOException ex)
        {
            log.Warn($"[i18n] 读取语言包失败 {path}：{ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Warn($"[i18n] 无权读取语言包 {path}：{ex.Message}");
            return null;
        }
    }

    /// <summary>加载目录下全部 *.json 语言包；目录不存在时返回空列表（已记 Warn）。</summary>
    /// <param name="directory">目录路径。</param>
    /// <param name="log">日志出口。</param>
    public static IReadOnlyList<LanguagePack> LoadDirectory(string directory, ILocalizationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("目录不能为空。", nameof(directory));
        }

        if (!Directory.Exists(directory))
        {
            log.Warn($"[i18n] 语言包目录不存在：{directory}；改用内置语言包。");
            return [];
        }

        var packs = new List<LanguagePack>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var pack = TryLoadFile(file, log);
            if (pack is not null)
            {
                packs.Add(pack);
            }
        }

        return packs;
    }

    /// <summary>JSON 反序列化目标（仅本类内部使用）。</summary>
    private sealed class PackDto
    {
        public string? Locale { get; set; }

        public string? NativeName { get; set; }

        public string? EnglishName { get; set; }

        public Dictionary<string, string?>? Strings { get; set; }
    }
}
