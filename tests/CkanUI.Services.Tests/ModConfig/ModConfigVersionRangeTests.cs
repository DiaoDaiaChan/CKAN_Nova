using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// 版本区间判定（CFG-03 的地基）：adapter 用 ">=1.30", "&lt;2.0" 这种写法声明支持范围。
/// 这里的存在理由是"越界必须能判定成越界"——判错的后果是拿不认识的版本去写配置文件。
/// </summary>
[TestFixture]
public sealed class ModConfigVersionRangeTests
{
    [TestCase(">=1.30", ">=", "1.30")]
    [TestCase("<=2.0", "<=", "2.0")]
    [TestCase("==1.30", "=", "1.30")]
    [TestCase("=", "", "")]
    public void TryParseConstraint_Cases(string text, string expectedOperator, string expectedVersion)
    {
        if (expectedOperator.Length == 0)
        {
            // "=" 单独出现属于非法（没有版本号），单独断言，避免把非法样例混进正常表里。
            Assert.That(ModConfigVersionRange.TryParseConstraint(text, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("缺少版本号"));
            return;
        }

        Assert.That(ModConfigVersionRange.TryParseConstraint(text, out var constraint, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(constraint.Operator, Is.EqualTo(expectedOperator));
            Assert.That(constraint.Version, Is.EqualTo(expectedVersion));
        });
    }

    [Test]
    public void TryParseConstraint_BareVersionMeansEquals()
    {
        Assert.That(ModConfigVersionRange.TryParseConstraint("1.30", out var constraint, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(constraint.Operator, Is.EqualTo("="));
            Assert.That(constraint.Version, Is.EqualTo("1.30"));
        });
    }

    [TestCase("")]
    [TestCase(">=")]
    [TestCase(">=abc")]
    [TestCase("about")]
    public void TryParseConstraint_InvalidInputs(string text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModConfigVersionRange.TryParseConstraint(text, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void TryParseAll_TrimsAndKeepsOrder()
    {
        Assert.That(ModConfigVersionRange.TryParseAll([" >=1.30 ", "<2.0"], out var parsed, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Has.Count.EqualTo(2));
            Assert.That(parsed[0].Version, Is.EqualTo("1.30"));
            Assert.That(parsed[1].Operator, Is.EqualTo("<"));
        });
    }

    [Test]
    public void TryParseAll_EmptyMeansNoRestriction()
    {
        Assert.That(ModConfigVersionRange.TryParseAll(null, out var parsed, out _), Is.True);
        Assert.That(parsed, Is.Empty);
    }

    [TestCase("1.31.0", true)]
    [TestCase("1.30", true)]
    [TestCase("1.29.9", false)]
    [TestCase("2.0.0", false)]
    public void TryIsSatisfied_ComparesAgainstEveryConstraint(string installedVersion, bool expected)
    {
        Assert.That(
            ModConfigVersionRange.TryIsSatisfied([">=1.30", "<2.0"], installedVersion, out var satisfied, out _),
            Is.True);
        Assert.That(satisfied, Is.EqualTo(expected), installedVersion);
    }

    [Test]
    public void TryIsSatisfied_UnknownInstalledVersion_CannotJudge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModConfigVersionRange.TryIsSatisfied([">=1.30"], null, out var satisfied, out var error), Is.False);
            Assert.That(satisfied, Is.False);
            Assert.That(error, Does.Contain("未知"));
        });
    }

    [Test]
    public void TryIsSatisfied_InvalidInstalledVersion_CannotJudge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModConfigVersionRange.TryIsSatisfied([">=1.30"], "not-a-version", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("不是合法版本"));
        });
    }

    [Test]
    public void TryIsSatisfied_EmptyRange_AcceptsAnything()
    {
        Assert.That(ModConfigVersionRange.TryIsSatisfied([], "0.1", out var satisfied, out _), Is.True);
        Assert.That(satisfied, Is.True);
    }

    [Test]
    public void FormatForDisplay_UsesMathematicalSymbols()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModConfigVersionRange.FormatForDisplay([">=1.30", "<2.0"]), Is.EqualTo("≥1.30, <2.0"));
            Assert.That(ModConfigVersionRange.FormatForDisplay(null), Is.EqualTo("不限"));
        });
    }
}
