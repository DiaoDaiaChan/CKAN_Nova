using System.Globalization;
using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>
/// 语言热切换（16 篇 §4.3 / I18N-12）：事件语义、无旧文案残留、未随包语言降级、缺键累计。
/// </summary>
[TestFixture]
public sealed class LanguageSwitchTests
{
    private const string KeyReady = "Ui_Common_Status_Ready";
    private const string KeyMissing = "Ui_Common_Nav_Missing_Xyz";

    private static Localizer CreateTwoLanguageLocalizer(
        Action<LocalizationOptions>? configure = null,
        ILocalizationLog? log = null)
    {
        var packs = new[]
        {
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")),
            TestPacks.Pack("de-DE", (KeyReady, "Bereit")),
            TestPacks.Pack("en-US", (KeyReady, "Ready")),
        };

        if (configure is null && log is null)
        {
            return TestPacks.Create("zh-CN", packs);
        }

        return TestPacks.Create(
            "zh-CN",
            options =>
            {
                options.Log = log ?? NullLocalizationLog.Instance;
                configure?.Invoke(options);
            },
            packs);
    }

    [Test]
    public void SetCulture_raises_event_once_with_new_culture()
    {
        var localizer = CreateTwoLanguageLocalizer();
        var raised = new List<CultureInfo>();
        localizer.LanguageChanged += (_, culture) => raised.Add(culture);

        localizer.SetCulture("de-DE");

        Assert.That(raised, Has.Count.EqualTo(1));
        Assert.That(raised[0].Name, Is.EqualTo("de-DE"));
        Assert.That(localizer.CurrentLocale, Is.EqualTo("de-DE"));
    }

    [Test]
    public void SetCulture_to_same_locale_does_not_raise()
    {
        var localizer = CreateTwoLanguageLocalizer();
        var count = 0;
        localizer.LanguageChanged += (_, _) => count++;

        localizer.SetCulture("zh-CN");

        // 设置页每次保存都会写一次语言；相同语言还触发全量刷新会让界面白闪一下。
        Assert.That(count, Is.Zero);
    }

    [Test]
    public void Handler_already_sees_new_language_strings()
    {
        var localizer = CreateTwoLanguageLocalizer();
        string? seenInHandler = null;
        localizer.LanguageChanged += (_, _) => seenInHandler = localizer.Get(KeyReady);

        localizer.SetCulture("de-DE");

        // 快照必须在事件之前换好：视图在事件里重读文案，读到旧语言就是"切换后残留旧文案"。
        Assert.That(seenInHandler, Is.EqualTo("Bereit"));
    }

    [Test]
    public void Snapshot_after_switch_has_no_stale_strings()
    {
        var localizer = CreateTwoLanguageLocalizer();

        localizer.SetCulture("de-DE");

        Assert.That(localizer.Snapshot()[KeyReady], Is.EqualTo("Bereit"));
        Assert.That(localizer.Get(KeyReady), Is.EqualTo("Bereit"));
    }

    [Test]
    public void Switch_to_locale_without_pack_warns_and_falls_back()
    {
        var log = new RecordingLocalizationLog();
        var localizer = CreateTwoLanguageLocalizer(log: log);

        localizer.SetCulture("fr-FR");

        Assert.That(localizer.CurrentLocale, Is.EqualTo("fr-FR"));
        Assert.That(localizer.Get(KeyReady), Is.EqualTo("Ready"), "未随包的语言只走回退链，不能崩也不能空白。");
        Assert.That(log.Contains("fr-FR"), Is.True);
    }

    [Test]
    public void Switch_reprobes_metadata_layer_with_new_locale()
    {
        var source = new LocaleRecordingSource(KeyMissing, "META");
        var localizer = CreateTwoLanguageLocalizer(options => options.Sources = [source]);

        localizer.SetCulture("de-DE");
        Assert.That(localizer.Get(KeyMissing), Is.EqualTo("META"));

        Assert.That(source.Locales, Does.Contain("de-DE"), "补充层必须按新语言重新探测，否则切完语言元数据仍是旧语言的。");
    }

    [Test]
    public void Missing_keys_accumulate_across_switches()
    {
        var localizer = CreateTwoLanguageLocalizer();

        localizer.Get(KeyMissing);
        localizer.SetCulture("de-DE");
        localizer.Get(KeyMissing);

        Assert.That(localizer.MissingKeys[KeyMissing], Is.EqualTo(2));
    }

    [Test]
    public void ClearDiagnostics_drops_layer_counters_too()
    {
        var localizer = CreateTwoLanguageLocalizer();
        localizer.Get(KeyReady);
        localizer.Get(KeyMissing);

        var released = localizer.ClearDiagnostics();

        // 一条缺键会同时进缺键表与告警去重表，所以释放数是 2 而不是 1；
        // 关键是"命中语言包"这种正常事件不参与计数（否则诊断读数永远非零）。
        Assert.That(released, Is.EqualTo(2));
        Assert.That(localizer.LayerHits, Is.Empty);
        Assert.That(localizer.MissingKeys, Is.Empty);
        Assert.That(localizer.LayerHits, Is.Empty);
    }

    /// <summary>记录每次被问到的语言标识，用来验证切换后重新探测。</summary>
    private sealed class LocaleRecordingSource : IStringSource
    {
        private readonly string _key;
        private readonly string _value;
        private readonly List<string> _locales = [];

        public LocaleRecordingSource(string key, string value)
        {
            _key = key;
            _value = value;
        }

        public string Name => "recording";

        public IReadOnlyList<string> Locales
        {
            get
            {
                lock (_locales)
                {
                    return [.. _locales];
                }
            }
        }

        public bool TryGet(string locale, string key, out string value)
        {
            lock (_locales)
            {
                _locales.Add(locale);
            }

            if (string.Equals(key, _key, StringComparison.Ordinal))
            {
                value = _value;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
