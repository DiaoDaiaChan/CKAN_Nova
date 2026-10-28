namespace CkanUI.Localization;

/// <summary>
/// 装配本地化实例（DI 组装根只调用本类，不感知资源细节）。
/// 语言包来源优先级：磁盘目录（随包 i18n/packs，可翻译后热更新）→ 程序集内嵌资源（兜底，保证单文件也能跑）。
/// </summary>
public static class LocalizerFactory
{
    /// <summary>内嵌语言包的资源名前缀（见 CkanUI.Localization.csproj 的 LogicalName）。</summary>
    private const string EmbeddedPrefix = "CkanUI.Localization.Packs.";

    /// <summary>伪本地化开关的环境变量名（CI 截图与手工验收用，见 16 篇 §7.3）。</summary>
    public const string PseudoLocaleEnvironmentVariable = "CKAN_UI_PSEUDO_LOCALE";

    /// <summary>只用内嵌语言包创建实例。</summary>
    /// <param name="options">装配选项。</param>
    public static Localizer Create(LocalizationOptions? options = null)
    {
        var effective = ApplyEnvironmentDefaults(options);
        var log = effective?.ResolveLog() ?? NullLocalizationLog.Instance;
        return new Localizer(LoadEmbedded(log), effective);
    }

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

        return new Localizer(merged.Values, ApplyEnvironmentDefaults(options));
    }

    /// <summary>
    /// 把环境变量里的调试开关折进选项（当前只有伪本地化）。返回同一个实例便于调用方串联。
    /// </summary>
    /// <remarks>
    /// 为什么走环境变量：伪本地化是"截图验收时才开"的调试开关，若做成配置项就要经配置层、
    /// 设置页、存盘格式三道变更；而它必须能进发布版（QA 在客户机器上也要能开）又不能被误开，
    /// 环境变量正好满足"默认关、临时开、不进用户配置"。
    /// </remarks>
    /// <param name="options">装配选项；null 表示调用方没给，此时不做任何改写。</param>
    private static LocalizationOptions? ApplyEnvironmentDefaults(LocalizationOptions? options)
    {
        if (options is null || options.PseudoLocalize)
        {
            return options;
        }

        var raw = Environment.GetEnvironmentVariable(PseudoLocaleEnvironmentVariable);
        if (raw is null)
        {
            return options;
        }

        var value = raw.Trim();
        if (value.Length == 0)
        {
            return options;
        }

        if (value.Equals("1", StringComparison.Ordinal) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            options.PseudoLocalize = true;
        }

        return options;
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
