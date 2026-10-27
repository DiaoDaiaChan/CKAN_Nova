using CKAN;
using CKAN.Configuration;
using CkanUI.Services.ChangeSets;
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 单元测试：ModChange 变更集模型（MOD-02 / 04 篇 §4、§5）。
/// </summary>
[TestFixture]
public sealed class ModChangeTests
{
    private static readonly IConfiguration FakeConfig = new FakeConfiguration();

    private static CkanModule CreateModule(string id, string ver)
    {
        return CkanModule.FromJson($$"""
        {
            "spec_version": 1,
            "identifier": "{{id}}",
            "name": "{{id}} Mod",
            "version": "{{ver}}",
            "download": "https://example.com/{{id}}.zip"
        }
        """);
    }

    [Test]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var mod = CreateModule("TestMod", "1.0.0");
        var reason = new SelectionReason.UserRequested();
        var change = new ModChange(mod, GUIModChangeType.Install, reason, FakeConfig);

        Assert.That(change.Mod, Is.SameAs(mod));
        Assert.That(change.ChangeType, Is.EqualTo(GUIModChangeType.Install));
        Assert.That(change.Reasons, Contains.Item(reason));
        Assert.That(change.IsUserRequested, Is.True);
        Assert.That(change.IsAutoRemoval, Is.False);
    }

    [Test]
    public void ModUpgrade_Constructor_SetsTargetModAndDescription()
    {
        var current = CreateModule("TestMod", "1.0.0");
        var target = CreateModule("TestMod", "1.1.0");
        var upgrade = new ModUpgrade(current, target, FakeConfig);

        Assert.That(upgrade.Mod, Is.SameAs(current));
        Assert.That(upgrade.targetMod, Is.SameAs(target));
        Assert.That(upgrade.ChangeType, Is.EqualTo(GUIModChangeType.Update));
    }

    [Test]
    public void ModUpgrade_TargetMod_CanBeUpdated()
    {
        var current = CreateModule("TestMod", "1.0.0");
        var target1 = CreateModule("TestMod", "1.1.0");
        var target2 = CreateModule("TestMod", "1.2.0");
        var upgrade = new ModUpgrade(current, target1, FakeConfig);

        upgrade.targetMod = target2;
        Assert.That(upgrade.targetMod, Is.SameAs(target2));
    }

    [Test]
    public void ModChange_Equality_BasedOnModIdentifierAndChangeType()
    {
        var m1 = CreateModule("ModA", "1.0.0");
        var m2 = CreateModule("ModA", "1.0.0");
        var m3 = CreateModule("ModB", "1.0.0");

        var c1 = new ModChange(m1, GUIModChangeType.Install, FakeConfig);
        var c2 = new ModChange(m2, GUIModChangeType.Install, FakeConfig);
        var c3 = new ModChange(m1, GUIModChangeType.Remove, FakeConfig);
        var c4 = new ModChange(m3, GUIModChangeType.Install, FakeConfig);

        Assert.That(c1.Equals(c2), Is.True);
        Assert.That(c1.GetHashCode(), Is.EqualTo(c2.GetHashCode()));
        Assert.That(c1.Equals(c3), Is.False);
        Assert.That(c1.Equals(c4), Is.False);
    }

    private sealed class FakeConfiguration : IConfiguration
    {
        public string? AutoStartInstance { get; set; }
        public string? DownloadCacheDir { get; set; }
        public long? CacheSizeLimit { get; set; }
        public int RefreshRate { get; set; }
        public string? Language { get; set; }
        public string?[] PreferredHosts { get; set; } = Array.Empty<string?>();
        public bool? DevBuilds { get; set; }

        public IEnumerable<string> GetAuthTokenHosts() => Array.Empty<string>();
        public bool TryGetAuthToken(string host, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? token) { token = null; return false; }
        public void SetAuthToken(string host, string? token) { }
        public void SetRegistryToInstances(SortedList<string, GameInstance> instances) { }
        public IEnumerable<Tuple<string, string, string>> GetInstances() => Array.Empty<Tuple<string, string, string>>();
        public string[] GetGlobalInstallFilters(CKAN.Games.IGame game) => Array.Empty<string>();
        public void SetGlobalInstallFilters(CKAN.Games.IGame game, string[] value) { }
#pragma warning disable CS0067
        #pragma warning disable CS0067
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
#pragma warning restore CS0067
    }
}

