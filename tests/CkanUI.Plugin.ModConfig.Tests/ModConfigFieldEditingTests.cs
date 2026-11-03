using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Plugin.ModConfig;
using NUnit.Framework;

namespace CkanUI.Plugin.ModConfig.Tests;

/// <summary>
/// 字段行的编辑语义（M9 / CFG-04）：草稿值、wire 值拼装、待保存判定、九种类型的控件开关、
/// 只读类型的"本版只读"说明、enum 白名单补值、保存后的新基线。
/// </summary>
[TestFixture]
public sealed class ModConfigFieldEditingTests
{
    private static readonly ModConfigFieldRendererRegistry Renderers = new();

    private static ModConfigFieldRowViewModel Row(
        ModConfigFieldState field,
        bool isFileWritable = true,
        string? filePath = null,
        string? readonlyReason = null)
        => new(field, Renderers.Resolve(field.Type), isFileWritable, filePath, readonlyReason);

    [Test]
    public void EditValue_StartsFromDiskValue()
    {
        var row = Row(ModConfigTestData.Field("name", value: "旧名字"));

        Assert.That(row.EditValue, Is.EqualTo("旧名字"));
        Assert.That(row.WireValue, Is.EqualTo("旧名字"));
        Assert.That(row.HasPendingChange, Is.False);
    }

    [Test]
    public void EditValue_StartsEmptyWhenDiskValueMissing()
    {
        var row = Row(ModConfigTestData.Field("name"));

        Assert.That(row.EditValue, Is.Empty);
        Assert.That(row.HasPendingChange, Is.False);
    }

    [Test]
    public void ListEditor_ShowsOneItemPerLine()
    {
        var row = Row(ModConfigTestData.Field("ignore", ModConfigFieldType.List, "GameData/A, GameData/B"));

        Assert.That(row.EditorKind, Is.EqualTo(ModConfigEditorKind.ListEditor));
        Assert.That(row.EditValue, Is.EqualTo("GameData/A\nGameData/B"));
        Assert.That(row.ShowTextEditor, Is.True);
        Assert.That(row.IsMultilineEditor, Is.True);
        Assert.That(row.HasPendingChange, Is.False);
    }

    [Test]
    public void ListEditor_WireValueIsSingleLine()
    {
        var row = Row(ModConfigTestData.Field("ignore", ModConfigFieldType.List, "GameData/A"));
        row.EditValue = "GameData/A\nGameData/B\nGameData/C";

        Assert.That(row.WireValue, Is.EqualTo("GameData/A, GameData/B, GameData/C"));
        Assert.That(row.WireValue, Does.Not.Contain("\n"));
        Assert.That(row.WireValue, Does.Not.Contain("\r"));
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public void ListEditor_DropsBlankItems()
    {
        var row = Row(ModConfigTestData.Field("ignore", ModConfigFieldType.List, "a"));
        row.EditValue = "  a  \n\n  , b ";

        Assert.That(row.WireValue, Is.EqualTo("a, b"));
    }

    [Test]
    public void HasPendingChange_TracksDraftAndReset()
    {
        var row = Row(ModConfigTestData.Field("name", value: "旧"));
        row.EditValue = "新";

        Assert.That(row.HasPendingChange, Is.True);

        row.ResetEdit();

        Assert.That(row.EditValue, Is.EqualTo("旧"));
        Assert.That(row.HasPendingChange, Is.False);
    }

    [Test]
    public void ReadonlyFile_HasNoEditableControls()
    {
        var row = Row(ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "true"), isFileWritable: false);

        Assert.That(row.CanEdit, Is.False);
        Assert.That(row.ShowToggle, Is.False);
        Assert.That(row.ShowTextEditor, Is.False);
        Assert.That(row.ShowReadOnlyValue, Is.True);
        Assert.That(row.HasReadOnlyNote, Is.True);
        Assert.That(row.ReadOnlyNote, Is.EqualTo("只读：所在配置文件当前不可写。"));

        row.EditValue = "false";

        Assert.That(row.HasPendingChange, Is.False);
    }

    [Test]
    public void ReadonlyFile_NoteUsesAdapterReason()
    {
        var row = Row(
            ModConfigTestData.Field("padHeapLimit"),
            isFileWritable: false,
            readonlyReason: "这个文件由 mod 自己生成，改了会被覆盖。");

        Assert.That(row.ReadOnlyNote, Is.EqualTo("只读：这个文件由 mod 自己生成，改了会被覆盖。"));
    }

