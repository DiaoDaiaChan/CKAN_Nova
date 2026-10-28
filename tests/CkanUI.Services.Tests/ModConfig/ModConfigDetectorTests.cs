using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// CFG-03：状态机是纯函数（docs/plan/15 §5）。这里只喂布尔与字符串，不碰磁盘，
/// 这样"版本越界""文件缺失""无适配器"这些分支才能在 headless 下穷举。
/// </summary>
[TestFixture]
public sealed class ModConfigDetectorTests
{
    private static readonly string[] SupportedRange = [">=1.30", "<2.0"];

    private static ModConfigFileDescription File(
        ModConfigFileLayout layout = ModConfigFileLayout.Flat,
        ModConfigWriteMode mode = ModConfigWriteMode.Editable,
        ModConfigMissingFilePolicy onMissing = ModConfigMissingFilePolicy.Error,
        string? template = null,
        string? readonlyReason = null)
        => new(
            Path: "GameData/Demo/settings.cfg",
            Layout: layout,
            Mode: mode,
            Index: 0,
            RootNode: null,
            Id: "settings",
            OnMissing: onMissing,
            Template: template,
            LineEnding: ModConfigLineEndingPolicy.Preserve,
            Separator: " = ",
            ReadonlyReason: readonlyReason,
            Label: null);

    private static ModConfigFieldDescription Field(
        string key,
        ModConfigFieldType type,
        string? defaultValue = null,
        IReadOnlyList<string>? values = null,
        double? min = null,
        double? max = null,
        string? pattern = null,
        ModConfigFieldRisk risk = ModConfigFieldRisk.Low)
        => new(
            Key: key,
            Type: type,
            FileIndex: 0,
            FileId: "settings",
            DefaultValue: defaultValue,
            Values: values,
            Min: min,
            Max: max,
            Unit: null,
            Risk: risk,
            RequiresRestart: false,
            Label: null,
            Pattern: pattern);

