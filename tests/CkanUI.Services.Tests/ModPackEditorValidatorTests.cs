using System.Linq;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ModPacks;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="ModPackEditorValidator"/> 的离线用例（06 篇 UI-24、01 篇 F-46）：纯函数、不读盘不写盘。
/// 覆盖标识大小写/字符集、版本可解析性、许可证白名单、kind 与 download 的联动、枚举白名单、
/// 关系行的类型/模式/名字/版本范围/重复，以及"错误排在提醒前面"的排序口径。
/// </summary>
[TestFixture]
public sealed class ModPackEditorValidatorTests
{
    private static ModPackEditorDocument Valid() => new()
    {
        FilePath = "C:/tmp/my-modpack.ckan",
        Identifier = "my-modpack",
        Name = "我的整合包",
        Abstract = "一句话摘要",
        Version = "1.0.0",
        Licenses = new[] { "MIT" },
        Authors = new[] { "Tester" },
        Kind = "metapackage",
        ReleaseStatus = "stable",
    };

    private static string[] Codes(ModPackEditorDocument document)
        => ModPackEditorValidator.Validate(document).Select(issue => issue.Code).ToArray();

    // ---------------- 基线 ----------------

    [Test]
    public void Validate_ValidDocument_ReturnsNoIssues()
    {
        var issues = ModPackEditorValidator.Validate(Valid());

        Assert.That(issues, Is.Empty);
        Assert.That(ModPackEditorValidator.HasError(issues), Is.False);
    }

    [Test]
    public void Validate_NullDocument_ReportsDocumentNull()
    {
        var issues = ModPackEditorValidator.Validate(null);

        Assert.That(issues.Count, Is.EqualTo(1));
        Assert.That(issues[0].Code, Is.EqualTo("document.null"));
        Assert.That(issues[0].Severity, Is.EqualTo(ModPackEditorIssueSeverity.Error));
    }

    // ---------------- 标识 ----------------

    [Test]
    public void Validate_EmptyIdentifier_ReportsIdentifierEmpty()
    {
        Assert.That(Codes(Valid() with { Identifier = "  " }), Does.Contain("identifier.empty"));
    }

