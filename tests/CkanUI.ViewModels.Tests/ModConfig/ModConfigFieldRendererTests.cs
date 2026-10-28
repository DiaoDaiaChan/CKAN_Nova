using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.ViewModels.Pages.ModConfig;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.ModConfig;

/// <summary>
/// 字段渲染器（UI-CFG-01 的 5 种类型 + 可扩展注册表）：格式化、编辑器种类、兜底与覆盖。
/// </summary>
[TestFixture]
public sealed class ModConfigFieldRendererTests
{
    [TestCase("true", "开")]
    [TestCase("TRUE", "开")]
    [TestCase("1", "开")]
    [TestCase("false", "关")]
    [TestCase("0", "关")]
    [TestCase("", "未设置")]
    [TestCase(null, "未设置")]
    [TestCase("yes", "yes")]
    public void Bool_RendersOnOffAndKeepsUnknownRaw(string? raw, string expected)
    {
        var field = ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, raw);

        Assert.That(new BoolFieldRenderer().FormatValue(field), Is.EqualTo(expected));
    }

    [TestCase("8", "8")]
    [TestCase("8.7", "9")]
    [TestCase("-3", "-3")]
    [TestCase("abc", "abc")]
    [TestCase(null, "未设置")]
    public void Int_RendersIntegerOrKeepsRaw(string? raw, string expected)
    {
        var field = ModConfigTestData.Field("count", ModConfigFieldType.Int, raw);

        Assert.That(new IntFieldRenderer().FormatValue(field), Is.EqualTo(expected));
    }

    [Test]
    public void Int_AppendsUnit()
    {
        var field = ModConfigTestData.Field("timeout", ModConfigFieldType.Int, "30", unit: "秒");

        Assert.That(new IntFieldRenderer().FormatValue(field), Is.EqualTo("30 秒"));
    }

    [TestCase("0.05", "0.05")]
    [TestCase("0.123456", "0.123")]
    [TestCase(null, "未设置")]
    public void Float_TrimsToThreeDecimals(string? raw, string expected)
    {
        var field = ModConfigTestData.Field("ratio", ModConfigFieldType.Float, raw);

        Assert.That(new FloatFieldRenderer().FormatValue(field), Is.EqualTo(expected));
    }

    [Test]
    public void Float_AppendsUnit()
    {
        var field = ModConfigTestData.Field("delta", ModConfigFieldType.Float, "0.05", unit: "s");

        Assert.That(new FloatFieldRenderer().FormatValue(field), Is.EqualTo("0.05 s"));
    }

    [Test]
    public void Enum_RendersRawOrUnset()
    {
        var renderer = new EnumFieldRenderer();

        Assert.That(renderer.FormatValue(ModConfigTestData.Field("lang", ModConfigFieldType.Enum, "en-us")), Is.EqualTo("en-us"));
        Assert.That(renderer.FormatValue(ModConfigTestData.Field("lang", ModConfigFieldType.Enum, null)), Is.EqualTo("未设置"));
    }

    [Test]
    public void String_RendersRaw()
    {
        var renderer = new StringFieldRenderer();

        Assert.That(renderer.FormatValue(ModConfigTestData.Field("note", ModConfigFieldType.String, "hello")), Is.EqualTo("hello"));
        Assert.That(renderer.FormatValue(ModConfigTestData.Field("note", ModConfigFieldType.String, null)), Is.Empty);
    }

    [Test]
    public void Renderers_ReportEditorKind()
    {
        Assert.That(new BoolFieldRenderer().EditorKind, Is.EqualTo(ModConfigEditorKind.Toggle));
        Assert.That(new IntFieldRenderer().EditorKind, Is.EqualTo(ModConfigEditorKind.NumberField));
        Assert.That(new FloatFieldRenderer().EditorKind, Is.EqualTo(ModConfigEditorKind.NumberField));
        Assert.That(new EnumFieldRenderer().EditorKind, Is.EqualTo(ModConfigEditorKind.Dropdown));
        Assert.That(new StringFieldRenderer().EditorKind, Is.EqualTo(ModConfigEditorKind.TextField));
    }

    [Test]
    public void Registry_DefaultCoversNineTypesInOrder()
    {
        var registry = new ModConfigFieldRendererRegistry();
        var defaults = ModConfigFieldRendererRegistry.CreateDefaultRenderers();

        Assert.That(defaults.Select(r => r.FieldType), Is.EqualTo(new[]
        {
            ModConfigFieldType.Bool,
            ModConfigFieldType.Int,
            ModConfigFieldType.Float,
            ModConfigFieldType.Enum,
            ModConfigFieldType.String,
            ModConfigFieldType.Path,
            ModConfigFieldType.List,
            ModConfigFieldType.Color,
            ModConfigFieldType.Keybinding,
        }));
        Assert.That(registry.SupportedTypes.Count, Is.EqualTo(9));
        Assert.That(registry.SupportedTypes, Does.Contain(ModConfigFieldType.Enum));
        Assert.That(registry.SupportedTypes, Does.Contain(ModConfigFieldType.List));
    }

    [Test]
    public void Registry_FallsBackForUnsupportedType()
    {
        var registry = new ModConfigFieldRendererRegistry();
        var field = ModConfigTestData.Field("rawLine", ModConfigFieldType.ReadOnly, "KeypadMultiply");

        // ReadOnly 是"永远不注册渲染器"的类型：它本身就表示只读原文，只能落兜底渲染器。
        Assert.That(registry.TryGet(ModConfigFieldType.ReadOnly, out _), Is.False);

        var renderer = registry.Resolve(ModConfigFieldType.ReadOnly);
        Assert.That(renderer, Is.SameAs(UnsupportedFieldRenderer.Instance));
        Assert.That(renderer.EditorKind, Is.EqualTo(ModConfigEditorKind.ReadOnlyText));
        Assert.That(renderer.FormatValue(field), Is.EqualTo("KeypadMultiply"));
    }

    [Test]
    public void Registry_RegisterOverridesSameType()
    {
        var registry = new ModConfigFieldRendererRegistry();
        var field = ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "true");

        registry.Register(new AlwaysYesRenderer());

        Assert.That(registry.Resolve(ModConfigFieldType.Bool).FormatValue(field), Is.EqualTo("总是开"));
        Assert.That(registry.SupportedTypes.Count, Is.EqualTo(9));
    }

    /// <summary>用来验证"同类型覆盖"的测试渲染器。</summary>
    private sealed class AlwaysYesRenderer : IModConfigFieldRenderer
    {
        public ModConfigFieldType FieldType => ModConfigFieldType.Bool;

        public ModConfigEditorKind EditorKind => ModConfigEditorKind.Toggle;

        public string FormatValue(ModConfigFieldState field) => "总是开";
    }
}
