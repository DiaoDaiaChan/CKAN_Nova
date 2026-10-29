using Avalonia;
using Avalonia.Controls;
using CkanUI.App.Theming;
using CkanUI.App.Tests.Automation;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 动效强度落地（20 篇 P-10 / THM-07）：设置 → 资源 token → 真实控件过渡时长这条链。
/// </summary>
/// <remarks>
/// 只测 <c>UiMotionScale</c> 的算术是不够的：视图里的 <c>Transition.Duration</c> 走的是
/// <c>{DynamicResource}</c>，写错键名或写成非 Avalonia 属性都只会在运行时静默失效，
/// 所以这里在无头 Avalonia 上真的造一个控件，读回它拿到的 Transitions。
/// </remarks>
[TestFixture]
public sealed class MotionTokenTests
{
    [Test]
    public void Install_WritesThreeDurationTokens()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var store = new FakeStore(new UiSettings { MotionIntensity = "full" });
        var host = new MotionHost(store, new InlineDispatcher(), app, () => true);

        try
        {
            host.Install();

            Assert.That(app.Resources[MotionHost.FastKey], Is.EqualTo(TimeSpan.FromMilliseconds(90)));
            Assert.That(app.Resources[MotionHost.BaseKey], Is.EqualTo(TimeSpan.FromMilliseconds(150)));
            Assert.That(app.Resources[MotionHost.PageKey], Is.EqualTo(TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Test]
    public void Changed_ToNone_ZeroesTheTokens()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var store = new FakeStore(new UiSettings { MotionIntensity = "full" });
        var host = new MotionHost(store, new InlineDispatcher(), app, () => true);

        try
        {
            host.Install();
            store.Save(new UiSettings { MotionIntensity = "none" });

            Assert.That(app.Resources[MotionHost.FastKey], Is.EqualTo(TimeSpan.Zero));
            Assert.That(app.Resources[MotionHost.BaseKey], Is.EqualTo(TimeSpan.Zero));
            Assert.That(app.Resources[MotionHost.PageKey], Is.EqualTo(TimeSpan.Zero));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Test]
    public void Auto_FollowsSystemPreference()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var store = new FakeStore(new UiSettings { MotionIntensity = UiMotionScale.Auto });
        var host = new MotionHost(store, new InlineDispatcher(), app, () => false);

        try
        {
            host.Install();

            // 系统关了动画 → auto 落到「精简」：保留 90ms 快速反馈，页面切换归零。
            Assert.That(app.Resources[MotionHost.FastKey], Is.EqualTo(TimeSpan.FromMilliseconds(90)));
            Assert.That(app.Resources[MotionHost.BaseKey], Is.EqualTo(TimeSpan.Zero));
            Assert.That(app.Resources[MotionHost.PageKey], Is.EqualTo(TimeSpan.Zero));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Test]
    public void Install_WritesFourSpringDurationTokens()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var store = new FakeStore(new UiSettings { MotionIntensity = UiMotionScale.Full });
        var host = new MotionHost(store, new InlineDispatcher(), app, () => true);

        try
        {
            host.Install();

            // 第三阶段的四档弹簧：时长只是「这套物理参数在多长窗口里跑完」，与刚度/阻尼解耦。
            Assert.That(app.Resources[MotionHost.SpringSoftKey], Is.EqualTo(TimeSpan.FromMilliseconds(320)));
            Assert.That(app.Resources[MotionHost.SpringSlideKey], Is.EqualTo(TimeSpan.FromMilliseconds(360)));
            Assert.That(app.Resources[MotionHost.SpringCrispKey], Is.EqualTo(TimeSpan.FromMilliseconds(250)));
            Assert.That(app.Resources[MotionHost.SpringImpulseKey], Is.EqualTo(TimeSpan.FromMilliseconds(450)));

            store.Save(new UiSettings { MotionIntensity = UiMotionScale.Reduced });
            Assert.That(app.Resources[MotionHost.SpringSoftKey], Is.EqualTo(TimeSpan.FromMilliseconds(90)));
            Assert.That(app.Resources[MotionHost.SpringImpulseKey], Is.EqualTo(TimeSpan.FromMilliseconds(90)));

            store.Save(new UiSettings { MotionIntensity = UiMotionScale.None });
            Assert.That(app.Resources[MotionHost.SpringSoftKey], Is.EqualTo(TimeSpan.Zero));
            Assert.That(app.Resources[MotionHost.SpringSlideKey], Is.EqualTo(TimeSpan.Zero));
            Assert.That(app.Resources[MotionHost.SpringCrispKey], Is.EqualTo(TimeSpan.Zero));
            Assert.That(app.Resources[MotionHost.SpringImpulseKey], Is.EqualTo(TimeSpan.Zero));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Test]
    public void ChipButton_PicksUpFastTokenForItsTransitions()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var fastKey = MotionHost.FastKey;
        var hasPrevious = app.Resources.TryGetValue(fastKey, out var previous);
        app.Resources[fastKey] = TimeSpan.FromMilliseconds(7);

        Window? window = null;
        try
        {
            var chip = new Button();
            chip.Classes.Add("CkanChip");
            window = HeadlessAppHost.CreateTestWindow(chip);

            // Transitions 是 ITransition 列表，Duration 在泛型实现类上，反射取最省事（不改产品代码去暴露它）。
            var durations = chip.Transitions?
                .Select(transition => transition.GetType().GetProperty("Duration")?.GetValue(transition))
                .OfType<TimeSpan>()
                .ToList() ?? new List<TimeSpan>();

            Assert.That(durations, Is.Not.Empty, "按钮样式应当挂上过渡；为空说明 FluentStyles 没生效或被改坏");
            Assert.That(durations, Has.Some.EqualTo(TimeSpan.FromMilliseconds(7)), "TransformOperationsTransition 应改用 CkanMotionDurationFast token");
        }
        finally
        {
            window?.Close();
            if (hasPrevious)
            {
                app.Resources[fastKey] = previous;
            }
            else
            {
                app.Resources.Remove(fastKey);
            }
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