    [Test]
    public void Validate_UppercaseIdentifier_ReportsLowercaseError()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with { Identifier = "My-Modpack" });

        Assert.That(issues.Select(issue => issue.Code), Does.Contain("identifier.lowercase"));
        Assert.That(issues.Single(issue => issue.Code == "identifier.lowercase").Field, Is.EqualTo("Identifier"));
    }

    [Test]
    public void Validate_IdentifierWithIllegalCharacters_ReportsCharsetError()
    {
        Assert.That(Codes(Valid() with { Identifier = "my_modpack" }), Does.Contain("identifier.charset"));
        Assert.That(Codes(Valid() with { Identifier = "a" }), Does.Contain("identifier.charset"));
    }

    [Test]
    public void Validate_EmptyNameAndAbstract_ReportBothErrors()
    {
        var codes = Codes(Valid() with { Name = string.Empty, Abstract = " " });

        Assert.That(codes, Does.Contain("name.empty"));
        Assert.That(codes, Does.Contain("abstract.empty"));
    }

    [Test]
    public void Validate_MissingAuthor_IsOnlyWarning()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with { Authors = System.Array.Empty<string>() });

        Assert.That(issues.Count, Is.EqualTo(1));
        Assert.That(issues[0].Code, Is.EqualTo("author.empty"));
        Assert.That(issues[0].Severity, Is.EqualTo(ModPackEditorIssueSeverity.Warning));
        Assert.That(ModPackEditorValidator.HasError(issues), Is.False);
    }

    // ---------------- 版本 ----------------

    [Test]
    public void Validate_UnparsableVersion_ReportsVersionInvalid()
    {
        Assert.That(Codes(Valid() with { Version = "一.零" }), Does.Contain("version.invalid"));
    }

    [Test]
    public void Validate_UnparsableGameVersion_ReportsFieldSpecificCode()
    {
        var codes = Codes(Valid() with { KspVersionMin = "abc" });

        Assert.That(codes, Does.Contain("KspVersionMin.invalid"));
    }

    [Test]
    public void Validate_GameVersionRangeInverted_IsOnlyWarning()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with { KspVersionMin = "1.12.9", KspVersionMax = "1.12.0" });

        Assert.That(issues.Select(issue => issue.Code), Does.Contain("ksp_version.range"));
        Assert.That(ModPackEditorValidator.HasError(issues), Is.False);
    }

    // ---------------- 许可证 / 枚举 / 下载 ----------------

    [Test]
    public void Validate_EmptyLicenses_ReportsLicenseEmpty()
    {
        Assert.That(Codes(Valid() with { Licenses = System.Array.Empty<string>() }), Does.Contain("license.empty"));
    }

    [Test]
    public void Validate_LicenseOutsideWhitelist_ReportsLicenseInvalid()
    {
        Assert.That(Codes(Valid() with { Licenses = new[] { "MIT", "Not-A-License" } }), Does.Contain("license.invalid"));
    }

    [Test]
    public void Validate_UnknownEnumValues_ReportBothErrors()
    {
        var codes = Codes(Valid() with { ReleaseStatus = "beta", Kind = "plugin" });

        Assert.That(codes, Does.Contain("release_status.invalid"));
        Assert.That(codes, Does.Contain("kind.invalid"));
    }

    [Test]
    public void Validate_PackageKindWithoutDownload_ReportsDownloadRequired()
    {
        Assert.That(Codes(Valid() with { Kind = "package" }), Does.Contain("download.required"));
    }

    [Test]
    public void Validate_NonHttpDownload_ReportsDownloadInvalid()
    {
        var codes = Codes(Valid() with { Kind = "package", DownloadUrl = "ftp://example.com/pack.zip" });

        Assert.That(codes, Does.Contain("download.invalid"));
        Assert.That(codes, Does.Not.Contain("download.required"));
    }

    [Test]
    public void Validate_PathWithoutCkanSuffix_IsOnlyWarning()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with { FilePath = "C:/tmp/my-modpack.json" });

        Assert.That(issues.Count, Is.EqualTo(1));
        Assert.That(issues[0].Code, Is.EqualTo("path.extension"));
        Assert.That(ModPackEditorValidator.HasError(issues), Is.False);
    }

    // ---------------- 关系行 ----------------

    [Test]
    public void Validate_RelationWithUppercaseName_ReportsRowIndex()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with
        {
            Relations = new[]
            {
                new ModPackRelation { Name = "ModuleManager" },
            },
        });

        var issue = issues.Single(item => item.Code == "relation.name.lowercase");
        Assert.That(issue.RowIndex, Is.EqualTo(0));
        Assert.That(issue.Message, Does.Contain("第 1 行"));
    }

    [Test]
    public void Validate_UnknownRelationType_ReportsRelationType()
    {
        Assert.That(
            Codes(Valid() with { Relations = new[] { new ModPackRelation { RelationType = "needs", Name = "modulemanager" } } }),
            Does.Contain("relation.type"));
    }

    [Test]
    public void Validate_UnknownRelationMode_ReportsRelationMode()
    {
        Assert.That(
            Codes(Valid() with { Relations = new[] { new ModPackRelation { Mode = "some", Name = "modulemanager" } } }),
            Does.Contain("relation.mode"));
    }

    [Test]
    public void Validate_AnyOfWithSingleCandidate_IsOnlyWarning()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with
        {
            Relations = new[]
            {
                new ModPackRelation
                {
                    Mode = ModPackRelationModes.AnyOf,
                    Candidates = new[] { "modulemanager" },
                },
            },
        });

        Assert.That(issues.Select(issue => issue.Code), Does.Contain("relation.any_of.single"));
        Assert.That(ModPackEditorValidator.HasError(issues), Is.False);
    }

    [Test]
    public void Validate_CandidateModeWithoutCandidates_ReportsEmptyCandidates()
    {
        var codes = Codes(Valid() with
        {
            Relations = new[] { new ModPackRelation { Mode = ModPackRelationModes.AnyOf } },
        });

        Assert.That(codes, Does.Contain("relation.candidates.empty"));
    }

    [Test]
    public void Validate_UnparsableRelationVersion_ReportsRelationVersionInvalid()
    {
        var codes = Codes(Valid() with
        {
            Relations = new[]
            {
                new ModPackRelation { Name = "modulemanager", MinVersion = "1.0 beta" },
            },
        });

        Assert.That(codes, Does.Contain("relation.version.invalid"));
    }

    [Test]
    public void Validate_KspVersionTogetherWithRange_IsError()
    {
        // 上游 CkanModule.CheckHealth：ksp_version 与 ksp_version_(min|max) 互斥，混写会 InvalidModuleAttributesKraken。
        var issues = ModPackEditorValidator.Validate(Valid() with
        {
            KspVersion = "1.12.5",
            KspVersionMax = "1.12.9",
        });

        Assert.That(issues.Select(issue => issue.Code), Does.Contain("ksp_version.conflict"));
        Assert.That(issues.Single(issue => issue.Code == "ksp_version.conflict").Severity, Is.EqualTo(ModPackEditorIssueSeverity.Error));
    }

    [Test]
    public void Validate_DuplicateRelation_IsOnlyWarning()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with
        {
            Relations = new[]
            {
                new ModPackRelation { Name = "modulemanager" },
                new ModPackRelation { Name = "modulemanager" },
            },
        });

        Assert.That(issues.Select(issue => issue.Code), Does.Contain("relation.duplicate"));
        Assert.That(ModPackEditorValidator.HasError(issues), Is.False);
    }

    [Test]
    public void Validate_ErrorsAreSortedBeforeWarnings()
    {
        var issues = ModPackEditorValidator.Validate(Valid() with
        {
            Identifier = "Bad-Ident",
            Authors = System.Array.Empty<string>(),
        });

        Assert.That(issues.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(issues[0].Severity, Is.EqualTo(ModPackEditorIssueSeverity.Error));
        Assert.That(issues[^1].Severity, Is.EqualTo(ModPackEditorIssueSeverity.Warning));
    }

    [Test]
    public void Validate_IsPure_DoesNotTouchDiskOrThrow()
    {
        var document = Valid() with
        {
            FilePath = "Z:/definitely/not/here/x.ckan",
            Relations = new[] { new ModPackRelation { Name = "modulemanager" } },
        };

        var first = ModPackEditorValidator.Validate(document);
        var second = ModPackEditorValidator.Validate(document);

        Assert.That(first.Count, Is.EqualTo(second.Count));
        Assert.That(System.IO.File.Exists(document.FilePath), Is.False);
    }
}
