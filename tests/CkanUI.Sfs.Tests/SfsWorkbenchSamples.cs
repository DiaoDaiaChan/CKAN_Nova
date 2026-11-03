using System.Text;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

/// <summary>工作台测试用的生涯存档片段：GAME 根 + 两个 SCENARIO + 一艘带 MODULE 的船 + 一名乘员。</summary>
internal static class SfsWorkbenchSamples
{
    public const string Career =
        "GAME\r\n"
        + "{\r\n"
        + "\tversion = 1.0\r\n"
        + "\tUT = 5005129.6190400701\r\n"
        + "\tSCENARIO\r\n"
        + "\t{\r\n"
        + "\t\tname = Funding\r\n"
        + "\t\tfunds = 1234.5\r\n"
        + "\t}\r\n"
        + "\tSCENARIO\r\n"
        + "\t{\r\n"
        + "\t\tname = ResearchAndDevelopment\r\n"
        + "\t\tsci = 42\r\n"
        + "\t}\r\n"
        + "\tFLIGHTSTATE\r\n"
        + "\t{\r\n"
        + "\t\tvesselCount = 1\r\n"
        + "\t\tVESSEL\r\n"
        + "\t\t{\r\n"
        + "\t\t\tname = Kerbal 1\r\n"
        + "\t\t\tpersistentId = 12345\r\n"
        + "\t\t\tPART\r\n"
        + "\t\t\t{\r\n"
        + "\t\t\t\tname = mk1pod\r\n"
        + "\t\t\t\tamount = 1\r\n"
        + "\t\t\t\tMODULE\r\n"
        + "\t\t\t\t{\r\n"
        + "\t\t\t\t\tname = ModuleCommand\r\n"
        + "\t\t\t\t}\r\n"
        + "\t\t\t}\r\n"
        + "\t\t}\r\n"
        + "\t}\r\n"
        + "\tROSTER\r\n"
        + "\t{\r\n"
        + "\t\tKERBAL\r\n"
        + "\t\t{\r\n"
        + "\t\t\tname = Jebediah Kerman\r\n"
        + "\t\t\ttrait = Pilot\r\n"
        + "\t\t}\r\n"
        + "\t}\r\n"
        + "}\r\n";

    /// <summary>解析样本；解析失败直接让测试炸掉，而不是返回 null 的连锁空引用。</summary>
    public static SfsDocument Parse(string? text = null)
    {
        var parsed = SfsParser.Parse(text ?? Career);
        Assert.That(parsed.Document, Is.Not.Null, "样本必须能解析");
        Assert.That(parsed.Document!.ParseDiagnostics.Any(diagnostic => diagnostic.Severity == SfsSeverity.Error), Is.False);
        return parsed.Document;
    }

    /// <summary>取包含指定片段的第一行行号（1 起算）。</summary>
    public static int LineOf(string text, string token)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].TrimEnd('\r').Contains(token, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        Assert.Fail($"样本里找不到包含 “{token}” 的行");
        return -1;
    }

    /// <summary>把文本变成"带 BOM 的 UTF-8 字节"（模拟 KSP 真实存档）。</summary>
    public static byte[] WithBom(string text)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text))
            .ToArray();
}
