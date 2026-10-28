using Avalonia;
using Avalonia.Platform;
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

    private readonly Func<bool>? _highContrastProbe;
    private bool _installed;
    private bool _forced;

    /// <summary>构造主题宿主。</summary>
    /// <param name="settings">UI 自有设置存储（主题的唯一真源）。</param>
    /// <param name="dispatcher">UI 线程封送器（<c>Changed</c> 可能在后台线程触发）。</param>
    /// <param name="application">当前 Avalonia 应用，用于改主题变体。</param>
    /// <param name="highContrastProbe">
    /// 系统高对比度探针（20 篇 P-02 / THM-10）；缺省视为未开启。给成委托而不是直接读平台设置，
    /// 是为了让「系统开了高对比度就压过用户选择」这条规则能在无 GUI 的测试里验证。
    /// </param>
    public ThemeHost(
        IUiSettingsStore settings,
        IDispatcher dispatcher,
        Application application,
        Func<bool>? highContrastProbe = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _dispatcher = dispatcher;
        _application = application;
        _highContrastProbe = highContrastProbe;
    }

    /// <summary>系统高对比度是否正在压过用户选择（20 篇 §15 优先级：系统/无障碍 &gt; 主题包 &gt; 用户覆盖）。</summary>
    public bool IsHighContrastForced => _highContrastProbe?.Invoke() ?? false;

    /// <summary>高对比度接管状态变化（开/关），供强调色宿主与设置页刷新自己的呈现。</summary>
    public event Action<bool>? ForcedStateChanged;

    /// <summary>读平台设置里的高对比度开关；拿不到平台设置时视为未开启。</summary>
    /// <param name="application">当前应用（可为 null）。</param>
    public static bool IsSystemHighContrast(Application? application)
    {
        try
        {
            return application?.PlatformSettings?.GetColorValues().ContrastPreference
                == ColorContrastPreference.High;
        }
        catch (Exception)
        {
            // 平台设置在某些后端上会抛（headless / 早期启动）；读不到就当没开，不能因此让主题装配失败。
            return false;
        }
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

        // 系统里切换高对比度/深色偏好时重新装配：不能等下一次保存设置才生效。
        if (_application.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += OnSystemColorValuesChanged;
        }
    }

    /// <summary>重新读设置并装配主题（系统配色变化、高对比度开关变化后调用）。</summary>
    public void Refresh()
    {
        if (_installed)
        {
            Apply(_settings.Load());
        }
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
        if (_application.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged -= OnSystemColorValuesChanged;
        }
    }

    private void OnSystemColorValuesChanged(object? sender, PlatformColorValues e) =>
        _dispatcher.Post(Refresh);

    private void OnSettingsChanged(UiSettings settings)
    {
        // 05 篇 §2：设置保存可能发生在后台线程，改 Application 状态必须回到 UI 线程。
        _dispatcher.Post(() => Apply(settings));
    }

    private void Apply(UiSettings settings)
    {
        // 20 篇 §15：系统高对比度是最高优先级，用户选的主题一律让位（设置页会把这些行置灰并注明「已由系统接管」）。
        var forced = IsHighContrastForced;
        var variant = forced ? HighContrastVariant : ToVariant(settings.Theme);
        if (_application.RequestedThemeVariant != variant)
        {
            _application.RequestedThemeVariant = variant;
        }

        if (_forced != forced)
        {
            _forced = forced;
            ForcedStateChanged?.Invoke(forced);
        }
    }
}
