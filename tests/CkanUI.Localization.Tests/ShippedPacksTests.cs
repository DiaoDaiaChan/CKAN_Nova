using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>随包语言包的完整性校验（对齐 T-I18N-01/03 与 16 篇 §9 上传前校验）。</summary>
[TestFixture]
public sealed class ShippedPacksTests
{
    private static readonly string[] Required = ["zh-CN", "en-US"];

    [Test]
    public void EmbeddedPacks_ContainMandatoryLanguages()
    {
        var packs = LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance);

        Assert.That(packs.Select(p => p.Locale), Is.SupersetOf(Required));
    }

    [Test]
    public void EmbeddedPacks_AllDeclareNativeAndEnglishNames()
    {
        foreach (var pack in LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance))
        {
            Assert.That(pack.NativeName, Is.Not.EqualTo(pack.Locale), pack.Locale);
            Assert.That(pack.EnglishName, Is.Not.EqualTo(pack.Locale), pack.Locale);
        }
    }

    [Test]
    public void EmbeddedPacks_HaveIdenticalKeySets()
    {
        var packs = LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance)
            .ToDictionary(p => p.Locale, p => p.Strings, StringComparer.Ordinal);
        var reference = packs["en-US"].Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        foreach (var locale in Required)
        {
            Assert.That(
                packs[locale].Keys.OrderBy(k => k, StringComparer.Ordinal),
                Is.EqualTo(reference),
                $"{locale} 的键集与 en-US 不一致");
        }
    }

    [Test]
    public void LocalizationKeys_AreAllPresentInEveryPack()
    {
        var keys = typeof(LocalizationKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false })
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.That(keys, Is.Not.Empty);

        foreach (var pack in LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance))
        {
            foreach (var key in keys)
            {
                Assert.That(pack.Strings.ContainsKey(key), Is.True, $"{pack.Locale} 缺少键 {key}");
            }
        }
    }

    [Test]
    public void Placeholders_AreConsistentAcrossLanguages()
    {
        var packs = LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance)
            .ToDictionary(p => p.Locale, p => p.Strings, StringComparer.Ordinal);

        foreach (var pair in packs["en-US"])
        {
            var expected = Placeholders(pair.Value);
            foreach (var locale in Required)
            {
                Assert.That(
                    Placeholders(packs[locale][pair.Key]),
                    Is.EqualTo(expected),
                    $"{locale} 的 {pair.Key} 占位符与 en-US 不一致");
            }
        }
    }

    [Test]
    public void Keys_FollowNamingRuleOfSpecSection51()
    {
        var pattern = new Regex(@"^Ui_([A-Z][A-Za-z0-9]*)_([A-Za-z0-9]+)(_[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant);

        foreach (var pack in LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance))
        {
            foreach (var key in pack.Strings.Keys)
            {
                Assert.That(pattern.IsMatch(key), Is.True, $"{pack.Locale} 的键 {key} 不符合 16 篇 §5.1 命名规范");
            }
        }
    }

    [Test]
    public void Factory_ReportsFullCoverageForBothShippedLanguages()
    {
        var loc = LocalizerFactory.Create(new LocalizationOptions
        {
            DefaultLocale = "en-US",
            ApplyThreadCulture = false,
            Log = NullLocalizationLog.Instance,
        });

        foreach (var info in loc.AvailableLocales.Where(l => Required.Contains(l.Name)))
        {
            Assert.That(info.IsComplete, Is.True, info.Name);
            Assert.That(info.CoveragePercent, Is.EqualTo(100), info.Name);
            Assert.That(info.DisplayText, Does.EndWith(" · 100%"), info.Name);
        }
    }

    private static IEnumerable<string> Placeholders(string template) =>
        Regex.Matches(template, @"\{(?<n>\d+)(:[^}]*)?\}", RegexOptions.CultureInvariant)
            .Select(m => m.Groups["n"].Value)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal);
}
