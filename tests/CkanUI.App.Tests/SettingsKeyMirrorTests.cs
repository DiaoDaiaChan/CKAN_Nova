using System.Reflection;

using CkanUI.Services.Settings;
using CkanUI.ViewModels.Pages.Settings;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 键名镜像（02 篇 §3）：<see cref="SettingsKeyNames"/> 是视图模型侧的副本，
/// 真源是 <see cref="SettingKeys"/>。分层铁律禁止 ViewModels 引用 CkanUI.Services，
/// 所以两侧一致性只能在这里（同时看得见两边的 App.Tests）钉死，防止悄悄漂移。
/// </summary>
[TestFixture]
public sealed class SettingsKeyMirrorTests
{
    [TestCase(nameof(SettingsKeyNames.AutoStartInstance))]
    [TestCase(nameof(SettingsKeyNames.DownloadCacheDir))]
    [TestCase(nameof(SettingsKeyNames.CacheSizeLimit))]
    [TestCase(nameof(SettingsKeyNames.RefreshRate))]
    [TestCase(nameof(SettingsKeyNames.Language))]
    [TestCase(nameof(SettingsKeyNames.DevBuilds))]
    [TestCase(nameof(SettingsKeyNames.PreferredHosts))]
    [TestCase(nameof(SettingsKeyNames.AuthTokenPrefix))]
    [TestCase(nameof(SettingsKeyNames.GlobalInstallFiltersPrefix))]
    public void CoreKeys_MatchServicesTruth(string name)
    {
        var viewModelSide = ReadConst(typeof(SettingsKeyNames), name);
        var servicesSide = ReadConst(typeof(SettingKeys), name);
        Assert.That(viewModelSide, Is.EqualTo(servicesSide), $"键名 {name} 两侧不一致");
    }

    [Test]
    public void AuthTokenAndInstallFilterHelpers_UseTheSharedPrefixes()
    {
        Assert.That(SettingsKeyNames.AuthToken("spacedock.info"), Is.EqualTo(SettingKeys.AuthTokenPrefix + "spacedock.info"));
        Assert.That(SettingsKeyNames.GlobalInstallFilters("KSP"), Is.EqualTo(SettingKeys.GlobalInstallFiltersPrefix + "KSP"));
    }

    [Test]
    public void UiOnlyKeys_DoNotCollideWithCoreKeys()
    {
        var coreValues = typeof(SettingKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var uiKey in new[] { SettingsKeyNames.Theme, SettingsKeyNames.Locale, SettingsKeyNames.Density })
        {
            Assert.That(coreValues.Contains(uiKey), Is.False, $"UI 自有键 {uiKey} 与 Core 键名冲突");
        }
    }

    private static string ReadConst(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static)
                    ?? throw new AssertionException($"{type.Name} 缺少常量 {name}");
        Assert.That(field.IsLiteral, Is.True, $"{type.Name}.{name} 必须是编译期常量");
        return (string)field.GetRawConstantValue()!;
    }
}
