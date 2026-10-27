using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>切语言与并发读取：快照整体替换，读侧不允许看到半成品或 key 本身。</summary>
[TestFixture]
public sealed class ConcurrencyTests
{
    [Test]
    public void ConcurrentReads_DuringSwitching_AlwaysReturnTranslatedText()
    {
        const string Key = LocalizationKeys.CommonStatusReady;
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (Key, "就绪")),
            TestPacks.Pack("en-US", (Key, "Ready")));

        var invalid = 0;
        var stop = 0;
        var readers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    var value = loc.Get(Key);
                    if (value != "就绪" && value != "Ready")
                    {
                        Interlocked.Increment(ref invalid);
                    }
                }
            }))
            .ToArray();

        for (var i = 0; i < 300; i++)
        {
            loc.SetCulture(i % 2 == 0 ? "en-US" : "zh-CN");
        }

        Volatile.Write(ref stop, 1);
        Task.WaitAll(readers);

        Assert.That(invalid, Is.EqualTo(0));
        Assert.That(loc.MissingKeys, Is.Empty);
    }

    [Test]
    public void ConcurrentMissingKeyCounting_DoesNotLoseCounts()
    {
        var loc = TestPacks.Create("zh-CN", TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")));

        Parallel.For(0, 200, _ => loc.Get("Ui_Ghost_Concurrent"));

        Assert.That(loc.MissingKeys.TryGetValue("Ui_Ghost_Concurrent", out var count), Is.True);
        Assert.That(count, Is.EqualTo(200));
    }
}
