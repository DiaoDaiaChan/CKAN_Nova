using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>
/// 缺译诊断表的释放（18 篇 §3 的 L1 档）：这两张表只服务「缺译清单」，清空后下次取译文会重新累计。
/// </summary>
[TestFixture]
public sealed class DiagnosticsTests
{
    [Test]
    public void ClearDiagnostics_DropsMissingKeys_AndTheyAccumulateAgain()
    {
        var loc = new Localizer();
        loc.Get("missing.one");
        loc.Get("missing.two");
        loc.Get("missing.one");

        Assert.That(loc.MissingKeys.Count, Is.EqualTo(2), "按 key 去重累计");

        var released = loc.ClearDiagnostics();

        Assert.That(released, Is.GreaterThanOrEqualTo(2), "释放条目数至少等于缺 key 的种数");
        Assert.That(loc.MissingKeys, Is.Empty);

        loc.Get("missing.one");

        // 清空只是丢诊断，不影响文案：下一次取译文照旧返回 key 并重新记一行。
        Assert.That(loc.MissingKeys.Count, Is.EqualTo(1));
    }

    [Test]
    public void ClearDiagnostics_WithoutAnyDiagnostics_ReturnsZero()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")));

        loc.Get(LocalizationKeys.CommonStatusReady);

        Assert.That(loc.ClearDiagnostics(), Is.EqualTo(0));
        Assert.That(loc.MissingKeys, Is.Empty);
    }
}
