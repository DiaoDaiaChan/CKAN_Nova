using CkanUI.TestSupport;
using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 稳定版容差文件（&lt;实例目录&gt;/CKAN/stability_tolerance.json）的读写契约。
/// </summary>
/// <remarks>
/// Core 侧用 Newtonsoft 的 <c>JsonReleaseStatusConverter</c>（StringEnumConverter 派生）序列化
/// <c>ReleaseStatus</c>，所以文件里的取值是 <c>"stable" / "testing" / "development"</c> 字符串；
/// <c>overall_stability_tolerance</c> 在等于 stable 时会被 <c>DefaultValueHandling.IgnoreAndPopulate</c>
/// 省略，因此「字段缺失 = stable」是正常状态而不是损坏文件。
/// </remarks>
[TestFixture]
public sealed class SettingsStabilityToleranceTests
{
    private TempWorkspace? workspace;

    private string TolerancePath => StabilityToleranceFile.PathFor(workspace!.Path);

    [SetUp]
    public void SetUp()
    {
        // 系统临时目录在受限沙箱里不可写，统一落在仓库根的 artifacts/tmp（见 TempWorkspace 注释）。
        workspace = new TempWorkspace("settings-tol");
        Directory.CreateDirectory(workspace.Combine(StabilityToleranceFile.DirectoryName));
    }

    [TearDown]
    public void TearDown()
    {
        workspace?.Dispose();
        workspace = null;
    }

    [Test]
    public void PathFor_PutsFileUnderInstanceCkanDirectory()
    {
        Assert.That(
            StabilityToleranceFile.PathFor("D:/KSP"),
            Is.EqualTo(Path.Combine("D:/KSP", "CKAN", "stability_tolerance.json")));
        Assert.That(StabilityToleranceFile.PathFor("D:/KSP"), Does.EndWith("stability_tolerance.json"));
    }

    [Test]
    public async Task ReadAsync_MissingFile_ReturnsStable()
    {
        var result = await new StabilityToleranceFile().ReadAsync(TolerancePath);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo("stable"));
    }

    [Test]
    public async Task ReadAsync_ReadsOverallToleranceAndIgnoresModOverrides()
    {
        await File.WriteAllTextAsync(
            TolerancePath,
            "{\"overall_stability_tolerance\":\"development\",\"mod_stability_tolerance\":{\"SomeMod\":\"testing\"}}");

        var result = await new StabilityToleranceFile().ReadAsync(TolerancePath);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo("development"));
    }

    [Test]
    public async Task ReadAsync_UnknownOrEmptyValue_FallsBackToStable()
    {
        await File.WriteAllTextAsync(TolerancePath, "{\"overall_stability_tolerance\":\"beta\"}");
        Assert.That((await new StabilityToleranceFile().ReadAsync(TolerancePath)).Value, Is.EqualTo("stable"));

        await File.WriteAllTextAsync(TolerancePath, "{}");
        Assert.That((await new StabilityToleranceFile().ReadAsync(TolerancePath)).Value, Is.EqualTo("stable"));
    }

    [Test]
    public async Task ReadAsync_InvalidJson_Fails()
    {
        await File.WriteAllTextAsync(TolerancePath, "{ not json");

        var result = await new StabilityToleranceFile().ReadAsync(TolerancePath);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("稳定版容差文件不是合法 JSON"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task WriteAsync_PreservesModOverrides()
    {
        await File.WriteAllTextAsync(
            TolerancePath,
            "{\"overall_stability_tolerance\":\"stable\",\"mod_stability_tolerance\":{\"SomeMod\":\"testing\"}}");

        var result = await new StabilityToleranceFile().WriteAsync(TolerancePath, "development");

        Assert.That(result.IsSuccess, Is.True);
        var text = await File.ReadAllTextAsync(TolerancePath);
        Assert.That(text, Does.Contain("\"overall_stability_tolerance\": \"development\""));
        Assert.That(text, Does.Contain("mod_stability_tolerance"));
        Assert.That(text, Does.Contain("SomeMod"));
        Assert.That(text, Does.Contain("testing"));
        Assert.That(File.Exists(TolerancePath + ".tmp"), Is.False);
    }

    [Test]
    public async Task WriteAsync_CreatesMissingDirectory()
    {
        Directory.Delete(workspace!.Combine(StabilityToleranceFile.DirectoryName), recursive: true);

        var result = await new StabilityToleranceFile().WriteAsync(TolerancePath, "testing");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.Exists(TolerancePath), Is.True);
    }

    [Test]
    public async Task WriteAsync_RejectsUnknownValueWithoutTouchingFile()
    {
        var result = await new StabilityToleranceFile().WriteAsync(TolerancePath, "beta");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("稳定版容差取值无效"));
        Assert.That(File.Exists(TolerancePath), Is.False);
    }

    [Test]
    public void NormalizeTolerance_HandlesCaseEmptyAndUnknown()
    {
        Assert.That(StabilityToleranceFile.NormalizeTolerance(null), Is.EqualTo("stable"));
        Assert.That(StabilityToleranceFile.NormalizeTolerance(string.Empty), Is.EqualTo("stable"));
        Assert.That(StabilityToleranceFile.NormalizeTolerance("TESTING"), Is.EqualTo("testing"));
        Assert.That(StabilityToleranceFile.NormalizeTolerance("development"), Is.EqualTo("development"));
        Assert.That(StabilityToleranceFile.NormalizeTolerance("beta"), Is.EqualTo("stable"));
        Assert.That(StabilityToleranceFile.KnownTolerances, Is.EqualTo(new[] { "stable", "testing", "development" }));
    }
}
