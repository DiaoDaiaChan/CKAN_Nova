using Avalonia;

using CkanUI.Services.Abstractions;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 圆角比例宿主（20 篇 P-05、THM-05）：把 <see cref="UiSettings.RadiusScale"/> 动态注入到 App 资源 token，
/// 支持 none (0x 直角), standard (1x 现代微圆角), round (1.5x 饱满圆角)。
/// </summary>
public sealed class RadiusHost : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(RadiusHost));

    public const string CornerRadiusSmallKey = "CkanCornerRadiusSmall";
    public const string CornerRadiusMediumKey = "CkanCornerRadiusMedium";
    public const string CornerRadiusLargeKey = "CkanCornerRadiusLarge";

    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    private bool _installed;

    public RadiusHost(IUiSettingsStore settings, IDispatcher dispatcher, Application application)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _dispatcher = dispatcher;
        _application = application;
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
    }

    private void OnSettingsChanged(UiSettings settings)
    {
        _dispatcher.Post(() => Apply(settings));
    }

    public static double ResolveScale(string? scaleStr) =>
        scaleStr?.Trim().ToLowerInvariant() switch
        {
            "none" or "0" or "sharp" => 0.0,
            "round" or "large" or "1.5" => 1.5,
            _ => 1.0 // standard
        };

    public void Apply(UiSettings settings)
    {
        var scale = ResolveScale(settings.RadiusScale);

        if (Math.Abs(scale - 1.0) < 0.01)
        {
            // 恢复默认基准圆角：Small 6, Medium 10, Large 14
            RemoveOverride(CornerRadiusSmallKey);
            RemoveOverride(CornerRadiusMediumKey);
            RemoveOverride(CornerRadiusLargeKey);
            Log.Info("圆角恢复为标准基线 (Small 6, Med 10, Large 14)");
            return;
        }

        _application.Resources[CornerRadiusSmallKey] = new CornerRadius(Math.Round(6 * scale));
        _application.Resources[CornerRadiusMediumKey] = new CornerRadius(Math.Round(10 * scale));
        _application.Resources[CornerRadiusLargeKey] = new CornerRadius(Math.Round(14 * scale));

        Log.Info($"已动态应用圆角缩放：{scale}x");
    }

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
    }
}
