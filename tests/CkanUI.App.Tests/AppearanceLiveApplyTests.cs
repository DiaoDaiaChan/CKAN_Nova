using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

using CkanUI.App.Theming;
using CkanUI.App.Tests.Automation;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Settings;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 外观设置的「即时生效」回归（20 篇 P-03 / P-08 / P-09、THM-03）。此前只有 <c>host.Apply(...)</c>
/// 与纯函数映射的断言，缺两段真实链路：<c>IUiSettingsStore.Save</c> 有没有把 <c>Changed</c> 送到宿主，
/// 以及宿主写进 <see cref="Application.Resources"/> 之后，靠 <c>{DynamicResource}</c> 取值的样式
/// 会不会在已挂到窗口的控件上当场换色/换字号。这一段正是用户能直接看到的那段。
/// </summary>
[TestFixture]
public sealed class AppearanceLiveApplyTests
{
    private string _directory = null!;
    private string _settingsPath = null!;

    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();

        // 沙箱里 %TEMP% 常常不可写（BackgroundHostTests 踩过同一个坑），设置文件落在测试程序集目录下。
        _directory = Path.Combine(AppContext.BaseDirectory, "headless_temp", "appearance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settingsPath = Path.Combine(_directory, "ckan-ui.json");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结论。
        }
    }

    [Test]
    public void AccentChange_ReachesStyledBorder()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();
        store.Save(store.Current with { AccentColor = "red" });

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            // 先落一个确定的首帧强调色，否则首帧拿到的是 Tokens 里的默认蓝，无法判断「有没有变过」。
            store.Save(store.Current with { AccentColor = "red" });
            using var host = new AccentHost(store, new InlineDispatcher(), app);
            host.Install();

            var badge = new Border { Classes = { "CkanBadge", "Accent" }, Child = new TextBlock { Text = "标识" } };
            var window = HeadlessAppHost.CreateTestWindow(badge);
            try
            {
                var before = BorderColorOf(badge);
                Assert.That(before, Is.EqualTo(DarkHex("red")), "首帧就该是保存的强调色（red 预设）");

                store.Save(store.Current with { AccentColor = "green" });
                HeadlessAppHost.PumpJobs();

                var after = BorderColorOf(badge);
                Assert.That(after, Is.EqualTo(DarkHex("green")), "保存后已挂载的控件没有换色：Changed 没送到宿主，或资源通知没传到样式");
                Assert.That(after, Is.Not.EqualTo(before), "颜色没变过，等于设置没生效");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            CleanupAccentKeys(app);
        }
    }

    [Test]
    public void ThemeChange_ReachesRequestedVariantAndThemedTokens()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();

        try
        {
            store.Save(store.Current with { Theme = "dark" });
            using var host = new ThemeHost(store, new InlineDispatcher(), app);
            host.Install();

            var card = new Border { Classes = { "CkanCard" }, Width = 80, Height = 40 };
            var window = HeadlessAppHost.CreateTestWindow(card);
            try
            {
                Assert.That(app.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Dark));
                var darkBackground = BackgroundOf(card);

                store.Save(store.Current with { Theme = "light" });
                HeadlessAppHost.PumpJobs();

                Assert.That(app.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Light), "切换主题后没有立刻换变体");
                Assert.That(BackgroundOf(card), Is.Not.EqualTo(darkBackground), "换了变体但卡片底色没跟着变");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
        }
    }

    [Test]
    public void FontScaleChange_ReachesStyledTextBlock()
    {
        var app = Application.Current!;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();

        using var host = new FontScaleHost(store, new InlineDispatcher(), app);
        host.Install();

        var title = new TextBlock { Text = "模组管理", Classes = { "CkanPageTitle" } };
        var window = HeadlessAppHost.CreateTestWindow(title);
        try
        {
            var before = title.FontSize;
            Assert.That(before, Is.GreaterThan(0), "CkanFontSizeTitle 没有解析出来");

            store.Save(store.Current with { FontScale = "130" });
            HeadlessAppHost.PumpJobs();
            Assert.That(title.FontSize, Is.EqualTo(FontScaleHost.ResolveFontSize(before, "130")).Within(0.05), "字号缩放没有即时作用到已挂载控件");

            store.Save(store.Current with { FontScale = "100" });
            HeadlessAppHost.PumpJobs();
            Assert.That(title.FontSize, Is.EqualTo(before).Within(0.05), "回到 100% 没有复原");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void DensityChange_ReachesRowHeightResource()
    {
        var app = Application.Current!;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();

        using var host = new DensityHost(store, new InlineDispatcher(), app);
        host.Install();

        try
        {
            var compact = AssertNumeric(app, DensityHost.RowHeightKey);

            store.Save(store.Current with { Density = "comfortable" });
            HeadlessAppHost.PumpJobs();
            var comfortable = AssertNumeric(app, DensityHost.RowHeightKey);

            Assert.That(comfortable, Is.GreaterThan(compact), "舒适档的行高应该比紧凑档更高");
        }
        finally
        {
            Cleanup(app, DensityHost.RowHeightKey);
            Cleanup(app, DensityHost.PagePaddingKey);
            Cleanup(app, DensityHost.ToolbarPaddingKey);
        }
    }

    [Test]
    public void RadiusChange_ReachesCornerRadiusTokens()
    {
        var app = Application.Current!;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();

        using var host = new RadiusHost(store, new InlineDispatcher(), app);
        host.Install();

        try
        {
            // 标准档必须把覆盖项摘掉，让 Tokens.axaml 的基线（Small=8）生效——留着旧覆盖值是「改了没反应」的一种形态。
            Assert.That(app.Resources.ContainsKey(RadiusHost.CornerRadiusSmallKey), Is.False, "标准档不该留圆角覆盖");
            var baseline = app.FindResource(RadiusHost.CornerRadiusSmallKey);

            store.Save(store.Current with { RadiusScale = "round" });
            HeadlessAppHost.PumpJobs();
            Assert.That(app.Resources[RadiusHost.CornerRadiusSmallKey], Is.EqualTo(new CornerRadius(12)), "1.5x 档没写进 token");
            Assert.That(app.Resources[RadiusHost.CornerRadiusMediumKey], Is.EqualTo(new CornerRadius(18)));
            Assert.That(app.Resources[RadiusHost.CornerRadiusLargeKey], Is.EqualTo(new CornerRadius(27)));

            store.Save(store.Current with { RadiusScale = "none" });
            HeadlessAppHost.PumpJobs();
            Assert.That(app.Resources[RadiusHost.CornerRadiusSmallKey], Is.EqualTo(new CornerRadius(0)), "直角档没归零");

            store.Save(store.Current with { RadiusScale = "standard" });
            HeadlessAppHost.PumpJobs();
            Assert.That(app.Resources.ContainsKey(RadiusHost.CornerRadiusSmallKey), Is.False, "回到标准档应撤掉覆盖");
            Assert.That(app.FindResource(RadiusHost.CornerRadiusSmallKey), Is.EqualTo(baseline), "撤掉覆盖后基线值回不来");
        }
        finally
        {
            Cleanup(app, RadiusHost.CornerRadiusSmallKey);
            Cleanup(app, RadiusHost.CornerRadiusMediumKey);
            Cleanup(app, RadiusHost.CornerRadiusLargeKey);
        }
    }

    [Test]
    public void MotionChange_ReachesDurationTokens()
    {
        var app = Application.Current!;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();

        using var host = new MotionHost(store, new InlineDispatcher(), app, systemAnimations: () => true);
        host.Install();

        try
        {
            store.Save(store.Current with { MotionIntensity = "full" });
            HeadlessAppHost.PumpJobs();
            var standard = PageDuration(app);

            store.Save(store.Current with { MotionIntensity = "reduced" });
            HeadlessAppHost.PumpJobs();
            var reduced = PageDuration(app);
            Assert.That(reduced, Is.LessThan(standard), "降级动效没有把页面过渡时长压短");

            store.Save(store.Current with { MotionIntensity = "none" });
            HeadlessAppHost.PumpJobs();
            Assert.That(PageDuration(app), Is.EqualTo(TimeSpan.Zero), "关闭动效应该把时长归零");
        }
        finally
        {
            Cleanup(app, MotionHost.FastKey);
            Cleanup(app, MotionHost.BaseKey);
            Cleanup(app, MotionHost.PageKey);
            Cleanup(app, MotionHost.SpringSoftKey);
            Cleanup(app, MotionHost.SpringSlideKey);
            Cleanup(app, MotionHost.SpringCrispKey);
            Cleanup(app, MotionHost.SpringImpulseKey);
        }
    }

    private static TimeSpan PageDuration(Application app)
    {
        Assert.That(app.Resources.ContainsKey(MotionHost.PageKey), Is.True, "页面过渡时长 token 没有导入");
        var value = app.Resources[MotionHost.PageKey];
        Assert.That(value, Is.InstanceOf<TimeSpan>(), "页面过渡时长不是 TimeSpan");
        return (TimeSpan)value!;
    }

    private static Color BorderColorOf(Border border) =>
        border.BorderBrush is ISolidColorBrush brush
            ? brush.Color
            : throw new AssertionException($"BorderBrush 不是纯色：{border.BorderBrush?.GetType().Name ?? "null"}");

    private static Color BackgroundOf(Border border) =>
        border.Background is ISolidColorBrush brush
            ? brush.Color
            : throw new AssertionException("Background 不是纯色");

    private static Color DarkHex(string key)
    {
        Assert.That(AccentPalettes.TryGet(key, out var palette), Is.True, $"色板 {key} 不存在");
        Assert.That(Color.TryParse(palette!.DarkHex, out var color), Is.True);
        return color;
    }

    private static double AssertNumeric(Application app, string key)
    {
        Assert.That(app.Resources.ContainsKey(key), Is.True, $"{key} 没有被宿主导入");
        Assert.That(app.Resources[key], Is.InstanceOf<double>(), $"{key} 应该是 double");
        return (double)app.Resources[key]!;
    }

    private static void CleanupAccentKeys(Application app)
    {
        Cleanup(app, AccentHost.AccentBrushKey);
        Cleanup(app, AccentHost.PrimaryBrushKey);
        Cleanup(app, AccentHost.AccentSubtleBrushKey);
        Cleanup(app, AccentHost.NavigationSelectionBrushKey);
        Cleanup(app, AccentHost.FocusRingBrushKey);
        Cleanup(app, AccentHost.AccentHoverBrushKey);
        Cleanup(app, AccentHost.AmbientGlowBrushKey);
        Cleanup(app, AccentHost.OrbitalLineBrushKey);
    }

    private static void Cleanup(Application app, string key)
    {
        if (app.Resources.ContainsKey(key))
        {
            app.Resources.Remove(key);
        }
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
}
