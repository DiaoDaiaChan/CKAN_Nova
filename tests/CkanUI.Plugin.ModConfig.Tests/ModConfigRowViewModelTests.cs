using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Plugin.ModConfig;
using NUnit.Framework;

namespace CkanUI.Plugin.ModConfig.Tests;

/// <summary>
/// 字段行 / 卡片 / 适配器清单行：文案、搜索三路匹配、已改判定、可编辑判定、风险与重启标记。
/// </summary>
[TestFixture]
public sealed class ModConfigRowViewModelTests
{
    private static ModConfigFieldRowViewModel Row(ModConfigFieldState field, bool isFileWritable = true)
        => new(field, new ModConfigFieldRendererRegistry().Resolve(field.Type), isFileWritable);

    [Test]
    public void Label_FallsBackToKey()
    {
        Assert.That(Row(ModConfigTestData.Field("enabled", label: "")).Label, Is.EqualTo("enabled"));
        Assert.That(Row(ModConfigTestData.Field("enabled", label: "退款")).Label, Is.EqualTo("退款"));
    }

    [Test]
    public void Matches_Key()
    {
        var row = Row(ModConfigTestData.Field("MaxPhysicsDelta"));

        Assert.That(row.Matches("physics"), Is.True);
        Assert.That(row.Matches("PHYSICS"), Is.True);
        Assert.That(row.Matches("nope"), Is.False);
        Assert.That(row.Matches("   "), Is.True);
        Assert.That(row.Matches(null), Is.True);
    }

    [Test]
    public void Matches_LabelAndComment()
    {
        var row = Row(ModConfigTestData.Field("RefundingOnRecovery", label: "回收时退款", comment: "关掉会让回收变成纯亏损"));

        Assert.That(row.Matches("退款"), Is.True);
        Assert.That(row.Matches("纯亏损"), Is.True);
        Assert.That(row.Matches("储罐"), Is.False);
    }

    [Test]
    public void IsModified_ComparesAgainstDefaultIgnoringSurroundingSpace()
    {
        Assert.That(Row(ModConfigTestData.Field("a", value: "2", defaultValue: "1")).IsModified, Is.True);
        Assert.That(Row(ModConfigTestData.Field("a", value: " 1 ", defaultValue: "1")).IsModified, Is.False);
        Assert.That(Row(ModConfigTestData.Field("a", value: "1")).IsModified, Is.False);
    }

    [Test]
    public void CanEdit_RequiresWritableFileAndNonReadonlyType()
    {
        Assert.That(Row(ModConfigTestData.Field("a", value: "1")).CanEdit, Is.True);
        Assert.That(Row(ModConfigTestData.Field("a", value: "1"), isFileWritable: false).CanEdit, Is.False);
        Assert.That(Row(ModConfigTestData.Field("a", ModConfigFieldType.ReadOnly, "1")).CanEdit, Is.False);
    }

    [Test]
    public void Hint_ShowsRangeWithUnit()
    {
        var row = Row(ModConfigTestData.Field("timeout", ModConfigFieldType.Int, "30", min: 1, max: 65535, unit: "秒"));

        Assert.That(row.HintText, Is.EqualTo("范围 1 ~ 65535 秒"));
        Assert.That(row.HasHint, Is.True);
    }

    [Test]
    public void Hint_ShowsLowerBoundOnly()
    {
        Assert.That(Row(ModConfigTestData.Field("d", ModConfigFieldType.Float, "0.05", min: 0.01)).HintText,
            Is.EqualTo("不小于 0.01"));
    }

    [Test]
    public void Hint_ShowsUpperBoundOnly()
    {
        Assert.That(Row(ModConfigTestData.Field("d", ModConfigFieldType.Float, "0.05", max: 0.2)).HintText,
            Is.EqualTo("不大于 0.2"));
    }

