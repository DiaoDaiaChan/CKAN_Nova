using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>占位符格式化（16 篇 §8.3：避免复数变位，用"标签：值"模板）。</summary>
[TestFixture]
public sealed class FormatTests
{
    [Test]
    public void Format_SubstitutesArguments()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusStartupIncomplete, "启动未完成：{0}")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusStartupIncomplete, "Startup incomplete: {0}")));

        Assert.That(
            loc.Format(LocalizationKeys.CommonStatusStartupIncomplete, "缺少 CKAN.dll"),
            Is.EqualTo("启动未完成：缺少 CKAN.dll"));
    }

    [Test]
    public void Format_FollowsCurrentLanguageAfterSwitch()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonLanguageSwitched, "界面语言已切换为 {0}")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonLanguageSwitched, "Interface language changed to {0}")));

        loc.SetCulture("en-US");

        Assert.That(loc.Format(LocalizationKeys.CommonLanguageSwitched, "English (United States)"),
            Is.EqualTo("Interface language changed to English (United States)"));
    }

    [Test]
    public void Format_WithoutArguments_ReturnsTemplate()
    {
        var loc = TestPacks.Create(
            "zh-CN",
            TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")),
            TestPacks.Pack("en-US", (LocalizationKeys.CommonStatusReady, "Ready")));

        Assert.That(loc.Format(LocalizationKeys.CommonStatusReady), Is.EqualTo("就绪"));
    }

    [Test]
    public void Format_BrokenPlaceholder_ReturnsTemplateAndWarnsOnce()
    {
        var log = new RecordingLocalizationLog();
        var loc = TestPacks.Create("zh-CN", log, TestPacks.Pack("zh-CN", ("Ui_Broken", "只有 {0} 与 {1}")));

        var first = loc.Format("Ui_Broken", "一个参数");
        var second = loc.Format("Ui_Broken", "一个参数");

        Assert.That(first, Is.EqualTo("只有 {0} 与 {1}"));
        Assert.That(second, Is.EqualTo(first));
        Assert.That(log.Messages.Count(m => m.Contains("Ui_Broken", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    [Test]
    public void Format_MissingKey_FormatsKeyItself()
    {
        var loc = TestPacks.Create("zh-CN", TestPacks.Pack("zh-CN", (LocalizationKeys.CommonStatusReady, "就绪")));

        // 缺 key 时返回 key 本身，格式化不会因此抛异常。
        Assert.That(loc.Format("Ui_Ghost {0}", "x"), Is.EqualTo("Ui_Ghost x"));
    }

    [Test]
    public void Format_InvalidFormatString_DoesNotThrow()
    {
        var loc = TestPacks.Create("zh-CN", TestPacks.Pack("zh-CN", ("Ui_Invalid", "非法 {abc")));

        Assert.That(loc.Format("Ui_Invalid", 1), Is.Not.Null);
    }
}
