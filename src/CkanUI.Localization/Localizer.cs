using System.Globalization;

namespace CkanUI.Localization;

/// <summary>最小实现：字典 + 回退到 key。后续由 E4 里程碑替换为三层解析实现。</summary>
public sealed class Localizer : ILocalizer
{
    private readonly Dictionary<string, string> _strings;

    public Localizer(Dictionary<string, string>? strings = null, string? locale = null)
    {
        _strings = strings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        CurrentLocale = locale ?? CultureInfo.CurrentUICulture.Name;
    }

    public string CurrentLocale { get; private set; }

    public string Get(string key) => _strings.TryGetValue(key, out var v) ? v : key;

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
