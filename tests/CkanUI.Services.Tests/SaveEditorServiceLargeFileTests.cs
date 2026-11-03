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
/// 大文件模式（流式扫描 + 行级补丁）：阈值注入得很小，用同一份小样本覆盖
/// “不建语法树也能给大纲 / 搜索 / 展开 / 改值”的契约，避免测试仓库里塞百 MB 文件。
/// </summary>
[TestFixture]
public sealed class SaveEditorServiceLargeFileTests
{
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

    private TempWorkspace _workspace = null!;
    private SaveEditorService _editor = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("save-editor-large");
        // 100 字节阈值：样本一定走流式分支。
        _editor = new SaveEditorService(fullParseLimitBytes: 100);
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
    public async Task LoadOutline_UsesStreamingProjection()
    {
        var path = WriteSample();

        var result = await _editor.LoadOutlineAsync(path);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var outline = result.Value!;
        Assert.That(outline.LargeFileMode, Is.True);
        Assert.That(outline.LargeFileNote, Does.Contain("流式"));
        Assert.That(outline.Mode, Is.EqualTo("CAREER"));
        Assert.That(outline.Funds, Is.EqualTo("√1234.5"));
        Assert.That(outline.VesselCount, Is.EqualTo(1));
        Assert.That(outline.PartCount, Is.EqualTo(1));
        Assert.That(outline.Sections.First().Title, Is.EqualTo("存档信息"));
        Assert.That(outline.Sections.Any(s => s.Id == "scenario:0:Funding"), Is.True);
        Assert.That(outline.Sections.Single(s => s.Id == "scenario:0:Funding").Fields.Any(f => f.Key == "funds"), Is.True);
    }

    [Test]
    public async Task LoadOutline_SmallFile_StaysInFullParseMode()
    {
        var path = WriteSample();
        var full = new SaveEditorService();

        var result = await full.LoadOutlineAsync(path);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.LargeFileMode, Is.False);
        Assert.That(result.Value!.LargeFileNote, Is.Empty);
    }

    [Test]
    public async Task Search_FindsPairByRawText()
    {
        var path = WriteSample();

        var result = await _editor.SearchAsync(path, "12345");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var hit = result.Value!.FirstOrDefault(m => m.Line == 19);
        Assert.That(hit, Is.Not.Null, "深层键值应能按原文命中");
        Assert.That(hit!.Value, Does.Contain("12345"));
        Assert.That(hit.Match, Is.EqualTo("键值"));
    }

    [Test]
    public async Task Search_MatchesChineseKeyLabel()
    {
        var path = WriteSample();

        var result = await _editor.SearchAsync(path, "资金");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var hit = result.Value!.FirstOrDefault(m => m.Line == 9);
        Assert.That(hit, Is.Not.Null, "流式搜索也要能按词表标签命中 funds");
        Assert.That(hit!.Key, Is.EqualTo("funds"));
        Assert.That(hit.Value, Does.Contain("1234.5"));
    }

    [Test]
    public async Task Search_MatchesChineseBlockLabel()
    {
        var path = WriteSample();

        var result = await _editor.SearchAsync(path, "飞行器");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var hit = result.Value!.FirstOrDefault(m => m.Line == 11);
        Assert.That(hit, Is.Not.Null, "块名走词表标签：飞行器 → FLIGHTSTATE");
        Assert.That(hit!.Label, Is.EqualTo("块"));
    }

    [Test]
    public async Task Describe_ReturnsDirectFieldsAndChildBlocks()
    {
        var path = WriteSample();

        var result = await _editor.DescribeNodeAsync(path, 11);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var detail = result.Value!;
        Assert.That(detail.Name, Is.EqualTo("FLIGHTSTATE"));
        Assert.That(detail.Title, Is.EqualTo("飞行器"));
        Assert.That(detail.Nodes.Any(n => n.Name == "VESSEL"), Is.True);
        Assert.That(detail.Fields, Is.Empty, "FLIGHTSTATE 下没有直接键值");
    }

    [Test]
    public async Task SetValue_WritesOnlyTargetLineAndKeepsBackup()
    {
        var path = WriteSample();

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.SetValue,
            Line = 9,
            Value = "9999.5",
            Write = true,
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var outcome = result.Value!;
        Assert.That(outcome.Succeeded, Is.True, string.Join(" / ", outcome.Issues));
        Assert.That(outcome.Changed, Is.True);
        Assert.That(outcome.Verified, Is.True);
        Assert.That(outcome.Written, Is.True);
        Assert.That(outcome.BackupPath, Is.Not.Null.And.Matches(@"pre-edit-"));

        var lines = File.ReadAllLines(path);
        Assert.That(lines[8], Is.EqualTo("\t\tfunds = 9999.5"));
        Assert.That(lines.Length, Is.EqualTo(Sample.Split("\r\n").Length - 1));
        Assert.That(Directory.GetFiles(_workspace.Path, "*.patch-*"), Is.Empty, "临时补丁文件必须清理");
    }

    [Test]
    public async Task SetValue_WithoutWrite_LeavesFileUntouched()
    {
        var path = WriteSample();
        var before = File.ReadAllText(path);

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.SetValue,
            Line = 9,
            Value = "42",
            Write = false,
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Succeeded, Is.True);
        Assert.That(result.Value!.Written, Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(_workspace.Path, "*.patch-*"), Is.Empty);
    }

    [Test]
    public async Task DeleteElement_IsRejectedInLargeFileMode()
    {
        var path = WriteSample();

        var result = await _editor.EditAsync(new SaveEditRequest
        {
            FilePath = path,
            Kind = SaveEditKind.DeleteElement,
            Line = 11,
            Write = true,
        });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("大文件"));
    }
}