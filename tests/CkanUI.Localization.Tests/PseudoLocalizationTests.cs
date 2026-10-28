using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>伪本地化（16 篇 §7.3）：加壳、30% 膨胀、占位符保真、默认关闭。</summary>
[TestFixture]
public sealed class PseudoLocalizationTests
{
    [Test]
    public void Transform_wraps_and_expands_by_thirty_percent()
    {
        var text = PseudoLocalization.Transform("Settings");

        Assert.That(text, Does.StartWith("[!! "));
        Assert.That(text, Does.EndWith(" !!]"));
        Assert.That(text.Length, Is.GreaterThan("Settings".Length), "伪本地化必须把文案撑长，否则测不出截断。");
        // 加壳后至少是原长的 1.3 倍（含填充符与方括号）。
        Assert.That(text.Length, Is.GreaterThanOrEqualTo((int)Math.Ceiling("Settings".Length * 1.3d)));
    }

    [Test]
    public void Transform_accents_ascii_but_leaves_cjk_alone()
    {
        var text = PseudoLocalization.Transform("Settings 设置");

        Assert.That(text, Does.Contain("Š"), "ASCII 字母要变形，才能一眼看出这条走了翻译而不是原文；" +
            "中文汉字保持不变，才能一眼看出这条是中文译文。");
        Assert.That(text, Does.Contain("设置"));
    }

    [Test]
    public void Transform_keeps_placeholders_intact()
    {
        var text = PseudoLocalization.Transform("共 {0:N0} 条");

        Assert.That(text, Does.Contain("{0:N0}"), "占位符被改写会让 string.Format 抛 FormatException。");
    }

    [Test]
    public void Transform_returns_empty_for_empty_input()
    {
        Assert.That(PseudoLocalization.Transform(null), Is.Empty);
        Assert.That(PseudoLocalization.Transform(string.Empty), Is.Empty);
    }

    [Test]
    public void IsPseudoLocale_matches_case_insensitively()
    {
        Assert.That(PseudoLocalization.IsPseudoLocale("qps-ploc"), Is.True);
        Assert.That(PseudoLocalization.IsPseudoLocale(" QPS-PLOC "), Is.True);
        Assert.That(PseudoLocalization.IsPseudoLocale("zh-CN"), Is.False);
        Assert.That(PseudoLocalization.IsPseudoLocale(null), Is.False);
    }

    [Test]
    public void Pseudo_is_off_by_default()
    {
        var localizer = TestPacks.Create("zh-CN", TestPacks.Pack("zh-CN", ("Ui_Common_Status_Ready", "就绪")));

        Assert.That(localizer.IsPseudoLocalized, Is.False);
        Assert.That(localizer.Get("Ui_Common_Status_Ready"), Is.EqualTo("就绪"));
    }

    [Test]
    public void Pseudo_wraps_pack_values_but_not_missing_keys()
    {
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.PseudoLocalize = true,
            TestPacks.Pack("zh-CN", ("Ui_Common_Status_Ready", "就绪")));

        Assert.That(localizer.IsPseudoLocalized, Is.True);
        Assert.That(localizer.Get("Ui_Common_Status_Ready"), Does.StartWith("[!! "));
        // 未命中的键保持原样：加壳会把"漏译"伪装成"已翻译"，而漏译正是要暴露的东西。
        Assert.That(localizer.Get("Ui_Common_Nav_Missing_Xyz"), Is.EqualTo("Ui_Common_Nav_Missing_Xyz"));
        Assert.That(localizer.MissingKeys.ContainsKey("Ui_Common_Nav_Missing_Xyz"), Is.True);
    }

    [Test]
    public void Pseudo_snapshot_is_transformed_too()
    {
        // Avalonia 的 DynamicResource 走快照，快照不变形的话热切换视图里根本看不到伪本地化。
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.PseudoLocalize = true,
            TestPacks.Pack("zh-CN", ("Ui_Common_Status_Ready", "就绪")));

        Assert.That(localizer.Snapshot()["Ui_Common_Status_Ready"], Does.StartWith("[!! "));
    }

    [Test]
    public void Pseudo_keeps_format_working()
    {
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.PseudoLocalize = true,
            TestPacks.Pack("zh-CN", ("Ui_Common_Count", "共 {0} 条")));

        Assert.That(localizer.Format("Ui_Common_Count", 5), Does.Contain("5"));
    }

    [Test]
    public void Pseudo_does_not_change_coverage()
    {
        var localizer = TestPacks.Create(
            "zh-CN",
            options => options.PseudoLocalize = true,
            TestPacks.Pack("zh-CN", ("Ui_Common_Status_Ready", "就绪")),
            TestPacks.Pack("en-US", ("Ui_Common_Status_Ready", "Ready")));

        var zh = localizer.AvailableLocales.Single(l => l.Name == "zh-CN");
        Assert.That(zh.IsComplete, Is.True);
        Assert.That(zh.CoveragePercent, Is.EqualTo(100));
    }
}
