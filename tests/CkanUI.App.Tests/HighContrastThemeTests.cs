using Avalonia;
using Avalonia.Styling;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 系统高对比度对主题的压制（20 篇 P-02 / THM-10）：探针说开了，用户选的主题一律让位；
/// 探针关掉后必须回到用户自己的选择，而不是一直卡在高对比度。
/// </summary>
[TestFixture]
public sealed class HighContrastThemeTests
{
    [Test]
    public void Host_ForcesHighContrast_AndReportsTheTransition()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var forced = true;
        var transitions = new List<bool>();
        var host = new ThemeHost(new FakeStore(new UiSettings { Theme = "light" }), new InlineDispatcher(), app, () => forced);
        host.ForcedStateChanged += transitions.Add;

        try
        {
            host.Install();

            Assert.That(host.IsHighContrastForced, Is.True);
            Assert.That(app.RequestedThemeVariant, Is.EqualTo(ThemeHost.HighContrastVariant));
            Assert.That(transitions, Is.EqualTo(new[] { true }));

            // 用户在系统里关掉高对比度：下一次 Refresh 必须把主题还回去。
            forced = false;
            host.Refresh();

            Assert.That(app.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Light));
            Assert.That(transitions, Is.EqualTo(new[] { true, false }));
        }
        finally
        {
            host.Dispose();
            app.RequestedThemeVariant = original;
        }
    }

    [Test]
    public void Host_LeavesTheChosenThemeAlone_WhenProbeSaysNo()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var host = new ThemeHost(new FakeStore(new UiSettings { Theme = "light" }), new InlineDispatcher(), app, () => false);

        try
        {
            host.Install();

            Assert.That(host.IsHighContrastForced, Is.False);
            Assert.That(app.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Light));
        }
        finally
        {
            host.Dispose();
            app.RequestedThemeVariant = original;
        }
    }

    [Test]
    public void AccentHost_SkipsTheCustomColorInHighContrast()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var store = new FakeStore(new UiSettings { AccentColor = "#4C9AFF" });
        var host = new AccentHost(store, new InlineDispatcher(), app);

        try
        {
            app.RequestedThemeVariant = ThemeHost.HighContrastVariant;
            host.Apply(store.Current);

            // app.Resources 里只有「覆盖项」：高对比度下必须一项不留，界面才会回落到 Tokens.axaml 中的合规色板。
            Assert.That(app.Resources.ContainsKey(AccentHost.AccentBrushKey), Is.False, "高对比度下不能保留自定义强调色覆盖项");
            Assert.That(app.Resources.ContainsKey(AccentHost.PrimaryBrushKey), Is.False);
        }
        finally
        {
            host.Dispose();
            app.RequestedThemeVariant = original;
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
