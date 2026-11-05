using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Settings;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 强调色宿主（20 篇 P-03、THM-03）：把 <see cref="UiSettings.AccentColor"/> 动态注入到 App 资源 token，
/// 联动更新 CkanAccentBrush、CkanPrimaryBrush、CkanAccentSubtleBrush 等。
/// </summary>
public sealed class AccentHost : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AccentHost));

    public const string AccentBrushKey = "CkanAccentBrush";
    public const string PrimaryBrushKey = "CkanPrimaryBrush";
    public const string AccentSubtleBrushKey = "CkanAccentSubtleBrush";
    public const string NavigationSelectionBrushKey = "CkanNavigationSelectionBrush";
    public const string FocusRingBrushKey = "CkanFocusRingBrush";
    public const string AccentHoverBrushKey = "CkanAccentHoverBrush";
    public const string AmbientGlowBrushKey = "CkanAmbientGlowBrush";
    public const string OrbitalLineBrushKey = "CkanOrbitalLineBrush";

    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    private readonly IInstanceService? _instances;
    private bool _installed;
    private string? _appliedColor;

    public AccentHost(
        IUiSettingsStore settings,
        IDispatcher dispatcher,
        Application application,
        IInstanceService? instances = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _dispatcher = dispatcher;
        _application = application;
        _instances = instances;
    }

    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Apply(_settings.Load());
        _settings.Changed += OnSettingsChanged;
        _application.ActualThemeVariantChanged += OnThemeVariantChanged;

        // 切换实例要跟着换色（20 篇 P-07）：覆盖表是按实例名索引的，实例变了生效值就变了。
        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentInstanceChanged;
        }
    }

    private void OnCurrentInstanceChanged(InstanceInfo? instance)
    {
        _dispatcher.Post(Refresh);
    }

    /// <summary>
    /// 强调色的生效值（20 篇 P-07）：实例名在 <see cref="UiSettings.AccentByInstance"/> 里有键就用它，
    /// 否则退回全局 <see cref="UiSettings.AccentColor"/>。
    /// </summary>
    public static string? ResolveAccent(UiSettings settings, string? instanceName)
    {
        if (!string.IsNullOrWhiteSpace(instanceName)
            && settings.AccentByInstance.TryGetValue(instanceName, out var perInstance)
            && !string.IsNullOrWhiteSpace(perInstance))
        {
            return perInstance;
        }

        return settings.AccentColor;
    }

    /// <summary>按当前设置重算一遍强调色（系统高对比度开关切换后由 <c>ThemeHost</c> 驱动）。</summary>
    public void Refresh() => Apply(_settings.Load());

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        _dispatcher.Post(() => Apply(_settings.Current));
    }

    private void OnSettingsChanged(UiSettings settings)
    {
        _dispatcher.Post(() => Apply(settings));
    }

    public void Apply(UiSettings settings)
    {
        var targetColor = ResolveAccent(settings, _instances?.Current?.Name);
        _appliedColor = targetColor;

        if (_application.ActualThemeVariant == ThemeHost.HighContrastVariant)
        {
            // 20 篇 §15：高对比度压过一切自定义颜色——覆盖项全摘掉，只留 Tokens.axaml 里那套合规色板。
            ClearOverrides();
            Log.Info("系统高对比度接管强调色，已跳过自定义颜色");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetColor) || string.Equals(targetColor, AccentPalettes.DefaultKey, StringComparison.OrdinalIgnoreCase))
        {
            // 恢复默认：移除覆盖的资源项，让 Tokens.axaml 的 ThemeDictionaries 原生生效
            ClearOverrides();
            Log.Info("已重置强调色为默认主题色彩");
            return;
        }

        var isDark = ThemeHost.IsDarkVariant(_application.ActualThemeVariant);
        Color? parsedColor = null;

        var preset = AccentPalettes.FindPreset(targetColor);
        if (preset is not null)
        {
            var hex = isDark ? preset.DarkHex : preset.LightHex;
            if (Color.TryParse(hex, out var c))
            {
                parsedColor = c;
            }
        }
        else if (Color.TryParse(targetColor, out var c))
        {
            parsedColor = c;
        }

        if (parsedColor is null)
        {
            Log.Warn($"无法解析强调色：{targetColor}，回退默认");
            return;
        }

        var accentColor = parsedColor.Value;

        // 对比度门禁（20 篇 P-03）：自定义 Hex 可能是用户手改 ckan-ui.json 塞进来的，
        // 设置页的校验挡不住这条路，所以真正落地前再兜一次——不合规就换成同色系的建议色并留日志。
        var themeKey = _application.ActualThemeVariant == ThemeHost.HighContrastVariant
            ? "highcontrast"
            : isDark ? "dark" : "light";
        var verdict = AccentContrast.Validate(ToHex(accentColor), themeKey, isDark);
        if (!verdict.IsValid && verdict.Suggested is { } suggested)
        {
            accentColor = Color.FromRgb(suggested.R, suggested.G, suggested.B);
            Log.Warn($"强调色 {ToHex(parsedColor.Value)} 对比度不足（{verdict.Ratio:0.00}:1），改用建议色 {ToHex(accentColor)}");
        }

        var accentBrush = new SolidColorBrush(accentColor);
        var subtleColor = Color.FromArgb(isDark ? (byte)45 : (byte)24, accentColor.R, accentColor.G, accentColor.B);
        var navColor = Color.FromArgb(isDark ? (byte)60 : (byte)35, accentColor.R, accentColor.G, accentColor.B);
        var hoverColor = isDark
            ? Color.FromArgb(255, (byte)Math.Min(255, accentColor.R + 25), (byte)Math.Min(255, accentColor.G + 25), (byte)Math.Min(255, accentColor.B + 25))
            : Color.FromArgb(255, (byte)Math.Max(0, accentColor.R - 25), (byte)Math.Max(0, accentColor.G - 25), (byte)Math.Max(0, accentColor.B - 25));

        var glowBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.65, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.65, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(isDark ? (byte)0x26 : (byte)0x40, accentColor.R, accentColor.G, accentColor.B), 0),
                new GradientStop(Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), 1)
            }
        };

        var orbitalBrush = new SolidColorBrush(Color.FromArgb(isDark ? (byte)0x18 : (byte)0x18, accentColor.R, accentColor.G, accentColor.B));

        _application.Resources[AccentBrushKey] = accentBrush;
        _application.Resources[PrimaryBrushKey] = accentBrush;
        _application.Resources[AccentSubtleBrushKey] = new SolidColorBrush(subtleColor);
        _application.Resources[NavigationSelectionBrushKey] = new SolidColorBrush(navColor);
        _application.Resources[FocusRingBrushKey] = accentBrush;
        _application.Resources[AccentHoverBrushKey] = new SolidColorBrush(hoverColor);
        _application.Resources[AmbientGlowBrushKey] = glowBrush;
        _application.Resources[OrbitalLineBrushKey] = orbitalBrush;

        Log.Info($"已动态应用强调色：{targetColor} ({accentColor})");
    }

    private void ClearOverrides()
    {
        RemoveOverride(AccentBrushKey);
        RemoveOverride(PrimaryBrushKey);
        RemoveOverride(AccentSubtleBrushKey);
        RemoveOverride(NavigationSelectionBrushKey);
        RemoveOverride(FocusRingBrushKey);
        RemoveOverride(AccentHoverBrushKey);
        RemoveOverride(AmbientGlowBrushKey);
        RemoveOverride(OrbitalLineBrushKey);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void RemoveOverride(string key)
    {
        if (_application.Resources.ContainsKey(key))
        {
            _application.Resources.Remove(key);
        }
    }

    public void Dispose()
    {
        if (!_installed)
        {
            return;
        }

        _installed = false;
        _settings.Changed -= OnSettingsChanged;
        _application.ActualThemeVariantChanged -= OnThemeVariantChanged;
        if (_instances is not null)
        {
            _instances.CurrentChanged -= OnCurrentInstanceChanged;
        }
    }
}
