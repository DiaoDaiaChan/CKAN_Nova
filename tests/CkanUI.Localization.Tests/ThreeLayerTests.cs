using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>
/// 三层解析（界面语言包 → 元数据包 → 回退键）的顺序、缓存与降级行为。
/// 对应 16 篇 §4.1 与 17 篇 §3 的查找链。
/// </summary>
[TestFixture]
public sealed class ThreeLayerTests
{
    private const string KeyReady = "Ui_Common_Status_Ready";
    private const string KeyBack = "Ui_Common_Nav_Back";
    private const string KeyMissing = "Ui_Common_Nav_Missing_Xyz";

    [Test]
    public void Pack_hit_wins_over_metadata_layer()
    {
        var source = new FakeSource("meta", [(KeyReady, "META-READY")]);
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.Sources = [source],
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")),
            TestPacks.Pack("en-US", (KeyReady, "Ready")));

        Assert.That(localizer.Get(KeyReady), Is.EqualTo("就绪"));
        // 语言包命中时补充层一次都不应该被问：它的探测可能是磁盘/资源读，白问就是浪费。
        Assert.That(source.Calls, Is.Zero);
        Assert.That(localizer.LayerHits["pack"], Is.EqualTo(1));
    }

    [Test]
    public void Metadata_layer_fills_keys_missing_from_packs()
    {
        var source = new FakeSource("meta", [(KeyBack, "META-BACK")]);
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.Sources = [source],
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")));

        Assert.That(localizer.Get(KeyBack), Is.EqualTo("META-BACK"));
        Assert.That(localizer.MissingKeys, Is.Empty);
        Assert.That(localizer.LayerHits["source:meta"], Is.EqualTo(1));
    }

    [Test]
    public void Fallback_chain_precedes_metadata_layer()
    {
        // 语言包整条回退链（zh-CN→zh→en-US）必须走完再问元数据包，
        // 否则 en-US 已有的正式译文会被机翻元数据顶掉。
        var source = new FakeSource("meta", [(KeyBack, "META-BACK")]);
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.Sources = [source],
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")),
            TestPacks.Pack("en-US", (KeyBack, "Back")));

        Assert.That(localizer.Get(KeyBack), Is.EqualTo("Back"));
        Assert.That(source.Calls, Is.Zero);
        Assert.That(localizer.FallbackKeyCount, Is.EqualTo(1));
    }

    [Test]
    public void Missing_in_all_layers_returns_key_and_counts_it()
    {
        var log = new RecordingLocalizationLog();
        var localizer = TestPacks.Create(
            "zh-CN",
            options =>
            {
                options.Log = log;
                options.Sources = [new FakeSource("meta", [])];
            },
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")));

        Assert.That(localizer.Get(KeyMissing), Is.EqualTo(KeyMissing));
        Assert.That(localizer.MissingKeys[KeyMissing], Is.EqualTo(1));
        Assert.That(localizer.LayerHits["missing"], Is.EqualTo(1));
        Assert.That(log.Contains(KeyMissing), Is.True, "缺键必须调试可见（16 篇 §4.5）。");
    }

    [Test]
    public void Source_results_are_cached_per_key()
    {
        var source = new FakeSource("meta", [(KeyBack, "META-BACK")]);
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.Sources = [source],
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")));

        Assert.That(localizer.Get(KeyBack), Is.EqualTo("META-BACK"));
        Assert.That(localizer.Get(KeyBack), Is.EqualTo("META-BACK"));
        Assert.That(localizer.Get(KeyMissing), Is.EqualTo(KeyMissing));
        Assert.That(localizer.Get(KeyMissing), Is.EqualTo(KeyMissing));
        // 两条键各探测一次：命中的与未命中的都要缓存，否则每次绑定重算都会重新扫磁盘。
        Assert.That(source.Calls, Is.EqualTo(2));
    }

    [Test]
    public void Throwing_source_is_skipped_not_fatal()
    {
        var log = new RecordingLocalizationLog();
        var localizer = TestPacks.Create(
            "zh-CN",
            options =>
            {
                options.Log = log;
                options.Sources = [new ThrowingSource("broken"), new FakeSource("meta", [(KeyBack, "META-BACK")])];
            },
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")));

        Assert.That(localizer.Get(KeyBack), Is.EqualTo("META-BACK"));
        Assert.That(log.Contains("broken"), Is.True, "坏来源要被记名，否则界面上少文案但没人知道为什么。");
    }

    [Test]
    public void Dictionary_source_filters_by_locale()
    {
        const string MetaOnly = "Ui_Common_MetaOnly_Probe";
        var source = DictionaryStringSource.TryParse(
            "meta",
            """
            { "strings": { "Ui_Common_MetaOnly_Probe": "返回" } }
            """,
            "zh-CN");
        Assert.That(source, Is.Not.Null);

        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.Sources = [source!],
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")),
            TestPacks.Pack("en-US", (KeyReady, "Ready")));

        Assert.That(localizer.Get(MetaOnly), Is.EqualTo("返回"));
        localizer.SetCulture("en-US");
        // 只带 zh-CN 的元数据层在 en-US 下必须让路，不能把中文塞进英文界面。
        Assert.That(localizer.Get(MetaOnly), Is.EqualTo(MetaOnly));
        Assert.That(localizer.MissingKeys.ContainsKey(MetaOnly), Is.True);
    }

    [Test]
    public void Dictionary_source_rejects_broken_json_without_throwing()
    {
        var log = new RecordingLocalizationLog();
        Assert.That(DictionaryStringSource.TryParse("meta", "{ not json", null, log), Is.Null);
        Assert.That(DictionaryStringSource.TryParse("meta", "{}", null, log), Is.Null);
        Assert.That(log.Count, Is.EqualTo(2));
    }

    [Test]
    public void Core_source_only_answers_prefixed_keys()
    {
        var source = new CoreResourceStringSource(NullLocalizationLog.Instance);
        Assert.That(source.TryGet("zh-CN", KeyReady, out _), Is.False);
        // 带前缀但不存在的资源同样只返回 false，绝不抛（Core 层是可选加速层）。
        Assert.That(source.TryGet("zh-CN", CoreResourceStringSource.KeyPrefix + "NoSuchResourceKey", out _), Is.False);
    }

    [Test]
    public void Snapshot_contains_only_language_pack_keys()
    {
        // 快照会被整表塞进 Avalonia 资源字典，掺进元数据层会让"改语言整表替换"的语义被污染。
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.Sources = [new FakeSource("meta", [(KeyBack, "META-BACK")])],
            TestPacks.Pack("zh-CN", (KeyReady, "就绪")));

        Assert.That(localizer.Get(KeyBack), Is.EqualTo("META-BACK"));
        Assert.That(localizer.Snapshot().ContainsKey(KeyReady), Is.True);
        Assert.That(localizer.Snapshot().ContainsKey(KeyBack), Is.False);
    }

    /// <summary>可计数、却什么都不命中的来源替身，用来验证"问了几次"。</summary>
    private sealed class FakeSource : IStringSource
    {
        private readonly IReadOnlyDictionary<string, string> _strings;

        public FakeSource(string name, IReadOnlyList<(string Key, string Value)> entries)
        {
            Name = name;
            _strings = entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        }

        public string Name { get; }

        public int Calls { get; private set; }

        public bool TryGet(string locale, string key, out string value)
        {
            Calls++;
            if (_strings.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    /// <summary>每次都炸的来源替身：界面不能因为它挂掉。</summary>
    private sealed class ThrowingSource : IStringSource
    {
        public ThrowingSource(string name) => Name = name;

        public string Name { get; }

        public bool TryGet(string locale, string key, out string value) =>
            throw new InvalidOperationException("来源探测故意失败");
    }
}