    [Test]
    public void Hint_ShowsUnitForNonNumericTypes()
    {
        Assert.That(Row(ModConfigTestData.Field("note", unit: "秒")).HintText, Is.EqualTo("单位 秒"));
        Assert.That(Row(ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "true")).HintText, Is.Empty);
    }

    [Test]
    public void RiskAndRestartMarks()
    {
        var high = Row(ModConfigTestData.Field("pad", risk: ModConfigFieldRisk.High, requiresRestart: true));
        var medium = Row(ModConfigTestData.Field("pad", risk: ModConfigFieldRisk.Medium));
        var low = Row(ModConfigTestData.Field("pad"));

        Assert.That(high.RiskText, Is.EqualTo("高风险"));
        Assert.That(high.HasRisk, Is.True);
        Assert.That(high.RestartText, Is.EqualTo("需重启"));
        Assert.That(high.HasRestartMark, Is.True);
        Assert.That(medium.RiskText, Is.EqualTo("注意"));
        Assert.That(low.RiskText, Is.Empty);
        Assert.That(low.HasRisk, Is.False);
        Assert.That(low.HasRestartMark, Is.False);
    }

    [Test]
    public void ValueAndChoicesComeFromRendererAndField()
    {
        var enumRow = Row(ModConfigTestData.Field("lang", ModConfigFieldType.Enum, "en-us", values: ["en-us", "zh-cn"]));
        var boolRow = Row(ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "true"));

        Assert.That(enumRow.ValueText, Is.EqualTo("en-us"));
        Assert.That(enumRow.Choices, Is.EqualTo(new[] { "en-us", "zh-cn" }));
        Assert.That(enumRow.EditorKind, Is.EqualTo(ModConfigEditorKind.Dropdown));
        Assert.That(boolRow.ValueText, Is.EqualTo("开"));
        Assert.That(boolRow.Choices, Is.Empty);
        Assert.That(boolRow.EditorKind, Is.EqualTo(ModConfigEditorKind.Toggle));
    }

    [Test]
    public void CommentAndGroup()
    {
        var row = Row(ModConfigTestData.Field("a", comment: "第一行注释", group: "游戏性"));

        Assert.That(row.CommentText, Is.EqualTo("第一行注释"));
        Assert.That(row.HasComment, Is.True);
        Assert.That(row.GroupTitle, Is.EqualTo("游戏性"));
        Assert.That(Row(ModConfigTestData.Field("b")).HasComment, Is.False);
    }

    [TestCase(ModConfigAdapterState.Editable, "可编辑", "已适配")]
    [TestCase(ModConfigAdapterState.CreatableByTemplate, "可按模板创建", "待创建")]
    [TestCase(ModConfigAdapterState.ReadOnlyVersion, "版本超出适配器范围（只读）", "只读")]
    [TestCase(ModConfigAdapterState.ReadOnlyRaw, "只读（原始视图）", "只读")]
    [TestCase(ModConfigAdapterState.NotInstalled, "未安装", "未安装")]
    public void Card_StateAndBadgeTexts(ModConfigAdapterState state, string stateText, string badgeText)
    {
        var card = new ModConfigModCardViewModel(ModConfigTestData.Mod(state: state));

        Assert.That(card.StateText, Is.EqualTo(stateText));
        Assert.That(card.BadgeText, Is.EqualTo(badgeText));
    }

    [Test]
    public void Card_VersionModifiedAndRangeTexts()
    {
        var card = new ModConfigModCardViewModel(ModConfigTestData.Mod(
            installedVersion: "1.31.0",
            modifiedCount: 2,
            supportedVersionRangeText: "≥1.30, <2.0"));

        Assert.That(card.VersionText, Is.EqualTo("v1.31.0"));
        Assert.That(card.HeaderText, Is.EqualTo("Demo Mod v1.31.0"));
        Assert.That(card.ModifiedText, Is.EqualTo("2 项已改"));
        Assert.That(card.HasModified, Is.True);
        Assert.That(card.SupportedRangeText, Is.EqualTo("适配器支持 ≥1.30, <2.0"));
        Assert.That(card.Homepage, Is.Empty);
    }

    [Test]
    public void Card_VersionUnknownWhenEmpty()
    {
        var card = new ModConfigModCardViewModel(ModConfigTestData.Mod(installedVersion: null));

        Assert.That(card.VersionText, Is.EqualTo("版本未知"));
        Assert.That(card.HeaderText, Is.EqualTo("Demo Mod 版本未知"));
        Assert.That(card.ModifiedText, Is.Empty);
        Assert.That(card.HasModified, Is.False);
        Assert.That(card.SupportedRangeText, Is.Empty);
    }

    [Test]
    public void Card_PrefersErrorDiagnosticAsWarning()
    {
        var card = new ModConfigModCardViewModel(ModConfigTestData.Mod(diagnostics:
        [
            ModConfigDiagnostic.Warning("version-unknown", "已安装版本未知"),
            ModConfigDiagnostic.Error("adapter-load-failed", "读取失败"),
        ]));

        Assert.That(card.WarningText, Is.EqualTo("读取失败"));
        Assert.That(card.HasWarning, Is.True);
        Assert.That(new ModConfigModCardViewModel(ModConfigTestData.Mod()).HasWarning, Is.False);
    }

    [Test]
    public void Card_IsEditableCoversCreatableByTemplate()
    {
        Assert.That(new ModConfigModCardViewModel(ModConfigTestData.Mod(state: ModConfigAdapterState.CreatableByTemplate)).IsEditable, Is.True);
        Assert.That(new ModConfigModCardViewModel(ModConfigTestData.Mod(state: ModConfigAdapterState.ReadOnlyVersion)).IsEditable, Is.False);
    }

    [Test]
    public void AdapterSummary_ShowsSourceRangeAndCounts()
    {
        var builtin = new ModConfigAdapterSummaryViewModel(ModConfigTestData.Adapter());
        var user = new ModConfigAdapterSummaryViewModel(ModConfigTestData.Adapter(
            id: "user-mod",
            isUserProvided: true,
            versionRange: [">=1.2.0", "<2.0"]));

        Assert.That(builtin.SourceText, Is.EqualTo("内置"));
        Assert.That(builtin.RangeText, Is.EqualTo("不限"));
        Assert.That(builtin.FileCountText, Is.EqualTo("1 个配置文件"));
        Assert.That(builtin.FieldCountText, Is.EqualTo("1 个字段"));
        Assert.That(user.SourceText, Is.EqualTo("用户目录"));
        Assert.That(user.RangeText, Is.EqualTo(">=1.2.0, <2.0"));
    }
}
