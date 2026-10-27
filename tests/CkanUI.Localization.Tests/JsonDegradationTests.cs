using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>语言包是随包数据：损坏必须降级而不是抛异常（16 篇 §4.4 / §9 校验口径）。</summary>
[TestFixture]
public sealed class JsonDegradationTests
{
    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        // 不用系统 Temp：本机沙箱对 %TEMP% 有 ACL 限制；改用测试程序集输出目录下的临时子目录（已忽略入库）。
        _dir = Path.Combine(AppContext.BaseDirectory, "i18n-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响结论，忽略。
        }
    }

    [Test]
    public void TryParse_CorruptJson_ReturnsNullAndWarns()
    {
        var log = new RecordingLocalizationLog();

        var pack = LanguagePackLoader.TryParse("{ \"locale\": \"zh-CN\", ", "zh-CN", log);

        Assert.That(pack, Is.Null);
        Assert.That(log.Count, Is.GreaterThan(0));
    }

    [Test]
    public void TryParse_ValidJson_UsesFileHintWhenLocaleMissing()
    {
        var log = new RecordingLocalizationLog();

        var pack = LanguagePackLoader.TryParse(
            "{\"strings\":{\"Ui_Common_Status_Ready\":\"Ready\"}}",
            "en-US",
            log);

        Assert.That(pack, Is.Not.Null);
        Assert.That(pack!.Locale, Is.EqualTo("en-US"));
        Assert.That(pack.Strings[LocalizationKeys.CommonStatusReady], Is.EqualTo("Ready"));
    }

    [Test]
    public void TryParse_NullAndEmptyValues_AreDroppedAsUntranslated()
    {
        var log = new RecordingLocalizationLog();

        var pack = LanguagePackLoader.TryParse(
            "{\"locale\":\"zh-CN\",\"strings\":{\"A\":null,\"B\":\"  \",\"C\":\"有\"}}",
            "zh-CN",
            log);

        Assert.That(pack, Is.Not.Null);
        Assert.That(pack!.Strings.Keys, Is.EquivalentTo(new[] { "C" }));
        Assert.That(log.Count, Is.GreaterThan(0));
    }

    [Test]
    public void TryParse_ToleratesCommentsAndTrailingCommas()
    {
        var json = """
        {
          // 译者的备注允许存在
          "locale": "zh-CN",
          "strings": {
            "Ui_Common_Status_Ready": "就绪",
          },
        }
        """;

        var pack = LanguagePackLoader.TryParse(json, "zh-CN", new RecordingLocalizationLog());

        Assert.That(pack, Is.Not.Null);
        Assert.That(pack!.Strings[LocalizationKeys.CommonStatusReady], Is.EqualTo("就绪"));
    }

    [Test]
    public void LoadDirectory_MissingDirectory_ReturnsEmptyWithoutThrowing()
    {
        var log = new RecordingLocalizationLog();

        var packs = LanguagePackLoader.LoadDirectory(Path.Combine(_dir, "not-exists"), log);

        Assert.That(packs, Is.Empty);
        Assert.That(log.Count, Is.GreaterThan(0));
    }

    [Test]
    public void LoadDirectory_SkipsCorruptFileAndKeepsGoodOne()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        File.WriteAllText(
            Path.Combine(_dir, "en-US.json"),
            "{\"locale\":\"en-US\",\"strings\":{\"Ui_Common_Status_Ready\":\"Ready\"}}");

        var log = new RecordingLocalizationLog();
        var packs = LanguagePackLoader.LoadDirectory(_dir, log);

        Assert.That(packs.Count, Is.EqualTo(1));
        Assert.That(packs[0].Locale, Is.EqualTo("en-US"));
        Assert.That(log.Contains("broken"), Is.True);
    }

    [Test]
    public void Factory_CorruptDiskPack_StillUsableViaFallback()
    {
        File.WriteAllText(Path.Combine(_dir, "zh-CN.json"), "{ 坏掉的语言包");

        using var _ = new TemporaryCultureScope();
        var loc = LocalizerFactory.CreateFromDirectory(
            _dir,
            new LocalizationOptions { DefaultLocale = "zh-CN", ApplyThreadCulture = false, Log = NullLocalizationLog.Instance });

        // 磁盘包坏了，内嵌的 en-US 仍然兜住，最坏结果是 key 本身，但绝不抛。
        Assert.That(loc.Get(LocalizationKeys.CommonStatusReady), Is.Not.Empty);
    }

    [Test]
    public void TryLoadFile_EmptyFile_ReturnsNull()
    {
        var path = Path.Combine(_dir, "empty.json");
        File.WriteAllText(path, string.Empty);

        var pack = LanguagePackLoader.TryLoadFile(path, new RecordingLocalizationLog());

        Assert.That(pack, Is.Null);
    }

    /// <summary>本测试组可能改动 UI Culture，退出时还原，避免影响同进程其他测试。</summary>
    private sealed class TemporaryCultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _ui = System.Globalization.CultureInfo.CurrentUICulture;

        public void Dispose() => System.Globalization.CultureInfo.CurrentUICulture = _ui;
    }
}
