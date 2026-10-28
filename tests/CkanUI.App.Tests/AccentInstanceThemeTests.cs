using System.Globalization;

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CkanUI.App.Theming;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 按实例强调色（20 篇 P-07）：宿主在切换实例时怎么选生效值。
/// </summary>
/// <remarks>
/// 设置页那两行的写入规则在 <c>tests/CkanUI.ViewModels.Tests/SettingsAccentInstanceTests.cs</c>。
/// </remarks>
[TestFixture]
public sealed class AccentInstanceThemeTests
{
    [Test]
    public void ResolveAccent_PrefersTheInstanceOverride()
    {
        var settings = new UiSettings
        {
            AccentColor = "blue",
            AccentByInstance = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "purple" },
        };

        Assert.That(AccentHost.ResolveAccent(settings, "A"), Is.EqualTo("purple"));
        Assert.That(AccentHost.ResolveAccent(settings, "B"), Is.EqualTo("blue"));
        Assert.That(AccentHost.ResolveAccent(settings, null), Is.EqualTo("blue"));
    }

    [Test]
    public void ResolveAccent_TreatsBlankAndEmptyNamesAsNoOverride()
    {
        var settings = new UiSettings
        {
            AccentColor = "teal",
            AccentByInstance = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["A"] = string.Empty,
                ["B"] = "  ",
            },
        };

        Assert.That(AccentHost.ResolveAccent(settings, "A"), Is.EqualTo("teal"));
        Assert.That(AccentHost.ResolveAccent(settings, "B"), Is.EqualTo("teal"));
        Assert.That(AccentHost.ResolveAccent(settings, "  "), Is.EqualTo("teal"));
    }

    [Test]
    public void Host_AppliesTheInstanceAccent_AndFollowsTheCurrentInstance()
    {
        var app = new Application { RequestedThemeVariant = ThemeVariant.Light };
        var store = new FakeStore(new UiSettings
        {
            Theme = "light",
            AccentColor = "blue",
            AccentByInstance = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "purple" },
        });
        var instances = new FakeInstances { Current = Instance("A") };

        using var host = new AccentHost(store, new InlineDispatcher(), app, instances);
        host.Install();

        Assert.That(AccentOn(app), Is.EqualTo(Palette("purple", dark: false)));

        instances.Current = Instance("B");
        instances.RaiseCurrentChanged(instances.Current);

        // B 没有覆盖，应当回到全局的 blue。
        Assert.That(AccentOn(app), Is.EqualTo(Palette("blue", dark: false)));
    }

    [Test]
    public void Host_Dispose_StopsFollowingInstanceChanges()
    {
        var app = new Application { RequestedThemeVariant = ThemeVariant.Light };
        var store = new FakeStore(new UiSettings
        {
            Theme = "light",
            AccentColor = "blue",
            AccentByInstance = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "red" },
        });
        var instances = new FakeInstances { Current = Instance("A") };

        var host = new AccentHost(store, new InlineDispatcher(), app, instances);
        host.Install();
        Assert.That(AccentOn(app), Is.EqualTo(Palette("red", dark: false)));

        host.Dispose();
        instances.Current = Instance("B");
        instances.RaiseCurrentChanged(instances.Current);

        // 退订之后不该再动资源，否则关窗后还会去写已经拆掉的 Application 资源。
        Assert.That(AccentOn(app), Is.EqualTo(Palette("red", dark: false)));
    }

    private static InstanceInfo Instance(string name) =>
        new(name, "D:/KSP", "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 0);

    private static Color Palette(string key, bool dark)
    {
        var preset = AccentPalettes.FindPreset(key);
        Assert.That(preset, Is.Not.Null, string.Create(CultureInfo.InvariantCulture, $"缺少预设 {key}"));
        return Color.Parse(dark ? preset!.DarkHex : preset!.LightHex);
    }

    private static Color AccentOn(Application app)
    {
        Assert.That(app.Resources.ContainsKey(AccentHost.AccentBrushKey), Is.True, "强调色覆盖项不存在");
        return ((ISolidColorBrush)app.Resources[AccentHost.AccentBrushKey]!).Color;
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

    private sealed class FakeInstances : IInstanceService
    {
        public IReadOnlyList<InstanceInfo> Instances { get; } = Array.Empty<InstanceInfo>();

        public InstanceInfo? Current { get; set; }

        public event Action? InstancesChanged;

        public event Action<InstanceInfo?>? CurrentChanged;

        public bool IsGameRunning(string instanceName = "") => false;

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不切换实例"));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不添加实例"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不克隆实例"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不重命名实例"));

        public void RaiseCurrentChanged(InstanceInfo? instance) => CurrentChanged?.Invoke(instance);

        public void RaiseInstancesChanged() => InstancesChanged?.Invoke();
    }
}
