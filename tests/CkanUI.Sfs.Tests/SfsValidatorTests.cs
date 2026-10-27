using System.Text;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsValidatorTests
{
    [Test]
    public void Validate_CanonicalSample_IsClean()
    {
        var result = SfsValidator.ValidateText(SfsSamples.CanonicalCrlf);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.HasWarnings, Is.False);
        Assert.That(result.IsClean, Is.True);
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ---------- 坏文件矩阵 1/9：少一个右花括号 ----------
    [Test]
    public void BadFile01_MissingClosingBrace_ReportsV01Error()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n\tSCENARIO\n\t{\n\t\tname = A\n\t}\n");

        var diagnostic = result.Find(SfsDiagnosticCodes.UnbalancedBraces);
        Assert.That(result.HasErrors, Is.True);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Error));
        Assert.That(diagnostic.Line, Is.GreaterThan(0));
        Assert.That(diagnostic.Message, Is.Not.Empty);
    }

    // ---------- 坏文件矩阵 2/9：末尾截断 ----------
    [Test]
    public void BadFile02_TruncatedFile_ReportsV01Errors()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n\tx = 1\n\tSCENARIO\n\t{\n");

        var errors = result.Diagnostics.Where(d => d.Code == SfsDiagnosticCodes.UnbalancedBraces).ToArray();

        Assert.That(result.HasErrors, Is.True);
        Assert.That(errors, Has.Length.EqualTo(2));
        Assert.That(errors.Select(d => d.Line), Is.EquivalentTo(new[] { 1, 4 }));
    }

    // ---------- 坏文件矩阵 3/9：注入 BOM ----------
    [Test]
    public void BadFile03_InjectedBom_ReportsV05Warning()
    {
        var body = new UTF8Encoding(false).GetBytes("GAME\r\n{\r\n\tx = 1\r\n}\r\n");
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);

        var result = SfsValidator.Validate(SfsParser.ParseBytes(bytes).Document!);

        var diagnostic = result.Find(SfsDiagnosticCodes.EncodingOrLineEnding);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Warning));
        Assert.That(diagnostic.Message, Does.Contain("BOM"));
        Assert.That(result.HasErrors, Is.False);
    }

    // ---------- 坏文件矩阵 4/9：CRLF/LF 混合 ----------
    [Test]
    public void BadFile04_MixedLineEndings_ReportV05Warning()
    {
        var result = SfsValidator.ValidateText("GAME\r\n{\n\tx = 1\r\n}\n");

        var diagnostic = result.Find(SfsDiagnosticCodes.EncodingOrLineEnding);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Warning));
        Assert.That(diagnostic.Line, Is.GreaterThan(0));
        Assert.That(result.HasErrors, Is.False);
    }

    // ---------- 坏文件矩阵 5/9：3 空格缩进 ----------
    [Test]
    public void BadFile05_ThreeSpaceIndentation_ReportsV06Warning()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n   x = 1\n}\n");

        var diagnostic = result.Find(SfsDiagnosticCodes.Indentation);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Warning));
        Assert.That(diagnostic.Line, Is.EqualTo(3));
        Assert.That(result.HasErrors, Is.False);
    }

    // ---------- 坏文件矩阵 6/9：NODE { 同行写法 ----------
    [Test]
    public void BadFile06_SameLineBrace_ParsesWithoutErrors()
    {
        var source = "GAME\n{\n\tNODE {\n\t\tx = 1\n\t}\n}\n";

        var result = SfsValidator.ValidateText(source);
        var formatted = SfsFormatter.FormatText(source);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.HasWarnings, Is.False);
        Assert.That(formatted.Text, Does.Contain("\tNODE\n\t{\n"));
    }

    // ---------- 坏文件矩阵 7/9：值里含花括号/等号/反斜杠 ----------
    [Test]
    public void BadFile07_ValuesWithSpecialCharacters_AreLiteralAndClean()
    {
        var source = "GAME\r\n"
            + "{\r\n"
            + "\tmods = A=B|C=D\r\n"
            + "\tbraces = {not a block}\r\n"
            + "\turl = GameData\\ModuleManager.TechTree\r\n"
            + "}\r\n";

        var document = SfsParser.Parse(source).Document!;
        var validation = SfsValidator.Validate(document);
        var formatted = SfsFormatter.Format(document);

        Assert.That(validation.HasErrors, Is.False);
        Assert.That(validation.HasWarnings, Is.False);
        Assert.That(formatted.Text, Is.EqualTo(source));
    }

    // ---------- 坏文件矩阵 8/9：100 层嵌套 ----------
    [Test]
    public void BadFile08_DeepNesting_ReportsV07WithoutStackOverflow()
    {
        var source = SfsSamples.Nested(100, "\n");

        var result = SfsValidator.ValidateText(source);

        var diagnostic = result.Find(SfsDiagnosticCodes.DepthOrLineLength);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Warning));
        Assert.That(diagnostic.Message, Does.Contain("100"));

        // 该样例的根叫 N0，因此另外还会有一条 V-02；这里只关心 V-07 被检出且没有崩溃。
        Assert.That(result.Contains(SfsDiagnosticCodes.RootNode), Is.True);
    }

    // ---------- 坏文件矩阵 9/9：单行 1MB ----------
    [Test]
    public void BadFile09_OneMegabyteLine_ReportsV07WithoutRunningOutOfMemory()
    {
        var source = "GAME\n{\n\tbig = " + new string('x', 1024 * 1024) + "\n}\n";

        var result = SfsValidator.ValidateText(source);

        var diagnostic = result.Find(SfsDiagnosticCodes.DepthOrLineLength);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Warning));
        Assert.That(diagnostic.Line, Is.EqualTo(3));
        Assert.That(result.HasErrors, Is.False);
    }

    [Test]
    public void Validate_WrongRootName_ReportsV02Error()
    {
        var result = SfsValidator.ValidateText("SAVE\n{\n\tx = 1\n}\n");

        var diagnostic = result.Find(SfsDiagnosticCodes.RootNode);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Error));
        Assert.That(diagnostic.Message, Does.Contain("GAME"));
    }

    [Test]
    public void Validate_MultipleRoots_ReportV02Error()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n}\nGAME\n{\n}\n");

        Assert.That(result.Contains(SfsDiagnosticCodes.RootNode), Is.True);
        Assert.That(result.HasErrors, Is.True);
    }

    [Test]
    public void Validate_DuplicateKeys_ReportV04WarningOnly()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n\tx = 1\n\tx = 2\n}\n");

        var diagnostic = result.Find(SfsDiagnosticCodes.DuplicateKey);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Warning));
        Assert.That(diagnostic.Line, Is.EqualTo(4));
        Assert.That(diagnostic.Message, Does.Contain("x"));
        Assert.That(result.HasErrors, Is.False);
    }

    [Test]
    public void Validate_ScenarioWithoutName_ReportsV08Warning()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n\tSCENARIO\n\t{\n\t\tid = x\n\t}\n}\n");

        var diagnostic = result.Find(SfsDiagnosticCodes.ScenarioName);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Warning));
        Assert.That(result.HasErrors, Is.False);
    }

    [Test]
    public void Validate_OpaqueLine_ReportsV03Error()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n\tbogus\n}\n");

        var diagnostic = result.Find(SfsDiagnosticCodes.OpaqueLine);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Severity, Is.EqualTo(SfsSeverity.Error));
    }

    [Test]
    public void ValidateFile_ReadsFromDiskAndReports()
    {
        using var workspace = new TempWorkspace("sfs-validate-file");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, "GAME\n{\n   x = 1\n}\n");

        var result = SfsValidator.ValidateFile(path);

        Assert.That(result.SourcePath, Is.EqualTo(path));
        Assert.That(result.Contains(SfsDiagnosticCodes.Indentation), Is.True);
        Assert.That(result.ToReport(), Does.Contain("V-06"));
    }

    [Test]
    public void ValidationResult_CountsAndReport_AreConsistent()
    {
        var result = SfsValidator.ValidateText("GAME\n{\n   x = 1\n\tx = 2\n\tx = 3\n}\n");

        Assert.That(result.ErrorCount, Is.EqualTo(result.Diagnostics.Count(d => d.Severity == SfsSeverity.Error)));
        Assert.That(result.WarningCount, Is.EqualTo(result.Diagnostics.Count(d => d.Severity == SfsSeverity.Warning)));
        Assert.That(result.IsClean, Is.False);
        Assert.That(result.ToReport(), Is.Not.Empty);
    }
}
