using System.Text;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsCliTests
{
    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CkanUI.Sfs.Cli.SfsCli.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Test]
    public void Run_NoArguments_ReturnsUsageError()
    {
        var result = Run();

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Not.Empty);
    }

    [Test]
    public void Run_UnknownCommand_ReturnsUsageError()
    {
        var result = Run("frobnicate", "x.sfs");

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Not.Empty);
    }

    [Test]
    public void Run_HelpCommand_ReturnsZero()
    {
        var result = Run("help");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("format"));
        Assert.That(result.Output, Does.Contain("validate"));
    }

    [Test]
    public void Format_Default_PrintsCanonicalTextAndDoesNotTouchFile()
    {
        using var workspace = new TempWorkspace("sfs-cli-format");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.Messy);
        var before = SfsSamples.ReadBytes(path);

        var result = Run("format", path);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("\tversion = 1.0"));
        Assert.That(result.Output, Does.Contain("\tSCENARIO"));
        Assert.That(SfsSamples.ReadBytes(path), Is.EqualTo(before));
    }

    [Test]
    public void Format_Check_NonCanonical_ReturnsOne()
    {
        using var workspace = new TempWorkspace("sfs-cli-check1");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.Messy);

        var result = Run("format", path, "--check");

        Assert.That(result.ExitCode, Is.EqualTo(1));
    }

    [Test]
    public void Format_Check_Canonical_ReturnsZero()
    {
        using var workspace = new TempWorkspace("sfs-cli-check0");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.CanonicalCrlf);

        var result = Run("format", path, "--check");

        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public void Format_CheckWithDiff_PrintsSomething()
    {
        using var workspace = new TempWorkspace("sfs-cli-diff");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.Messy);

        var result = Run("format", path, "--check", "--diff");

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Not.Empty);
    }

    [Test]
    public void Format_Write_RewritesFileKeepsTimestampAndCreatesBackup()
    {
        using var workspace = new TempWorkspace("sfs-cli-write");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.Messy);
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        var result = Run("format", path, "--write");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(File.ReadAllText(path, Encoding.UTF8), Is.EqualTo(SfsSamples.CanonicalMessy));
        Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(stamp));
        Assert.That(File.Exists(path + ".sfsbak"), Is.True);
    }

    [Test]
    public void Format_Out_WritesSeparateFileAndLeavesOriginal()
    {
        using var workspace = new TempWorkspace("sfs-cli-out");
        var path = workspace.Combine("savegame.sfs");
        var outPath = workspace.Combine("formatted.sfs");
        SfsSamples.WriteFile(path, SfsSamples.Messy);
        var before = SfsSamples.ReadBytes(path);

        var result = Run("format", path, "--out", outPath);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(File.Exists(outPath), Is.True);
        Assert.That(File.ReadAllText(outPath, Encoding.UTF8), Is.EqualTo(SfsSamples.CanonicalMessy));
        Assert.That(SfsSamples.ReadBytes(path), Is.EqualTo(before));
    }

    [Test]
    public void Format_UnknownOption_ReturnsUsageError()
    {
        using var workspace = new TempWorkspace("sfs-cli-badopt");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.CanonicalCrlf);

        var result = Run("format", path, "--frobnicate");

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Not.Empty);
    }

    [Test]
    public void Format_MissingFileArgument_ReturnsUsageError()
    {
        var result = Run("format");

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Not.Empty);
    }

    [Test]
    public void Format_WriteAndCheckTogether_ReturnsUsageError()
    {
        using var workspace = new TempWorkspace("sfs-cli-conflict");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.CanonicalCrlf);

        var result = Run("format", path, "--write", "--check");

        Assert.That(result.ExitCode, Is.EqualTo(2));
    }

    [Test]
    public void Format_NonExistentFile_ReturnsTwo()
    {
        using var workspace = new TempWorkspace("sfs-cli-notfound");
        var path = workspace.Combine("missing.sfs");

        var result = Run("format", path);

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Not.Empty);
    }

    [Test]
    public void Format_WriteOnBrokenFile_ReturnsOneAndLeavesFileIntact()
    {
        using var workspace = new TempWorkspace("sfs-cli-broken");
        var path = workspace.Combine("savegame.sfs");
        var broken = "GAME\n{\n\tx = 1\n";
        SfsSamples.WriteFile(path, broken);

        var result = Run("format", path, "--write");

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(File.ReadAllText(path, Encoding.UTF8), Is.EqualTo(broken));
        Assert.That(File.Exists(path + ".sfsbak"), Is.False);
    }

    [Test]
    public void Format_EolLf_PrintsLfText()
    {
        using var workspace = new TempWorkspace("sfs-cli-eol");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.CanonicalCrlf);

        var result = Run("format", path, "--eol=lf");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Not.Contain("\r"));
    }

    [Test]
    public void Validate_CleanFile_ReturnsZero()
    {
        using var workspace = new TempWorkspace("sfs-cli-validate0");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.CanonicalCrlf);

        var result = Run("validate", path);

        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public void Validate_BadFile_ReturnsOne()
    {
        using var workspace = new TempWorkspace("sfs-cli-validate1");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, "GAME\n{\n\tbogus\n}\n");

        var result = Run("validate", path);

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Does.Contain("V-03"));
    }

    [Test]
    public void Validate_NonExistentFile_ReturnsTwo()
    {
        using var workspace = new TempWorkspace("sfs-cli-validate2");
        var path = workspace.Combine("missing.sfs");

        var result = Run("validate", path);

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Not.Empty);
    }

    [Test]
    public void Validate_UsageWithoutFile_ReturnsTwo()
    {
        var result = Run("validate");

        Assert.That(result.ExitCode, Is.EqualTo(2));
    }

    [Test]
    public void Outline_PrintsCareerSummary()
    {
        using var workspace = new TempWorkspace("sfs-cli-outline");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);

        var result = Run("outline", path);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        Assert.That(result.Output, Does.Contain("资金"));
        Assert.That(result.Output, Does.Contain("1234.5"));
        Assert.That(result.Output, Does.Contain("[存档信息]"));
        Assert.That(result.Output, Does.Contain("[飞行器]"));
    }

    [Test]
    public void Outline_JsonExposesSnapshotAndSections()
    {
        using var workspace = new TempWorkspace("sfs-cli-outline-json");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);

        var result = Run("outline", path, "--json");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        using var json = System.Text.Json.JsonDocument.Parse(result.Output);
        var snapshot = json.RootElement.GetProperty("snapshot");
        Assert.That(snapshot.GetProperty("Funds").GetProperty("Value").GetString(), Is.EqualTo("1234.5"));
        Assert.That(snapshot.GetProperty("Science").GetProperty("Value").GetString(), Is.EqualTo("42"));
        Assert.That(json.RootElement.GetProperty("sections").GetArrayLength(), Is.GreaterThan(2));
    }

    [Test]
    public void Search_FindsFieldByValueAndByLabel()
    {
        using var workspace = new TempWorkspace("sfs-cli-search");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);

        var byValue = Run("search", path, "1234.5");
        Assert.That(byValue.ExitCode, Is.EqualTo(0), byValue.Error);
        Assert.That(byValue.Output, Does.Contain("funds"));

        var byLabel = Run("search", path, "资金");
        Assert.That(byLabel.Output, Does.Contain("funds"));
    }

    [Test]
    public void Search_NoMatch_ExitsZeroWithMessage()
    {
        using var workspace = new TempWorkspace("sfs-cli-search-none");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);

        var result = Run("search", path, "no-such-key");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        Assert.That(result.Output, Does.Contain("没有匹配"));
    }

    [Test]
    public void Set_WithoutWriteKeepsFileUntouched()
    {
        using var workspace = new TempWorkspace("sfs-cli-set-dry");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var before = File.ReadAllText(path);
        var line = SfsWorkbenchSamples.LineOf(before, "funds");

        var result = Run("set", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "999");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        Assert.That(result.Error, Does.Contain("未写盘"));
    }

    [Test]
    public void Set_WritesOnlyTheTargetLine()
    {
        using var workspace = new TempWorkspace("sfs-cli-set");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var before = File.ReadAllText(path);
        var lines = before.Split("\r\n");
        var line = SfsWorkbenchSamples.LineOf(before, "funds");

        var result = Run("set", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "999", "--write");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        var after = File.ReadAllText(path).Split("\r\n");
        Assert.That(after.Length, Is.EqualTo(lines.Length));
        Assert.That(after[line - 1], Does.Contain("999"));
        for (var index = 0; index < lines.Length; index++)
        {
            if (index == line - 1)
            {
                continue;
            }

            Assert.That(after[index], Is.EqualTo(lines[index]), $"第 {index + 1} 行被意外改动");
        }
    }

    [Test]
    public void Set_WithOutWritesCopyAndLeavesSource()
    {
        using var workspace = new TempWorkspace("sfs-cli-set-out");
        var path = workspace.Combine("career.sfs");
        var outPath = workspace.Combine("edited.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var before = File.ReadAllText(path);
        var line = SfsWorkbenchSamples.LineOf(before, "funds");

        var result = Run("set", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "777", "--out", outPath);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        Assert.That(File.Exists(outPath), Is.True);
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        Assert.That(File.ReadAllText(outPath), Does.Contain("777"));
    }

    [Test]
    public void Set_RejectsNewlineValueWithoutWriting()
    {
        using var workspace = new TempWorkspace("sfs-cli-set-newline");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var before = File.ReadAllText(path);
        var line = SfsWorkbenchSamples.LineOf(before, "funds");

        var result = Run("set", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "a\nb", "--write");

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
    }

    [Test]
    public void Set_OnBlockLineIsRejected()
    {
        using var workspace = new TempWorkspace("sfs-cli-set-block");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var before = File.ReadAllText(path);
        var line = SfsWorkbenchSamples.LineOf(before, "FLIGHTSTATE");

        var result = Run("set", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "--write");

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
    }

    [Test]
    public void Duplicate_WriteLeavesSnapshotAndVerifies()
    {
        using var workspace = new TempWorkspace("sfs-cli-dup");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var before = File.ReadAllText(path);
        var line = SfsWorkbenchSamples.LineOf(before, "MODULE");

        var result = Run("duplicate", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "--write");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        Assert.That(File.Exists(path + ".sfsbak"), Is.True);
        var after = File.ReadAllText(path);
        Assert.That(after.Length, Is.GreaterThan(before.Length));
        Assert.That(Count(after, "ModuleCommand"), Is.EqualTo(2));
    }

    [Test]
    public void Delete_RemovesWholeBlockAndKeepsRest()
    {
        using var workspace = new TempWorkspace("sfs-cli-del");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var before = File.ReadAllText(path);
        var line = SfsWorkbenchSamples.LineOf(before, "MODULE");

        var result = Run("delete", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "--write");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        var after = File.ReadAllText(path);
        Assert.That(after, Does.Not.Contain("ModuleCommand"));
        Assert.That(after, Does.Contain("mk1pod"));
        Assert.That(after, Does.Contain("Jebediah Kerman"));
    }

    [Test]
    public void Insert_AddsIndentedKeyToBlock()
    {
        using var workspace = new TempWorkspace("sfs-cli-insert");
        var path = workspace.Combine("career.sfs");
        SfsSamples.WriteFile(path, SfsWorkbenchSamples.Career);
        var line = SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "FLIGHTSTATE");

        var result = Run("insert", path, line.ToString(System.Globalization.CultureInfo.InvariantCulture), "customTag", "hello", "--write");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.Error);
        var after = File.ReadAllText(path);
        Assert.That(after, Does.Contain("customTag = hello"));
        Assert.That(after, Does.Contain("vesselCount = 1"));
    }

    [Test]
    public void Edit_RejectsFileWithParseErrors()
    {
        using var workspace = new TempWorkspace("sfs-cli-edit-broken");
        var path = workspace.Combine("broken.sfs");
        const string broken = "GAME\r\n{\r\n\tversion = 1.0\r\n";
        SfsSamples.WriteFile(path, broken);

        var result = Run("set", path, "2", "2.0", "--write");

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo(broken));
    }

    private static int Count(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