    [Test]
    public void ReadonlyFieldType_ExplainsWhyReadonly()
    {
        var row = Row(ModConfigTestData.Field("rawLine", ModConfigFieldType.ReadOnly, "abc"));

        Assert.That(row.EditorKind, Is.EqualTo(ModConfigEditorKind.ReadOnlyText));
        Assert.That(row.CanEdit, Is.False);
        Assert.That(row.ShowReadOnlyValue, Is.True);
        Assert.That(row.ReadOnlyNote, Is.EqualTo("只读字段：值由 mod 自己维护，页面不提供修改。"));
    }

    [Test]
    public void ColorField_EditsWithSwatchPreview()
    {
        var row = Row(ModConfigTestData.Field("tint", ModConfigFieldType.Color, "255,0,0"));

        Assert.That(row.EditorKind, Is.EqualTo(ModConfigEditorKind.ColorPicker));
        Assert.That(row.CanEdit, Is.True);
        Assert.That(row.ShowColorEditor, Is.True);
        Assert.That(row.ShowReadOnlyValue, Is.False);
        Assert.That(row.HasReadOnlyNote, Is.False);

        // 逗号三元组能被认出来：色块有颜色，且不再需要"写法不对"的提示。
        Assert.That(row.ColorPreviewHex, Is.EqualTo("#FF0000"));
        Assert.That(row.HasColorPreview, Is.True);
        Assert.That(row.HasColorNote, Is.False);

        row.EditValue = "#80ff0000";

        Assert.That(row.ColorPreviewHex, Is.EqualTo("#80ff0000"));
        Assert.That(row.WireValue, Is.EqualTo("#80ff0000"));
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public void ColorField_UnrecognizedTextStillWritesAsTyped()
    {
        var row = Row(ModConfigTestData.Field("tint", ModConfigFieldType.Color, "255,0,0"));

        row.EditValue = "跟着 mod 自己的写法";

        // 认不出来只提示、不拦：色块隐藏，说明文字如实写"仍按原文写入"。
        Assert.That(row.HasColorPreview, Is.False);
        Assert.That(row.HasColorNote, Is.True);
        Assert.That(row.ColorNote, Does.Contain("仍会按原文写入"));
        Assert.That(row.WireValue, Is.EqualTo("跟着 mod 自己的写法"));
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public void ColorField_EmptyValueExplainsWhatToType()
    {
        var row = Row(ModConfigTestData.Field("tint", ModConfigFieldType.Color));

        Assert.That(row.HasColorPreview, Is.False);
        Assert.That(row.ColorNote, Does.Contain("#RRGGBB"));
    }

    [Test]
    public void KeybindingField_CapturesWithHint()
    {
        var row = Row(ModConfigTestData.Field("toggleWindow", ModConfigFieldType.Keybinding, "KeypadMultiply"));

        Assert.That(row.EditorKind, Is.EqualTo(ModConfigEditorKind.KeyCapture));
        Assert.That(row.CanEdit, Is.True);
        Assert.That(row.ShowKeyCapture, Is.True);
        Assert.That(row.ShowReadOnlyValue, Is.False);
        Assert.That(row.HasReadOnlyNote, Is.False);
        Assert.That(row.HasKeyCaptureHint, Is.True);
        Assert.That(row.KeyCaptureHint, Does.Contain("Ctrl"));

        // 捕获是视图层把组合键文本写回 EditValue，行只负责承载草稿。
        row.EditValue = KeyGestureText.Format(ctrl: true, alt: false, shift: true, meta: false, key: "K");

        Assert.That(row.EditValue, Is.EqualTo("Ctrl+Shift+K"));
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public void PathField_WithoutPickerHasNoBrowseButton()
    {
        var row = Row(ModConfigTestData.Field("cacheDir", ModConfigFieldType.Path, "GameData/Demo"));

        Assert.That(row.EditorKind, Is.EqualTo(ModConfigEditorKind.PathPicker));
        Assert.That(row.CanBrowse, Is.False);
    }

    [Test]
    public async Task PathField_BrowseCommandFillsThePickedFolder()
    {
        var row = Row(ModConfigTestData.Field("cacheDir", ModConfigFieldType.Path, "GameData/Demo"));
        ModConfigFieldRowViewModel? asked = null;
        row.PathPicker = picked =>
        {
            asked = picked;
            return Task.FromResult<string?>("GameData/Picked");
        };

        Assert.That(row.CanBrowse, Is.True);

        await row.BrowseCommand.ExecuteAsync(null);

        Assert.That(asked, Is.SameAs(row));
        Assert.That(row.EditValue, Is.EqualTo("GameData/Picked"));
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public async Task PathField_CancelledBrowseKeepsTheDraft()
    {
        var row = Row(ModConfigTestData.Field("cacheDir", ModConfigFieldType.Path, "GameData/Demo"));
        row.PathPicker = _ => Task.FromResult<string?>(null);

        await row.BrowseCommand.ExecuteAsync(null);

        Assert.That(row.EditValue, Is.EqualTo("GameData/Demo"));
        Assert.That(row.HasPendingChange, Is.False);
    }

    [Test]
    public void ReadonlyColorFileHasNoEditor()
    {
        // 文件不可写时不许编辑：即便字段类型有控件，也只显示只读值 + 原因。
        var row = Row(
            ModConfigTestData.Field("tint", ModConfigFieldType.Color, "255,0,0"),
            isFileWritable: false,
            readonlyReason: "这个文件由 mod 自己生成，改了会被覆盖。");

        Assert.That(row.ShowColorEditor, Is.False);
        Assert.That(row.ShowReadOnlyValue, Is.True);
        Assert.That(row.ReadOnlyNote, Does.Contain("改了会被覆盖"));
    }

    [Test]
    public void PathField_UsesTextEditor()
    {
        var row = Row(ModConfigTestData.Field("cacheDir", ModConfigFieldType.Path, "GameData/Demo"));

        Assert.That(row.EditorKind, Is.EqualTo(ModConfigEditorKind.PathPicker));
        Assert.That(row.ShowTextEditor, Is.True);
        Assert.That(row.ShowReadOnlyValue, Is.False);
        Assert.That(row.HasReadOnlyNote, Is.False);

        row.EditValue = "GameData/Other";

        Assert.That(row.WireValue, Is.EqualTo("GameData/Other"));
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public void Toggle_ReflectsAndWritesBoolean()
    {
        var row = Row(ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "1"));

        Assert.That(row.IsToggleOn, Is.True);

        row.IsToggleOn = false;

        Assert.That(row.EditValue, Is.EqualTo("false"));
        Assert.That(row.IsToggleOn, Is.False);
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public void OnlyOneControlPerRow()
    {
        var toggle = Row(ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "true"));
        var dropdown = Row(ModConfigTestData.Field("lang", ModConfigFieldType.Enum, "en-us", values: ["en-us", "zh-cn"]));
        var text = Row(ModConfigTestData.Field("name", value: "x"));

        Assert.That(toggle.ShowToggle, Is.True);
        Assert.That(toggle.ShowDropdown, Is.False);
        Assert.That(toggle.ShowTextEditor, Is.False);
        Assert.That(toggle.ShowReadOnlyValue, Is.False);

        Assert.That(dropdown.ShowToggle, Is.False);
        Assert.That(dropdown.ShowDropdown, Is.True);
        Assert.That(dropdown.ShowTextEditor, Is.False);
        Assert.That(dropdown.ShowReadOnlyValue, Is.False);

        Assert.That(text.ShowToggle, Is.False);
        Assert.That(text.ShowDropdown, Is.False);
        Assert.That(text.ShowTextEditor, Is.True);
        Assert.That(text.ShowReadOnlyValue, Is.False);
    }

    [Test]
    public void Choices_KeepDeclaredWhenCurrentListed()
    {
        var row = Row(ModConfigTestData.Field("lang", ModConfigFieldType.Enum, "B", values: ["a", "b"]));

        Assert.That(row.Choices, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Choices_AppendUndeclaredCurrentValue()
    {
        var row = Row(ModConfigTestData.Field("lang", ModConfigFieldType.Enum, "c", values: ["a", "b"]));

        Assert.That(row.Choices, Is.EqualTo(new[] { "c", "a", "b" }));
    }

    [Test]
    public void MarkSaved_MakesDraftTheNewBaseline()
    {
        var row = Row(ModConfigTestData.Field("name", value: "旧"));
        row.EditValue = "新";
        row.MarkSaved();

        Assert.That(row.HasPendingChange, Is.False);

        row.EditValue = "更新";

        Assert.That(row.HasPendingChange, Is.True);

        row.ResetEdit();

        Assert.That(row.EditValue, Is.EqualTo("新"));
        Assert.That(row.HasPendingChange, Is.False);
    }

    [Test]
    public void ApplyDraft_ConvertsWireValueToEditorText()
    {
        var row = Row(ModConfigTestData.Field("ignore", ModConfigFieldType.List, "a"));
        row.ApplyDraft("x, y");

        Assert.That(row.EditValue, Is.EqualTo("x\ny"));
        Assert.That(row.WireValue, Is.EqualTo("x, y"));
        Assert.That(row.HasPendingChange, Is.True);
    }

    [Test]
    public void FilePath_IsCarriedForWriteGrouping()
    {
        var row = Row(ModConfigTestData.Field("name"), filePath: "/game/GameData/Demo/settings.cfg");

        Assert.That(row.FilePath, Is.EqualTo("/game/GameData/Demo/settings.cfg"));
        Assert.That(Row(ModConfigTestData.Field("name")).FilePath, Is.Empty);
    }
}
