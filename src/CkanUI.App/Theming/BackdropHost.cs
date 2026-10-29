using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using CkanUI.Services.Abstractions;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 窗口材质与背景宿主（20 篇 P-04、THM-04）：管理窗口材质透明效果（Mica / AcrylicBlur / None）。
/// </summary>
public sealed class BackdropHost : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(BackdropHost));

    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private bool _installed;

    public BackdropHost(IUiSettingsStore settings, IDispatcher dispatcher, IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(desktop);

        _settings = settings;
        _dispatcher = dispatcher;
        _desktop = desktop;
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

    public static IReadOnlyList<WindowTransparencyLevel> ResolveHints(string? backdropKind)
    {
        return backdropKind?.Trim().ToLowerInvariant() switch
        {
            "mica" => new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.None },
            "acrylic" or "blur" => new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None },
            "none" or "solid" => new[] { WindowTransparencyLevel.None },
            _ => new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Mica, WindowTransparencyLevel.None }
        };
    }

    public void Apply(UiSettings settings)
    {
        if (_desktop.MainWindow is not { } window)
        {
            return;
        }

        window.TransparencyLevelHint = ResolveHints(settings.BackdropKind);
        Log.Info($"已设置窗口材质首选项：{settings.BackdropKind ?? "auto"}");
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
