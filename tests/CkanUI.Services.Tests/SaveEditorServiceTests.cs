using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Saves;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 存档编辑服务：语义投影、搜索、结构化写入。用真实临时文件跑，
/// 因为这里要验证的正是“写盘只改该改的那几行 + 写前留快照”。
/// </summary>
[TestFixture]
public sealed class SaveEditorServiceTests
{
    private TempWorkspace _workspace = null!;
    private SaveEditorService _editor = null!;

    private const string Sample =
        "GAME\r\n" +
        "{\r\n" +
        "\tversion = 1.12.5\r\n" +
        "\tTitle = 我的生涯\r\n" +
        "\tMode = CAREER\r\n" +
        "\tSCENARIO\r\n" +
        "\t{\r\n" +
        "\t\tname = Funding\r\n" +
        "\t\tfunds = 1234.5\r\n" +
        "\t}\r\n" +
        "\tFLIGHTSTATE\r\n" +
        "\t{\r\n" +
        "\t\tVESSEL\r\n" +
        "\t\t{\r\n" +
        "\t\t\tname = 一号船\r\n" +
        "\t\t\tPART\r\n" +
        "\t\t\t{\r\n" +
        "\t\t\t\tname = mk1pod\r\n" +
        "\t\t\t\tmid = 12345\r\n" +
        "\t\t\t}\r\n" +
        "\t\t}\r\n" +
        "\t}\r\n" +
        "}\r\n";

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("save-editor");
        _editor = new SaveEditorService();
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private string WriteSample(string name = "persistent.sfs")
    {
        var path = _workspace.Combine(name);
        File.WriteAllText(path, Sample);
        return path;
    }

    [Test]
    public async Task LoadOutline_ProjectsCommonContentFirst()
    {
        var path = WriteSample();

        var result = await _editor.LoadOutlineAsync(path);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var outline = result.Value!;
        Assert.That(outline.Mode, Is.EqualTo("CAREER"));
        Assert.That(outline.Funds, Is.EqualTo("√1234.5"));
        Assert.That(outline.VesselCount, Is.EqualTo(1));
        Assert.That(outline.PartCount, Is.EqualTo(1));
        Assert.That(outline.Sections.First().Title, Is.EqualTo("存档信息"));
        Assert.That(outline.Highlights.Any(f => f.Key == "funds"), Is.True);
    }

    [Test]
    public async Task Search_FindsByChineseLabelAndDeepRawLine()
    {
        var path = WriteSample();

        var byLabel = await _editor.SearchAsync(path, "资金");
        var byDeepValue = await _editor.SearchAsync(path, "12345");

        Assert.That(byLabel.IsSuccess, Is.True);
        Assert.That(byLabel.Value!.Any(m => m.Key == "funds"), Is.True, "中文标签应命中 funds");
        Assert.That(byDeepValue.Value!.Any(m => m.Line == 19 || m.DisplayValue.Contains("mid")), Is.True, "深层键值应能按原文命中");
    }

    [Test]
    public async Task Search_CutsHighlightAroundTheMatchedToken()
    {
        var path = WriteSample();

        var result = await _editor.SearchAsync(path, "1234");

        Assert.That(result.IsSuccess, Is.True);
        var hit = result.Value!.First(m => m.Line == 9);
        Assert.That(hit.HighlightHit, Is.EqualTo("1234"));
        Assert.That(hit.HighlightPrefix, Is.EqualTo("√"));
        Assert.That(hit.HighlightPrefix + hit.HighlightHit + hit.HighlightSuffix, Is.EqualTo(hit.DisplayValue));
    }

    [Test]
    public async Task Edit_RenameKey_ChangesOnlyKeyName()
    {
        var path = WriteSample();

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.RenameKey,
            Line = 9,
            Key = "fundsFixed",
            Write = true,
        });

        Assert.That(result.Value!.Succeeded, Is.True, result.Value.Summary);
        var text = File.ReadAllText(path);
        Assert.That(text, Does.Contain("fundsFixed = 1234.5"));
        Assert.That(text, Does.Contain("version = 1.12.5"));
    }

    [Test]
    public async Task Edit_SetValue_WritesOnlyTargetLineAndLeavesSnapshot()
    {
        var path = WriteSample();
        var before = File.ReadAllText(path).Split("\r\n");

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.SetValue,
            Line = 9,
            Value = "99999",
            Write = true,
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Succeeded, Is.True);
        Assert.That(result.Value.Written, Is.True);
        Assert.That(result.Value.BackupPath, Is.Not.Null);
        Assert.That(File.Exists(result.Value.BackupPath!), Is.True, "写盘前必须留快照");

        var after = File.ReadAllText(path).Split("\r\n");
        Assert.That(after.Length, Is.EqualTo(before.Length));
        var changed = before.Zip(after).Count(pair => pair.First != pair.Second);
        Assert.That(changed, Is.EqualTo(1));
        Assert.That(after[8], Is.EqualTo("\t\tfunds = 99999"));
    }

    [Test]
    public async Task Edit_WithoutWrite_ReturnsCandidateAndKeepsFileUntouched()
    {
        var path = WriteSample();
        var before = File.ReadAllText(path);

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.SetValue,
            Line = 9,
            Value = "1",
            Write = false,
        });

        Assert.That(result.Value!.Succeeded, Is.True);
        Assert.That(result.Value.Written, Is.False);
        Assert.That(result.Value.CandidateText, Does.Contain("funds = 1"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
    }

    [Test]
    public async Task Edit_DeleteBlock_RemovesWholeVesselAndKeepsRest()
    {
        var path = WriteSample();

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.DeleteElement,
            Line = 13,
            Write = true,
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Succeeded, Is.True, result.Value.Summary);
        var text = File.ReadAllText(path);
        Assert.That(text, Does.Not.Contain("一号船"));
        Assert.That(text, Does.Not.Contain("mk1pod"), "删的是 VESSEL 整块，里面的 PART 一起走");
        Assert.That(text, Does.Contain("FLIGHTSTATE"), "外层块必须留着，别把容器一起删了");
        Assert.That(text, Does.Contain("funds = 1234.5"));
    }

    [Test]
    public async Task Edit_RejectsValueWithNewlineWithoutWriting()
    {
        var path = WriteSample();
        var before = File.ReadAllText(path);

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.SetValue,
            Line = 9,
            Value = "1\n2",
            Write = true,
        });

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.False);
        Assert.That(result.Value.Written, Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
    }
}
