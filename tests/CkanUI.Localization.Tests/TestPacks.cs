namespace CkanUI.Localization.Tests;

/// <summary>测试用的语言包与实例构造助手。</summary>
internal static class TestPacks
{
    /// <summary>用 (key, value) 对拼一个语言包。</summary>
    /// <param name="locale">语言标识。</param>
    /// <param name="pairs">键值对。</param>
    public static LanguagePack Pack(string locale, params (string Key, string Value)[] pairs)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            strings[pair.Key] = pair.Value;
        }

        return new LanguagePack(locale, locale, locale, strings);
    }

    /// <summary>
    /// 建一个初始语言确定、且不污染同进程 UI Culture 的实例
    /// （<see cref="LocalizationOptions.ApplyThreadCulture"/> 置 false，否则会改掉 TestRunner 进程的 UI 语言）。
    /// </summary>
    /// <param name="locale">初始语言。</param>
    /// <param name="packs">语言包。</param>
    public static Localizer Create(string locale, params LanguagePack[] packs) =>
        new(
            packs,
            new LocalizationOptions
            {
                DefaultLocale = locale,
                ApplyThreadCulture = false,
                Log = NullLocalizationLog.Instance,
            });

    /// <summary>同上，但把告警收集起来。</summary>
    /// <param name="locale">初始语言。</param>
    /// <param name="log">日志出口。</param>
    /// <param name="packs">语言包。</param>
    public static Localizer Create(string locale, ILocalizationLog log, params LanguagePack[] packs) =>
        new(
            packs,
            new LocalizationOptions
            {
                DefaultLocale = locale,
                ApplyThreadCulture = false,
                Log = log,
            });
}
