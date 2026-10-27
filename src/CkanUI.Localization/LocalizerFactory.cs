namespace CkanUI.Localization;

/// <summary>
/// 装配本地化实例（DI 组装根只调用本类，不感知资源细节）。
/// 语言包来源优先级：磁盘目录（随包 i18n/packs，可翻译后热更新）→ 程序集内嵌资源（兜底，保证单文件也能跑）。
/// </summary>
public static class LocalizerFactory
{
    /// <summary>内嵌语言包的资源名前缀（见 CkanUI.Localization.csproj 的 LogicalName）。</summary>
    private const string EmbeddedPrefix = "CkanUI.Localization.Packs.";

    /// <summary>只用内嵌语言包创建实例。</summary>
    /// <param name="options">装配选项。</param>
    public static Localizer Create(LocalizationOptions? options = null) =>
        new(LoadEmbedded(options?.ResolveLog() ?? NullLocalizationLog.Instance), options);

    /// <summary>磁盘目录优先、内嵌兜底创建实例（同 locale 时磁盘版本覆盖内嵌版本）。</summary>
    /// <param name="directory">语言包目录（如随包的 i18n/packs）。</param>
    /// <param name="options">装配选项。</param>
    public static Localizer CreateFromDirectory(string directory, LocalizationOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("目录不能为空。", nameof(directory));
        }

        var log = options?.ResolveLog() ?? NullLocalizationLog.Instance;
        var merged = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in LoadEmbedded(log))
        {
            merged[pack.Locale] = pack;
        }

        foreach (var pack in LanguagePackLoader.LoadDirectory(directory, log))
        {
            merged[pack.Locale] = pack;
        }

        return new Localizer(merged.Values, options);
    }

    /// <summary>读取程序集内嵌的全部语言包。</summary>
    /// <param name="log">日志出口。</param>
    public static IReadOnlyList<LanguagePack> LoadEmbedded(ILocalizationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var assembly = typeof(LocalizerFactory).Assembly;
        var packs = new List<LanguagePack>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(EmbeddedPrefix, StringComparison.Ordinal) ||
                !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var hint = name[EmbeddedPrefix.Length..^".json".Length];
            var pack = LanguagePackLoader.TryParse(reader.ReadToEnd(), hint, log);
            if (pack is not null)
            {
                packs.Add(pack);
            }
        }

        if (packs.Count == 0)
        {
            // 内嵌资源缺失说明构建脚本漏了；返回空集合，调用方仍能工作（全部回退到 key）。
            log.Warn("[i18n] 程序集内没有找到任何内嵌语言包，所有文案将回退为 key。");
        }

        return packs;
    }
}
