namespace CkanUI.Localization;

/// <summary>一种语言的语言包：locale + 显示名 + 键值表（文件格式见 i18n/README.md）。</summary>
public sealed record LanguagePack
{
    /// <summary>创建语言包。</summary>
    /// <param name="locale">BCP-47 语言标识。</param>
    /// <param name="nativeName">本地名。</param>
    /// <param name="englishName">英文名。</param>
    /// <param name="strings">键 → 译文；键用 Ordinal 比较。</param>
    public LanguagePack(string locale, string nativeName, string englishName, IReadOnlyDictionary<string, string> strings)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("语言标识不能为空。", nameof(locale));
        }

        Locale = locale;
        NativeName = string.IsNullOrWhiteSpace(nativeName) ? locale : nativeName;
        EnglishName = string.IsNullOrWhiteSpace(englishName) ? locale : englishName;
        Strings = strings ?? throw new ArgumentNullException(nameof(strings));
    }

    /// <summary>BCP-47 语言标识。</summary>
    public string Locale { get; }

    /// <summary>本地名。</summary>
    public string NativeName { get; }

    /// <summary>英文名。</summary>
    public string EnglishName { get; }

    /// <summary>键 → 译文。</summary>
    public IReadOnlyDictionary<string, string> Strings { get; }

    /// <summary>创建空包（JSON 损坏时降级用，不抛异常）。</summary>
    public static LanguagePack Empty(string locale) =>
        new(locale, locale, locale, new Dictionary<string, string>(StringComparer.Ordinal));
}
