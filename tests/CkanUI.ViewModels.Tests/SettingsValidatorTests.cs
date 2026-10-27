using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>设置项校验与线格式转换（03 篇 §7 的逐项校验、02 篇 §3 的字典型配置序列化）。</summary>
[TestFixture]
public sealed class SettingsValidatorTests
{
    private static readonly SettingChoice[] ThemeChoices =
    {
        new("system", "跟随系统"),
        new("light", "浅色"),
    };

    [Test]
    public void Integer_AllowsEmptyAndRejectsNonNumeric()
    {
        Assert.That(SettingValidator.ValidateInteger(string.Empty, 0, 100), Is.Null);
        Assert.That(SettingValidator.ValidateInteger("abc", 0, 100), Is.EqualTo("请输入整数"));
        Assert.That(SettingValidator.ValidateInteger("12", 0, 100), Is.Null);
    }

    [Test]
    public void Integer_RespectsBounds()
    {
        Assert.That(SettingValidator.ValidateInteger("-1", 0, 1440), Is.EqualTo("不能小于 0"));
        Assert.That(SettingValidator.ValidateInteger("1441", 0, 1440), Is.EqualTo("不能大于 1440"));
    }

    [Test]
    public void Text_RejectsTooLongValue()
    {
        var tooLong = new string('x', SettingValidator.MaxTextLength + 1);

        Assert.That(SettingValidator.ValidateText(tooLong), Is.EqualTo("最多 512 个字符"));
        Assert.That(SettingValidator.ValidateText("D:/CKAN"), Is.Null);
    }

    [Test]
    public void Path_AllowsEmptyAndAbsoluteButRejectsRelative()
    {
        Assert.That(SettingValidator.ValidatePath(string.Empty), Is.Null);
        Assert.That(SettingValidator.ValidatePath("D:/CKAN/cache"), Is.Null);
        Assert.That(SettingValidator.ValidatePath("cache"), Does.Contain("绝对路径"));
    }

    [Test]
    public void Path_RejectsInvalidCharacters()
    {
        Assert.That(SettingValidator.ValidatePath("D:/CKAN/\0cache"), Is.EqualTo("路径包含非法字符"));
    }

    [Test]
    public void Boolean_OnlyAcceptsTrueFalseOrEmpty()
    {
        Assert.That(SettingValidator.ValidateBoolean("true"), Is.Null);
        Assert.That(SettingValidator.ValidateBoolean("false"), Is.Null);
        Assert.That(SettingValidator.ValidateBoolean(string.Empty), Is.Null);
        Assert.That(SettingValidator.ValidateBoolean("yes"), Is.EqualTo("取值只能是 true 或 false"));
    }

    [Test]
    public void Choice_RejectsValuesOutsideTheList()
    {
        Assert.That(SettingValidator.ValidateChoice("light", ThemeChoices), Is.Null);
        Assert.That(SettingValidator.ValidateChoice("solarized", ThemeChoices), Is.EqualTo("请选择一个有效选项"));
        Assert.That(SettingValidator.ValidateChoice("anything", null), Is.Null);
    }

    [Test]
    public void Lines_RejectWhitespaceInsideAnEntry()
    {
        Assert.That(SettingValidator.ValidateLines("spacedock.info"), Is.Null);
        Assert.That(SettingValidator.ValidateLines("spacedock .info"), Does.Contain("不能包含空白字符"));
        Assert.That(SettingValidator.ValidateLines("not:*.zip\r\nkerbal.curseforge.com"), Is.Null);
    }

    [Test]
    public void SplitLines_KeepsMiddleEmptyLineAndTrimsTrailing()
    {
        var lines = SettingValidator.SplitLines("a\r\n\r\nb\n");

        Assert.That(lines, Is.EqualTo(new[] { "a", string.Empty, "b" }));
    }

    [Test]
    public void WireFormat_RoundTripsLineListWithNullPlaceholder()
    {
        var wire = SettingWireFormat.ToWire(SettingItemKind.LineList, "a\n\nb");

        Assert.That(wire, Is.EqualTo("[\"a\",null,\"b\"]"));
        Assert.That(SettingWireFormat.FromWire(SettingItemKind.LineList, wire), Is.EqualTo("a\n\nb"));
    }

    [Test]
    public void WireFormat_EmptyLineListBecomesEmptyString()
    {
        Assert.That(SettingWireFormat.ToWire(SettingItemKind.LineList, string.Empty), Is.EqualTo(string.Empty));
        Assert.That(SettingWireFormat.FromWire(SettingItemKind.LineList, null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void WireFormat_InvalidJsonFallsBackToRawText()
    {
        Assert.That(SettingWireFormat.FromWire(SettingItemKind.LineList, "not-json"), Is.EqualTo("not-json"));
        Assert.That(SettingWireFormat.ToWire(SettingItemKind.Integer, "  60  "), Is.EqualTo("60"));
        Assert.That(SettingWireFormat.FromWire(SettingItemKind.Boolean, null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Mask_KeepsFourCharactersOnEachEdge()
    {
        Assert.That(AuthTokenMasking.Mask("abcdefgh1234"), Is.EqualTo("abcd****1234"));
        Assert.That(AuthTokenMasking.Mask("abcdefgh1234"), Does.Not.Contain("efgh1"));
    }

    [Test]
    public void Mask_HidesShortTokensCompletely()
    {
        Assert.That(AuthTokenMasking.Mask("abcd1234"), Is.EqualTo("********"));
        Assert.That(AuthTokenMasking.Mask(null), Is.EqualTo(AuthTokenMasking.EmptyPlaceholder));
        Assert.That(AuthTokenMasking.Mask(string.Empty), Is.EqualTo(AuthTokenMasking.EmptyPlaceholder));
        Assert.That(AuthTokenMasking.IsConfigured("abcd1234"), Is.True);
        Assert.That(AuthTokenMasking.IsConfigured(null), Is.False);
    }
}