    [Test]
    public void IsInstalled_RegistryHitOrFileHit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModConfigDetector.IsInstalled(registryHit: true, anyDeclaredFileExists: false), Is.True);
            Assert.That(ModConfigDetector.IsInstalled(registryHit: false, anyDeclaredFileExists: true), Is.True,
                "只靠文件命中也要算已装（用户可能手工装的）");
            Assert.That(ModConfigDetector.IsInstalled(registryHit: false, anyDeclaredFileExists: false), Is.False);
        });
    }

    [Test]
    public void DetectAdapterState_NotInstalled_WhenNothingMatches()
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: false,
            anyDeclaredFileExists: false,
            supportedVersionRange: SupportedRange,
            installedVersion: null,
            anyCreatableFile: false,
            anyWritableFile: false,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.NotInstalled));
            Assert.That(diagnostic, Is.Null);
        });
    }

    [Test]
    public void DetectAdapterState_VersionInRange_IsEditable()
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: true,
            anyDeclaredFileExists: true,
            supportedVersionRange: SupportedRange,
            installedVersion: "1.31.0",
            anyCreatableFile: false,
            anyWritableFile: true,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.Editable));
            Assert.That(diagnostic, Is.Null);
        });
    }

    [TestCase("1.29.9")]
    [TestCase("2.0.0")]
    [TestCase("19.0.0")]
    public void DetectAdapterState_VersionOutOfRange_ForcesReadOnly(string installedVersion)
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: true,
            anyDeclaredFileExists: true,
            supportedVersionRange: SupportedRange,
            installedVersion: installedVersion,
            anyCreatableFile: false,
            anyWritableFile: true,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.ReadOnlyVersion), "越界一律只读，不写盘");
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.Code, Is.EqualTo("version-out-of-range"));
        });
    }

    [Test]
    public void DetectAdapterState_UnknownVersion_StaysEditableWithWarning()
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: false,
            anyDeclaredFileExists: true,
            supportedVersionRange: SupportedRange,
            installedVersion: null,
            anyCreatableFile: false,
            anyWritableFile: true,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.Editable));
            Assert.That(diagnostic?.Code, Is.EqualTo("version-unknown"));
        });
    }

    [Test]
    public void DetectAdapterState_EmptyVersionRange_AcceptsAnyVersion()
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: true,
            anyDeclaredFileExists: true,
            supportedVersionRange: null,
            installedVersion: "99.0",
            anyCreatableFile: false,
            anyWritableFile: true,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.Editable));
            Assert.That(diagnostic, Is.Null);
        });
    }

    [Test]
    public void DetectAdapterState_AllFilesMissingWithTemplate_IsCreatable()
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: true,
            anyDeclaredFileExists: false,
            supportedVersionRange: SupportedRange,
            installedVersion: "1.31",
            anyCreatableFile: true,
            anyWritableFile: false,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.CreatableByTemplate));
            Assert.That(diagnostic, Is.Null);
        });
    }

    [Test]
    public void DetectAdapterState_AllFilesMissingWithoutTemplate_IsRawReadOnly()
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: true,
            anyDeclaredFileExists: false,
            supportedVersionRange: SupportedRange,
            installedVersion: "1.31",
            anyCreatableFile: false,
            anyWritableFile: false,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.ReadOnlyRaw));
            Assert.That(diagnostic?.Code, Is.EqualTo("files-missing"));
        });
    }

    [Test]
    public void DetectAdapterState_AllFilesReadOnly_IsRawReadOnly()
    {
        var state = ModConfigDetector.DetectAdapterState(
            registryHit: true,
            anyDeclaredFileExists: true,
            supportedVersionRange: SupportedRange,
            installedVersion: "1.31",
            anyCreatableFile: false,
            anyWritableFile: false,
            out var diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(ModConfigAdapterState.ReadOnlyRaw));
            Assert.That(diagnostic?.Code, Is.EqualTo("files-readonly"));
        });
    }

    [Test]
    public void DetectFileState_Matrix()
    {
        var editable = File();
        var readonlyFile = File(mode: ModConfigWriteMode.ReadOnly, readonlyReason: "语义危险");
        var custom = File(layout: ModConfigFileLayout.Custom, mode: ModConfigWriteMode.ReadOnly);
        var creatable = File(onMissing: ModConfigMissingFilePolicy.CreateFromTemplate, template: "fixtures/default.cfg");
        var missingError = File(onMissing: ModConfigMissingFilePolicy.Error);

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigDetector.DetectFileState(editable, exists: true), Is.EqualTo(ModConfigFileState.Editable));
            Assert.That(ModConfigDetector.DetectFileState(readonlyFile, exists: true), Is.EqualTo(ModConfigFileState.ReadOnly));
            Assert.That(ModConfigDetector.DetectFileState(custom, exists: true), Is.EqualTo(ModConfigFileState.ReadOnly),
                "custom 布局永远只读");
            Assert.That(ModConfigDetector.DetectFileState(creatable, exists: false), Is.EqualTo(ModConfigFileState.CreatableByTemplate));
            Assert.That(ModConfigDetector.DetectFileState(missingError, exists: false), Is.EqualTo(ModConfigFileState.Missing));
        });
    }

    [Test]
    public void BuildVersionOutOfRangeMessage_MatchesPlanWording()
    {
        var message = ModConfigDetector.BuildVersionOutOfRangeMessage("1.31", SupportedRange);

        Assert.That(
            message,
            Is.EqualTo("此 mod 版本 1.31 超出适配器支持范围（≥1.30, <2.0），为避免写坏配置已禁用编辑；请更新适配器或原始视图查看"));
    }

    [Test]
    public void ValidateFieldValue_Bool()
    {
        var field = Field("visible", ModConfigFieldType.Bool);

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "true", out _), Is.True);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "False", out _), Is.True);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "maybe", out var error), Is.False);
            Assert.That(error, Does.Contain("true 或 false"));
        });
    }

    [Test]
    public void ValidateFieldValue_IntRespectsRange()
    {
        var field = Field("port", ModConfigFieldType.Int, min: 1, max: 65535);

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "8080", out _), Is.True);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "0", out _), Is.False);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "70000", out _), Is.False);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "8.5", out var error), Is.False);
            Assert.That(error, Does.Contain("整数"));
        });
    }

    [Test]
    public void ValidateFieldValue_FloatRespectsRange()
    {
        var field = Field("MaxPhysicsDelta", ModConfigFieldType.Float, min: 0.01, max: 0.2);

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "0.05", out _), Is.True);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "0.005", out _), Is.False);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "0.5", out _), Is.False);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "NaN", out _), Is.False);
        });
    }

    [Test]
    public void ValidateFieldValue_EnumUsesWhitelistCaseInsensitively()
    {
        var field = Field("Language", ModConfigFieldType.Enum, values: ["en-us", "zh-cn"]);

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "zh-cn", out _), Is.True);
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "ZH-CN", out _), Is.True,
                "白名单比较忽略大小写，避免用户被无意义的差异挡住");
            Assert.That(ModConfigDetector.ValidateFieldValue(field, "de", out var error), Is.False);
            Assert.That(error, Does.Contain("en-us / zh-cn"));
        });
    }

    [Test]
    public void ValidateFieldValue_PatternAndReadonlyAndNewline()
    {
        var patterned = Field("key", ModConfigFieldType.String, pattern: "^Keypad[0-9]+$");
        var readonlyField = Field("padheap", ModConfigFieldType.ReadOnly);
        var plain = Field("name", ModConfigFieldType.String);

        Assert.Multiple(() =>
        {
            Assert.That(ModConfigDetector.ValidateFieldValue(patterned, "Keypad7", out _), Is.True);
            Assert.That(ModConfigDetector.ValidateFieldValue(patterned, "F7", out var patternError), Is.False);
            Assert.That(patternError, Does.Contain("格式"));
            Assert.That(ModConfigDetector.ValidateFieldValue(readonlyField, "1", out var readonlyError), Is.False);
            Assert.That(readonlyError, Does.Contain("只读"));
            Assert.That(ModConfigDetector.ValidateFieldValue(plain, null, out var emptyError), Is.False);
            Assert.That(emptyError, Does.Contain("不能为空"));
            Assert.That(ModConfigDetector.ValidateFieldValue(plain, "a\nb", out var newlineError), Is.False);
            Assert.That(newlineError, Does.Contain("换行"));
        });
    }
}
