using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CkanUI.App.Theming;
using CkanUI.App.Tests.Automation;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 强调色的对比度门禁在 App 层的另一半（20 篇 P-03）：底色常量与 Tokens.axaml 同步、预设色板合规、
/// 宿主对不合规颜色做兜底（手改 ckan-ui.json 也绕不过去）。
/// </summary>
[TestFixture]
public sealed class AccentContrastThemeTests
{
    [Test]
    public void SurfacePoles_MatchTokensAxaml()
    {
        var light = ReadSurfaceBrush("Light");
        var dark = ReadSurfaceBrush("Dark");
        var highContrast = ReadSurfaceBrush("{x:Static app:ThemeHost.HighContrastVariant}");

        Assert.That(ToRgb(light), Is.EqualTo(AccentContrast.LightPole), "浅色主题底色变了就要同步 AccentContrast.LightPole");
        Assert.That(ToRgb(dark), Is.EqualTo(AccentContrast.DarkPole), "深色主题底色变了就要同步 AccentContrast.DarkPole");
        Assert.That(ToRgb(highContrast), Is.EqualTo(AccentContrast.HighContrastPole), "高对比度底色变了就要同步 AccentContrast.HighContrastPole");
    }

    [Test]
    public void Presets_MeetContrastInBothThemes()
    {
        foreach (var preset in AccentPalettes.Presets)
        {
            var light = AccentContrast.Ratio(Parse(preset.LightHex), AccentContrast.LightPole);
            var dark = AccentContrast.Ratio(Parse(preset.DarkHex), AccentContrast.DarkPole);

            Assert.That(light, Is.GreaterThanOrEqualTo(AccentContrast.MinimumRatio), $"{preset.Key} 的浅色档 {preset.LightHex} 与白底只有 {light:0.00}:1");
            Assert.That(dark, Is.GreaterThanOrEqualTo(AccentContrast.MinimumRatio), $"{preset.Key} 的深色档 {preset.DarkHex} 与深色底只有 {dark:0.00}:1");
        }
    }

    [Test]
    public void Host_ClampsNonCompliantColorToTheSuggestedOne()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var store = new FakeStore(new UiSettings { AccentColor = "#1F1F1F" });
        var host = new AccentHost(store, new InlineDispatcher(), app);

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            host.Apply(store.Current);

            var brush = app.Resources[AccentHost.AccentBrushKey] as ISolidColorBrush;
            Assert.That(brush, Is.Not.Null, "强调色刷子没有写进资源字典");
            var applied = new AccentRgb(brush!.Color.R, brush.Color.G, brush.Color.B);
            var ratio = AccentContrast.Ratio(applied, AccentContrast.DarkPole);
            Assert.That(ratio, Is.GreaterThanOrEqualTo(AccentContrast.MinimumRatio), $"落地色 {AccentContrast.ToHex(applied)} 仍然读不清");
        }
        finally
        {
            host.Dispose();
            app.RequestedThemeVariant = original;
            Cleanup(app, AccentHost.AccentBrushKey);
            Cleanup(app, AccentHost.PrimaryBrushKey);
            Cleanup(app, AccentHost.AccentSubtleBrushKey);
            Cleanup(app, AccentHost.NavigationSelectionBrushKey);
        }
    }

    [Test]
    public void Host_AppliesCompliantCustomHexVerbatim()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var store = new FakeStore(new UiSettings { AccentColor = "#4C9AFF" });
        var host = new AccentHost(store, new InlineDispatcher(), app);

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            host.Apply(store.Current);

            var brush = app.Resources[AccentHost.AccentBrushKey] as ISolidColorBrush;
            Assert.That(brush, Is.Not.Null);
            Assert.That(AccentContrast.ToHex(new AccentRgb(brush!.Color.R, brush.Color.G, brush.Color.B)), Is.EqualTo("#4C9AFF"));
        }
        finally
        {
            host.Dispose();
            app.RequestedThemeVariant = original;
            Cleanup(app, AccentHost.AccentBrushKey);
            Cleanup(app, AccentHost.PrimaryBrushKey);
            Cleanup(app, AccentHost.AccentSubtleBrushKey);
            Cleanup(app, AccentHost.NavigationSelectionBrushKey);
        }
    }

    private static void Cleanup(Application app, string key)
    {
        if (app.Resources.ContainsKey(key))
        {
            app.Resources.Remove(key);
        }
    }

    private static AccentRgb Parse(string hex)
    {
        Assert.That(AccentContrast.TryParse(hex, out var rgb), Is.True, hex);
        return rgb;
    }

    private static Color ReadSurfaceBrush(string themeKey)
    {
        var document = XDocument.Load(TokensPath());
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var dictionary in document.Root!.Elements().Where(element => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries").Elements())
        {
            var key = dictionary.Attribute(xaml + "Key")?.Value;
            if (!string.Equals(key, themeKey, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var brush in dictionary.Elements().Where(element => element.Name.LocalName == "SolidColorBrush"))
            {
                if (string.Equals(brush.Attribute(xaml + "Key")?.Value, "CkanSurfaceBackgroundBrush", StringComparison.Ordinal))
                {
                    var color = brush.Attribute("Color")?.Value;
                    Assert.That(color, Is.Not.Null);
                    Assert.That(Color.TryParse(color, out var parsed), Is.True, color);
                    return parsed;
                }
            }
        }

        Assert.Fail($"Tokens.axaml 里找不到主题 {themeKey} 的 CkanSurfaceBackgroundBrush");
        return default;
    }

    private static AccentRgb ToRgb(Color color) => new(color.R, color.G, color.B);

    private static string TokensPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "CkanUI.App", "Resources", "Tokens.axaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("定位不到 src/CkanUI.App/Resources/Tokens.axaml");
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsOnUiThread => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }

    private sealed class FakeStore : IUiSettingsStore
    {
        public FakeStore(UiSettings current) => Current = current;

        public UiSettings Current { get; private set; }

        public event Action<UiSettings>? Changed;

        public UiSettings Load() => Current;

        public OperationResult<bool> Save(UiSettings settings)
        {
            Current = settings;
            Changed?.Invoke(settings);
            return OperationResult<bool>.Ok(true);
        }
    }
}
