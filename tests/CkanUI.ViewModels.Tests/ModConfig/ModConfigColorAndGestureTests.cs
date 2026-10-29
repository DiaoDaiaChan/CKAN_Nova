using CkanUI.ViewModels.Pages.ModConfig;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.ModConfig;

/// <summary>
/// 取色与按键捕获的文本工具（CFG-04 的九种控件里 color/keybinding 两个纯函数件）：
/// 颜色只做"能不能预览"，按键组合只做"怎么写才一致"，都不替用户改写配置原文。
/// </summary>
[TestFixture]
public sealed class ModConfigColorAndGestureTests
{
    [TestCase("#f00", "#ff0000")]
    [TestCase("#FF0000", "#FF0000")]
    [TestCase("#80ff0000", "#80ff0000")]
    [TestCase("255,0,0", "#FF0000")]
    [TestCase("0.2, 0.4, 0.8", "#3366CC")]
    [TestCase("1,0,0", "#FF0000")]
    [TestCase("51,102,204,128", "#803366CC")]
    public void TryNormalize_AcceptsEveryWriteStyleKspUses(string text, string expected)
    {
        Assert.That(ModConfigColorText.TryNormalize(text, out var hex), Is.True);
        Assert.That(hex, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("不是颜色")]
    [TestCase("#12345")]
    [TestCase("#GGGGGG")]
    [TestCase("255,0")]
    [TestCase("300,0,0")]
    [TestCase("-1,0,0")]
    [TestCase("a,b,c")]
    public void TryNormalize_RejectsWhatItCannotRead(string text)
        => Assert.That(ModConfigColorText.TryNormalize(text, out var hex), Is.False, text);

    [Test]
    public void Describe_StaysQuietWhenTheColorIsReadable()
        => Assert.That(ModConfigColorText.Describe("#123456"), Is.Empty);

    [Test]
    public void Describe_TellsWhatToTypeWhenEmpty()
    {
        Assert.That(ModConfigColorText.Describe(null), Does.Contain("#RRGGBB"));
        Assert.That(ModConfigColorText.Describe("  "), Does.Contain("#RRGGBB"));
    }

    [Test]
    public void Describe_SaysItWillStillWriteTheOriginalText()
    {
        var note = ModConfigColorText.Describe("red-ish");

        Assert.That(note, Does.Contain("仍会按原文写入"));
        Assert.That(note, Does.Not.Contain("已"));
    }

    [Test]
    public void KeyGesture_OrdersModifiersTheSameWayEveryTime()
    {
        Assert.That(KeyGestureText.Format(false, false, true, true, "K"), Is.EqualTo("Shift+Win+K"));
        Assert.That(KeyGestureText.Format(true, true, true, true, "F5"), Is.EqualTo("Ctrl+Alt+Shift+Win+F5"));
        Assert.That(KeyGestureText.Format(true, false, false, false, "KeypadMultiply"), Is.EqualTo("Ctrl+KeypadMultiply"));
    }

    [Test]
    public void KeyGesture_WithoutModifiersIsJustTheKey()
        => Assert.That(KeyGestureText.Format(false, false, false, false, "escape"), Is.EqualTo("escape"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void KeyGesture_WithoutKeyIsEmpty(string? key)
        => Assert.That(KeyGestureText.Format(true, false, false, false, key), Is.Empty);

    [Test]
    public void KeyGesture_TrimsTheKeyName()
        => Assert.That(KeyGestureText.Format(false, true, false, false, "  K  "), Is.EqualTo("Alt+K"));
}
