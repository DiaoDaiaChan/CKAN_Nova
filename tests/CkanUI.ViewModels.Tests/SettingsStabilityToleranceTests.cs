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

    [Test]
    public async Task GetModToleranceAsync_MissingFile_ReturnsNull()
    {
        var result = await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetModToleranceAsync_ExistingFile_ReturnsOverride()
    {
        await File.WriteAllTextAsync(
            TolerancePath,
            """""{"overall_stability_tolerance":"stable","mod_stability_tolerance":{"ModA":"testing","ModB":"development"}}""""");

        var modA = await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA");
        var modB = await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModB");
        var modC = await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModC");

        Assert.That(modA, Is.EqualTo("testing"));
        Assert.That(modB, Is.EqualTo("development"));
        Assert.That(modC, Is.Null);
    }

    [Test]
    public void GetModToleranceSync_ReturnsSameAsAsync()
    {
        File.WriteAllText(
            TolerancePath,
            """""{"overall_stability_tolerance":"stable","mod_stability_tolerance":{"ModA":"testing"}}""""");

        var modA = StabilityToleranceFile.GetModToleranceSync(workspace!.Path, "ModA");
        var modB = StabilityToleranceFile.GetModToleranceSync(workspace!.Path, "ModB");

        Assert.That(modA, Is.EqualTo("testing"));
        Assert.That(modB, Is.Null);
    }

    [Test]
    public async Task SetModToleranceAsync_AddsAndUpdatesOverride()
    {
        var result1 = await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", "testing");
        Assert.That(result1.IsSuccess, Is.True);
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA"), Is.EqualTo("testing"));

        // 更新同一个 mod
        var result2 = await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", "development");
        Assert.That(result2.IsSuccess, Is.True);
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA"), Is.EqualTo("development"));

        // 添加第二个 mod，不影响第一个
        var result3 = await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModB", "stable");
        Assert.That(result3.IsSuccess, Is.True);
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA"), Is.EqualTo("development"));
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModB"), Is.EqualTo("stable"));
    }

    [Test]
    public async Task SetModToleranceAsync_NullOrEmptyTolerance_RemovesOverride()
    {
        await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", "testing");
        await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModB", "development");

        // 移除 ModA
        var removeResult = await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", null);
        Assert.That(removeResult.IsSuccess, Is.True);
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA"), Is.Null);
        // ModB 依然保留
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModB"), Is.EqualTo("development"));

        // 传入空字符串也视为移除或无效，测试移除传 null
    }

    [Test]
    public async Task SetModToleranceAsync_InvalidTolerance_ReturnsErrorWithoutTouchingFile()
    {
        await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", "testing");

        var invalidResult = await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", "invalid_val");
        Assert.That(invalidResult.IsSuccess, Is.False);
        Assert.That(invalidResult.Message, Does.Contain("无效"));

        // 原有配置不受影响
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA"), Is.EqualTo("testing"));
    }

    [Test]
    public async Task SetModToleranceAsync_PreservesOverallTolerance()
    {
        await File.WriteAllTextAsync(
            TolerancePath,
            """""{"overall_stability_tolerance":"development","mod_stability_tolerance":{}}""""");

        await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", "testing");

        var readOverall = await new StabilityToleranceFile().ReadAsync(TolerancePath);
        Assert.That(readOverall.Value, Is.EqualTo("development"));
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA"), Is.EqualTo("testing"));
    }

    [Test]
    public async Task SaveOverallToleranceAsync_PreservesModOverrides()
    {
        await StabilityToleranceFile.SetModToleranceAsync(workspace!.Path, "ModA", "testing");

        var saveResult = await StabilityToleranceFile.SaveOverallToleranceAsync(workspace!.Path, "development");
        Assert.That(saveResult.IsSuccess, Is.True);

        var readOverall = await new StabilityToleranceFile().ReadAsync(TolerancePath);
        Assert.That(readOverall.Value, Is.EqualTo("development"));
        Assert.That(await StabilityToleranceFile.GetModToleranceAsync(workspace!.Path, "ModA"), Is.EqualTo("testing"));
    }
}
