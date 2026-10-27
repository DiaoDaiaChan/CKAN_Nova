using Avalonia;
using Avalonia.Controls;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;

namespace CkanUI.App.Localization;

/// <summary>
/// 把 <see cref="ILocalizer"/> 的快照灌进 Avalonia 资源字典，并在切语言时整表热替换
/// （docs/ops/i18n-guide.md §6.2：只支持整表替换，避免中间态混语言）。
/// </summary>
public sealed class LocalizationHost : IDisposable
{
    private readonly ILocalizer _localizer;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    private int _dictionaryIndex = -1;
    private bool _disposed;

    /// <summary>初始化宿主。</summary>
    /// <param name="localizer">本地化实现。</param>
    /// <param name="dispatcher">UI 线程封送器（LanguagesChanged 可能来自任意线程）。</param>
    /// <param name="application">目标应用实例。</param>
    public LocalizationHost(ILocalizer localizer, IDispatcher dispatcher, Application application)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _application = application ?? throw new ArgumentNullException(nameof(application));
    }

    /// <summary>
    /// 首次灌入：必须在窗口显示之前调用，否则首帧会看到裸 key。
    /// </summary>
    public void Install()
    {
        var resources = _application.Resources;
        resources.MergedDictionaries.Add(Build(_localizer));
        _dictionaryIndex = resources.MergedDictionaries.Count - 1;
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>把要显示的语言名做成可直接绑定的文本（如「简体中文 · Chinese, Simplified」）。</summary>
    /// <param name="locale">BCP-47 标识。</param>
    public string Describe(string locale) =>
        _localizer.AvailableLocales.FirstOrDefault(l => string.Equals(l.Name, locale, StringComparison.OrdinalIgnoreCase))
            ?.DisplayText ?? locale;

    private static ResourceDictionary Build(ILocalizer localizer)
    {
        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in localizer.Snapshot())
        {
            // 键名与 LocalizationKeys 常量一致，XAML 里可直接 {DynamicResource Ui_NavRail_ManageMods}。
            dictionary[key] = value;
        }

        return dictionary;
    }

    private void OnLanguageChanged(object? sender, System.Globalization.CultureInfo culture)
    {
        // 先建新表再替换：绝不能让控件读到半张表。
        var replacement = Build(_localizer);
        _dispatcher.Post(() =>
        {
            if (_disposed || _dictionaryIndex < 0) return;
            var dictionaries = _application.Resources.MergedDictionaries;
            if (_dictionaryIndex < dictionaries.Count)
            {
                dictionaries[_dictionaryIndex] = replacement;
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localizer.LanguageChanged -= OnLanguageChanged;
    }
}
