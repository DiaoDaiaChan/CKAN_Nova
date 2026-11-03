using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Saves;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Plugin.Saves.Tests;

/// <summary>
/// CRLF 存档的回归：Windows 上的 .sfs 全是 CRLF，原文命中的行号要是真实行号。
/// 曾经的写法按 CR 与 LF 两个字符切行，CRLF 因此每行多出一个空元素，行号整体翻倍（20 → 39），
/// 于是"搜索命中的行"和"文档里的行"对不上，点命中打不开正确的块。
/// </summary>
[TestFixture]
public sealed class SfsCrlfRegressionTests
{
    [Test]
    public async Task 原文命中的行号在CRLF存档里仍然是真实行号()
    {
        using var workspace = new TempWorkspace("sfs-crlf-search");
        var path = workspace.Combine("persistent.sfs");
        await File.WriteAllTextAsync(path, CrlfSaveText);

        var expectedLine = File.ReadAllLines(path)
            .Select((line, index) => (line, number: index + 1))
            .First(pair => pair.line.Trim() == "name = nestedFoo")
            .number;

        var result = await new SaveEditorService().SearchAsync(path, "nestedFoo");
        Assert.That(result.IsSuccess, Is.True, result.Message);

        var rawHit = result.Value!
            .FirstOrDefault(match => match.Match == "原文" && match.DisplayValue.Contains("nestedFoo", System.StringComparison.Ordinal));
        Assert.That(rawHit, Is.Not.Null, "CRLF 存档里也要能搜到原文行");
        Assert.That(rawHit!.Line, Is.EqualTo(expectedLine), "CRLF 不能让行号翻倍");
    }

    /// <summary>和产品里真实存档一样的 CRLF 文本。</summary>
    private const string CrlfSaveText =
        "GAME\r\n" +
        "{\r\n" +
        "\tversion = 1.12.5\r\n" +
        "\tFLIGHTSTATE\r\n" +
        "\t{\r\n" +
        "\t\tVESSEL\r\n" +
        "\t\t{\r\n" +
        "\t\t\tname = ship\r\n" +
        "\t\t}\r\n" +
        "\t}\r\n" +
        "\tSCENARIO\r\n" +
        "\t{\r\n" +
        "\t\tMODULE\r\n" +
        "\t\t{\r\n" +
        "\t\t\tname = nestedFoo\r\n" +
        "\t\t}\r\n" +
        "\t}\r\n" +
        "}\r\n";
}
