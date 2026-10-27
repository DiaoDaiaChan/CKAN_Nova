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
}
