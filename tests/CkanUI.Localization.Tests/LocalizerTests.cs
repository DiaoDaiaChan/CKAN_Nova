using NUnit.Framework;

namespace CkanUI.Localization.Tests;

[TestFixture]
public sealed class LocalizerTests
{
    [Test]
    public void MissingKey_FallsBackToKey()
    {
        var loc = new Localizer();
        Assert.That(loc.Get("missing.key"), Is.EqualTo("missing.key"));
    }

    [Test]
    public void Get_ReturnsValueOfCurrentLanguage()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.NavRailManageMods, "模组")),
            TestPacks.Pack("en-US", (LocalizationKeys.NavRailManageMods, "Mods")));

        Assert.That(loc.CurrentLocale, Is.EqualTo("zh-CN"));
        Assert.That(loc.Get(LocalizationKeys.NavRailManageMods), Is.EqualTo("模组"));
    }

    [Test]
    public void SetCulture_SwitchesImmediatelyAndRaisesLanguageChanged()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        var raised = new List<string>();
        loc.LanguageChanged += (_, culture) => raised.Add(culture.Name);

        loc.SetCulture("en-US");

        // 不重启、不重建对象即可拿到新语言（UI-13 判据）。
        Assert.That(loc.Get(LocalizationKeys.CommonStatusReady), Is.EqualTo("Ready"));
        Assert.That(loc.CurrentLocale, Is.EqualTo("en-US"));
        Assert.That(raised, Is.EqualTo(new[] { "en-US" }));
    }

    [Test]
    public void SetCulture_ToSameCulture_DoesNotRaiseEvent()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        var count = 0;
        loc.LanguageChanged += (_, _) => count++;
        loc.SetCulture("zh-CN");
        loc.SetCulture("ZH-cn");

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void SetCulture_EmptyLocale_Throws()
    {
        var loc = TestPacks.Create("zh-CN", TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")));

        Assert.Throws<ArgumentException>(() => loc.SetCulture("  "));
    }

    [Test]
    public void TryGet_DistinguishesHitFromMissingWithoutSideEffects()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        Assert.That(loc.TryGet(LocalizationKeys.CommonStatusReady, out var hit), Is.True);
        Assert.That(hit, Is.EqualTo("就绪"));
        Assert.That(loc.TryGet("Ui_Not_Exists", out _), Is.False);

        // 探测不应污染缺译统计。
        Assert.That(loc.MissingKeys, Is.Empty);
    }

    [Test]
    public void MissingKeys_CountsEveryHitAndWarnsOnlyOncePerKey()
    {
        var log = new RecordingLocalizationLog();
        var loc = TestPacks.Create("zh-CN", log, TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")));

        loc.SetCulture("zh-CN");
        for (var i = 0; i < 5; i++)
        {
            Assert.That(loc.Get("Ui_Ghost_Key"), Is.EqualTo("Ui_Ghost_Key"));
        }

        Assert.That(loc.MissingKeys.TryGetValue("Ui_Ghost_Key", out var count), Is.True);
        Assert.That(count, Is.EqualTo(5));
        Assert.That(log.Messages.Count(m => m.Contains("Ui_Ghost_Key", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    [Test]
    public void Snapshot_ExposesResolvedStrings()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready"), ("Ui_Extra_OnlyEn", "extra")));

        var snapshot = loc.Snapshot();

        // 快照是"已应用回退链"的结果：en-US 独有的键也出现在其中。
        Assert.That(snapshot[LocalizationKeys.CommonStatusReady], Is.EqualTo("就绪"));
        Assert.That(snapshot["Ui_Extra_OnlyEn"], Is.EqualTo("extra"));
    }

    [Test]
    public void SetCulture_UnshippedLocale_DegradesToFallbackAndWarns()
    {
        var log = new RecordingLocalizationLog();
        var loc = TestPacks.Create(
            "zh-CN",
            log,
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        // Core 的 config.json 里可能存了 es-ES 等未随包语言：不抛异常，只提示并走回退链。
        loc.SetCulture("es-ES");

        Assert.That(loc.Get(LocalizationKeys.CommonStatusReady), Is.EqualTo("Ready"));
        Assert.That(log.Contains("es-ES"), Is.True);
    }

    [Test]
    public void Constructor_UnknownDefaultLocale_PicksAnAvailablePack()
    {
        var loc = TestPacks.Create(
            "fr-FR",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        // 没有 fr 语言包时不应崩，落到可用的语言上。
        Assert.That(loc.Get(LocalizationKeys.CommonStatusReady), Is.AnyOf("就绪", "Ready"));
        Assert.That(loc.AvailableLocales.Select(l => l.Name), Is.EquivalentTo(new[] { "zh-CN", "en-US" }));
    }
}
