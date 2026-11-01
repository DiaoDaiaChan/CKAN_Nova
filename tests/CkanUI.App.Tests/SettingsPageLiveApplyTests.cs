using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using CkanUI.App.Theming;
using CkanUI.App.Tests.Automation;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Settings;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 设置页 → 宿主 → 已挂载控件 的端到端回归（无头）。
/// </summary>
/// <remarks>
/// <c>AppearanceLiveApplyTests</c> 证的是「宿主收到 Save 后控件会变」；这里补的是用户真正走的那一段：
/// 改设置页某一行的 <c>EditValue</c> → 450ms 防抖自动保存 → 页面保存委托 → <c>UiSettingsStore</c> 落盘 →
/// 宿主写资源 → 窗口里的控件当场换色 / 换圆角。少了这一段，「改了没反应」只能靠人工点。
/// </remarks>
[TestFixture]
public sealed class SettingsPageLiveApplyTests
{
    private string _directory = null!;
    private string _settingsPath = null!;

    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();

        _directory = Path.Combine(AppContext.BaseDirectory, "headless_temp", "settings-live", Guid.NewGuid().ToString("N"));
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
    public async Task AccentRowEdit_ReachesMountedBadge_AndPersistsToDisk()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();
        store.Save(store.Current with { AccentColor = "red" });

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            using var host = new AccentHost(store, new UiThreadDispatcher(), app);
            host.Install();

            var badge = new Border { Classes = { "CkanBadge", "Accent" }, Child = new TextBlock { Text = "标识" } };
            var window = HeadlessAppHost.CreateTestWindow(badge);
            try
            {
                var page = new SettingsPageViewModel(new InMemorySettingsService(), store);
                await page.LoadAsync().ConfigureAwait(true);
                HeadlessAppHost.PumpJobs();

                Assert.That(BorderColorOf(badge), Is.EqualTo(DarkHex("red")), "首帧不是已保存的强调色，后面的「变了」就没有对照");

                var row = Find(page, SettingsKeyNames.AccentColor);
                Assert.That(row.Kind, Is.EqualTo(SettingItemKind.Choice), "强调色行应该是可选项而不是文本框");
                row.EditValue = "green";

                PumpUntil(() => string.Equals(store.Current.AccentColor, "green", StringComparison.Ordinal)
                    && BorderColorOf(badge) == DarkHex("green"));

                Assert.That(
                    BorderColorOf(badge),
                    Is.EqualTo(DarkHex("green")),
                    "设置页改了强调色，已挂载控件没有当场换色");
                Assert.That(row.IsDirty, Is.False, "落盘后行还标着脏");

                var reloaded = new UiSettingsStore(_settingsPath);
                reloaded.Load();
                Assert.That(reloaded.Current.AccentColor, Is.EqualTo("green"), "自动保存没有写进 ckan-ui.json");
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
    public async Task RadiusRowEdit_ReachesCornerRadiusTokens()
    {
        var app = Application.Current!;
        var store = new UiSettingsStore(_settingsPath);
        store.Load();

        using var host = new RadiusHost(store, new UiThreadDispatcher(), app);
        host.Install();

        try
        {
            var page = new SettingsPageViewModel(new InMemorySettingsService(), store);
            await page.LoadAsync().ConfigureAwait(true);
            HeadlessAppHost.PumpJobs();

            var row = Find(page, SettingsKeyNames.RadiusScale);
            row.EditValue = "round";

            PumpUntil(() => app.Resources.ContainsKey(RadiusHost.CornerRadiusSmallKey));

            Assert.That(store.Current.RadiusScale, Is.EqualTo("round"), "圆角档没落盘");
            Assert.That(app.Resources[RadiusHost.CornerRadiusSmallKey], Is.EqualTo(new CornerRadius(12)), "设置页改了圆角，token 没跟着变");
        }
        finally
        {
            Cleanup(app, RadiusHost.CornerRadiusSmallKey);
            Cleanup(app, RadiusHost.CornerRadiusMediumKey);
            Cleanup(app, RadiusHost.CornerRadiusLargeKey);
        }
    }

    private static SettingItemViewModel Find(SettingsPageViewModel page, string key)
    {
        foreach (var group in page.Groups)
        {
            foreach (var item in group.Items)
            {
                if (string.Equals(item.Key, key, StringComparison.Ordinal))
                {
                    return item;
                }
            }
        }

        throw new AssertionException("设置页缺少 " + key + " 这一行");
    }

    /// <summary>推进 UI 队列直到条件成立（自动保存是 450ms 防抖 + dispatcher 续体，必须边排空边等）。</summary>
    private static void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMilliseconds)
            {
                throw new AssertionException("等待自动保存超时：" + timeoutMilliseconds + "ms 内条件未成立");
            }

            HeadlessAppHost.PumpJobs();
            Thread.Sleep(20);
        }

        HeadlessAppHost.PumpJobs();
    }

    private static Color BorderColorOf(Border border) =>
        border.BorderBrush is ISolidColorBrush brush
            ? brush.Color
            : throw new AssertionException("BorderBrush 不是纯色： " + (border.BorderBrush?.GetType().Name ?? "null"));

    private static Color DarkHex(string key)
    {
        Assert.That(AccentPalettes.TryGet(key, out var palette), Is.True, "色板 " + key + " 不存在");
        Assert.That(Color.TryParse(palette!.DarkHex, out var color), Is.True);
        return color;
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

    /// <summary>ISettingsService 的最小内存实现：设置页只写外观项，标量项保持默认即可。</summary>
    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public event Action<string, string?>? SettingChanged
        {
            add { }
            remove { }
        }

        public Task<OperationResult<string?>> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string?>.Ok(_values.TryGetValue(key, out var value) ? value : null));

        public Task<OperationResult<bool>> SetAsync(string key, string? value, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.FromResult(OperationResult<bool>.Ok(true));
        }

        public Task<OperationResult<IReadOnlyDictionary<string, string?>>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyDictionary<string, string?>>.Ok(
                new Dictionary<string, string?>(_values, StringComparer.Ordinal)));

        public Task<OperationResult<string?>> SetAndVerifyAsync(string key, string? value, CancellationToken ct = default) =>
            SetAsync(key, value, ct).ContinueWith(_ => OperationResult<string?>.Ok(value), TaskScheduler.Default);
    }

    /// <summary>
    /// 与生产 <c>AvaloniaDispatcher</c> 同语义：Post 一律排队到 UI 线程，绝不在调用方线程就地执行。
    /// </summary>
    /// <remarks>
    /// 就地执行会让自动保存的续体（线程池线程）直接调 <c>AccentHost.Apply</c>，
    /// 读 <c>Application.ActualThemeVariant</c> 时抛 <see cref="InvalidOperationException"/>（线程所有权校验）；
    /// 而 <c>UiSettingsStore.RaiseChanged</c> 只记日志不抛 —— 于是「设置改了没换色」看起来像产品缺陷。
    /// </remarks>
    private sealed class UiThreadDispatcher : CkanUI.Services.Abstractions.IDispatcher
    {
        public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

        public void Post(Action action) => Dispatcher.UIThread.Post(action);

        public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

        public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();
    }
}
