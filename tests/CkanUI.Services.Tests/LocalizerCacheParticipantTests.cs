using System.Globalization;
using CkanUI.Localization;
using CkanUI.Services.Lifecycle;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// L1 的本地化参与方（18 篇 §3）：把 <see cref="ILocalizer.ClearDiagnostics"/> 的结果如实上报。
/// </summary>
[TestFixture]
public sealed class LocalizerCacheParticipantTests
{
    [Test]
    public void TrimAppCache_ReportsWhatTheLocalizerReleased()
    {
        var localizer = new FakeLocalizer { Released = 7 };
        var participant = new LocalizerCacheParticipant(localizer);

        Assert.That(participant.Name, Is.EqualTo("本地化诊断表"));
        Assert.That(participant.TrimAppCache(), Is.EqualTo(7));
        Assert.That(localizer.ClearCount, Is.EqualTo(1));
    }

    [Test]
    public void TrimAppCache_WithoutDiagnostics_ReportsZero()
    {
        var participant = new LocalizerCacheParticipant(new FakeLocalizer { Released = 0 });

        Assert.That(participant.TrimAppCache(), Is.EqualTo(0));
    }

    [Test]
    public void Ctor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new LocalizerCacheParticipant(null!));
    }

    /// <summary>只关心释放计数的本地化器替身；其余成员按最小的合理行为实现。</summary>
    private sealed class FakeLocalizer : ILocalizer
    {
        public int Released { get; set; }

        public int ClearCount { get; private set; }

        public string CurrentLocale => "zh-CN";

        public CultureInfo CurrentCulture => CultureInfo.GetCultureInfo("zh-CN");

        public event EventHandler<CultureInfo>? LanguageChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<LocaleInfo> AvailableLocales => Array.Empty<LocaleInfo>();

        public IReadOnlyDictionary<string, int> MissingKeys => new Dictionary<string, int>();

        public int ClearDiagnostics()
        {
            ClearCount++;
            return Released;
        }

        public string Get(string key) => key;

        public string Format(string key, params object?[] args) => key;

        public bool TryGet(string key, out string value)
        {
            value = key;
            return false;
        }

        public void SetCulture(string locale)
        {
        }

        public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>();
    }
}
