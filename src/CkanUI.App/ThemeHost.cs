using Avalonia;
using Avalonia.Styling;

using CkanUI.Services.Abstractions;

namespace CkanUI.App;

/// <summary>
/// 主题宿主（THM-01/THM-02、03 篇 §21、20 篇 P-08）：把 <see cref="UiSettings.Theme"/> 落到
/// <see cref="Application.RequestedThemeVariant"/>，并在设置保存后立即生效。
/// </summary>
/// <remarks>
/// 为什么不是 VM 直接改 Application：视图模型层禁止引用 Avalonia（架构铁律 1），
/// 所以「读设置 → 改主题」这条线只能落在 App 项目里，通过 <see cref="IUiSettingsStore.Changed"/> 订阅。
/// 主题变体切换只换 <c>Resources/Tokens.axaml</c> 里 ThemeDictionaries 的解析结果，
/// 页面与控件全部走 DynamicResource，不需要重建。
/// </remarks>
public sealed class ThemeHost : IDisposable
{
    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    /// <summary>
    /// 高对比度变体。Avalonia 12 的 <see cref="ThemeVariant"/> 只内置 Light/Dark/Default，
    /// 高对比度用自定义 key（与 Tokens.axaml 的 ThemeDictionaries key 一致）并继承 Dark 兜底。
    /// </summary>
    public static readonly ThemeVariant HighContrastVariant = new("HighContrast", ThemeVariant.Dark);

    private bool _installed;

    /// <summary>构造主题宿主。</summary>
    /// <param name="settings">UI 自有设置存储（主题的唯一真源）。</param>
    /// <param name="dispatcher">UI 线程封送器（<c>Changed</c> 可能在后台线程触发）。</param>
    /// <param name="application">当前 Avalonia 应用，用于改主题变体。</param>
    public ThemeHost(IUiSettingsStore settings, IDispatcher dispatcher, Application application)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _dispatcher = dispatcher;
        _application = application;
    }

    /// <summary>从磁盘读一次设置、应用主题，并订阅后续变更。可重复调用（幂等）。</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Apply(_settings.Load());
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>设置字符串 → 主题变体。未知值（含空）一律跟随系统（20 篇 P-08「跟随系统」）。</summary>
    /// <param name="theme">light / dark / high-contrast / system。</param>
    public static ThemeVariant ToVariant(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            "high-contrast" or "highcontrast" or "contrast" => HighContrastVariant,
            _ => ThemeVariant.Default,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_installed)
        {
            return;
        }

        _installed = false;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(UiSettings settings)
    {
        // 05 篇 §2：设置保存可能发生在后台线程，改 Application 状态必须回到 UI 线程。
        _dispatcher.Post(() => Apply(settings));
    }

    private void Apply(UiSettings settings)
    {
        var variant = ToVariant(settings.Theme);
        if (_application.RequestedThemeVariant != variant)
        {
            _application.RequestedThemeVariant = variant;
        }
    }
}
