using System.Text;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsFormatterTests
{
    private static SfsFormatResult Format(string text, SfsFormatOptions? options = null)
        => SfsFormatter.Format(SfsParser.Parse(text).Document!, options);

    [Test]
    public void Format_CanonicalSample_IsByteIdentical()
    {
        var result = Format(SfsSamples.CanonicalCrlf);

        Assert.That(result.Refused, Is.False);
        Assert.That(result.Changed, Is.False);
        Assert.That(result.Text, Is.EqualTo(SfsSamples.CanonicalCrlf));
    }

    [Test]
    public void Format_MessyInput_BecomesCanonical()
    {
        var source = "GAME\r\n"
            + "{\r\n"
            + "    version=1.0\r\n"
            + "  UT   =   5005129.6190400701\r\n"
            + "    SCENARIO\r\n"
            + "    {\r\n"
            + "        name = Default\r\n"
            + "    }\r\n"
            + "}\r\n";

        var result = Format(source);

        Assert.That(result.Refused, Is.False);
        Assert.That(result.Changed, Is.True);
        Assert.That(result.Text, Is.EqualTo(SfsSamples.CanonicalCrlf));
        Assert.That(result.Stats.ReindentedLines, Is.GreaterThan(0));
    }

    [Test]
    public void Format_IsIdempotent()
    {
        var first = Format(SfsSamples.Messy);
        var second = Format(first.Text);
        var third = Format(second.Text);

        Assert.That(first.Refused, Is.False);
        Assert.That(second.Text, Is.EqualTo(first.Text));
        Assert.That(third.Text, Is.EqualTo(first.Text));
        Assert.That(second.Changed, Is.False);
        Assert.That(third.Changed, Is.False);
    }

    [Test]
    public void Format_MessySample_NormalisesEveryLine()
    {
        var result = Format(SfsSamples.Messy);

        Assert.That(result.Text, Does.Not.Contain("    "), "不应再有 4 空格缩进");
        Assert.That(result.Text, Does.Contain("\tversion = 1.0\r\n"));
        Assert.That(result.Text, Does.Contain("\tUT = 5005129.6190400701\r\n"));
        Assert.That(result.Text, Does.Contain("\tmodded = True\r\n"));
        Assert.That(result.Text, Does.Contain("\tSCENARIO\r\n\t{\r\n\t\tname = Default\r\n"));
        Assert.That(result.Stats.NodeHeadersSplit, Is.EqualTo(1));
    }

    [Test]
    public void Format_SplitsSameLineBraceOntoItsOwnLine()
    {
        var source = "GAME\n{\n\tNODE {\n\t\tx = 1\n\t}\n}\n";

        var result = Format(source);

        Assert.That(result.Text, Is.EqualTo("GAME\n{\n\tNODE\n\t{\n\t\tx = 1\n\t}\n}\n"));
        Assert.That(result.Stats.NodeHeadersSplit, Is.EqualTo(1));
    }

    [Test]
    public void Format_EmptyValue_KeepsTheSpaceAfterEquals()
    {
        var source = "GAME\r\n{\r\n\tlinkURL =\r\n}\r\n";

        var result = Format(source);

        Assert.That(result.Text, Is.EqualTo("GAME\r\n{\r\n\tlinkURL = \r\n}\r\n"));
    }

    [Test]
    public void Format_SpaceIndentation_UsesRequestedWidth()
    {
        var options = new SfsFormatOptions { Indent = SfsIndentMode.Spaces, IndentWidth = 4 };

        var result = Format(SfsSamples.CanonicalCrlf, options);

        Assert.That(result.Text, Does.Contain("    version = 1.0"));
        Assert.That(result.Text, Does.Contain("        name = Default"));
        Assert.That(result.Text, Does.Not.Contain("\t"));
    }

    [Test]
    public void Format_ForcedLf_RemovesAllCarriageReturns()
    {
        var result = Format(SfsSamples.CanonicalCrlf, new SfsFormatOptions { NewLine = SfsNewLineMode.Lf });

        Assert.That(result.Text, Does.Not.Contain("\r"));
        Assert.That(result.Text, Is.EqualTo(SfsSamples.CanonicalLf));
    }

    [Test]
    public void Format_ForcedCrLf_OnLfInput()
    {
        var result = Format(SfsSamples.CanonicalLf, new SfsFormatOptions { NewLine = SfsNewLineMode.CrLf });

        Assert.That(result.Text, Is.EqualTo(SfsSamples.CanonicalCrlf));
    }

    [Test]
    public void Format_BlankLineModes_KeepCollapseStrip()
    {
        var source = "GAME\r\n{\r\n\tx = 1\r\n\r\n\r\n\ty = 2\r\n}\r\n";

        var keep = Format(source);
        var collapse = Format(source, new SfsFormatOptions { BlankLines = SfsBlankLineMode.Collapse });
        var strip = Format(source, new SfsFormatOptions { BlankLines = SfsBlankLineMode.Strip });

        Assert.That(keep.Text, Is.EqualTo(source));
        Assert.That(collapse.Text, Is.EqualTo("GAME\r\n{\r\n\tx = 1\r\n\r\n\ty = 2\r\n}\r\n"));
        Assert.That(strip.Text, Is.EqualTo("GAME\r\n{\r\n\tx = 1\r\n\ty = 2\r\n}\r\n"));
    }

    [Test]
    public void Format_PreservesLongAndNumericLiteralsVerbatim()
    {
        var modVersions = "Scale_Redist=3.3.2|ModuleManager=4.2.3";
        var source = "GAME\r\n{\r\n\tUT = 5005129.6190400701\r\n\tSeed = 123456789\r\n\t_modVersions = " + modVersions + "\r\n}\r\n";

        var document = SfsParser.Parse(source).Document!;
        var result = SfsFormatter.Format(document);

        Assert.That(document.Root!.FindPair("UT")!.Value, Is.EqualTo("5005129.6190400701"));
        Assert.That(document.Root.FindPair("_modVersions")!.Value, Is.EqualTo(modVersions));
        Assert.That(result.Text, Is.EqualTo(source));
    }

    [Test]
    public void Format_RefusesBrokenFileAndReturnsSourceText()
    {
        var source = "GAME\n{\n\tx = 1\n";

        var result = Format(source);

        Assert.That(result.Refused, Is.True);
        Assert.That(result.RefusalReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Text, Is.EqualTo(source));
    }

    [Test]
    public void Format_KeepsValueMultisetUnchanged()
    {
        var source = "GAME\r\n"
            + "{\r\n"
            + "\tdup = 1\r\n"
            + "\tdup = 2\r\n"
            + "\tname = \"Jeb \\\"Kerman\\\"\"\r\n"
            + "\turl = GameData\\ModuleManager.TechTree\r\n"
            + "// 注释\r\n"
            + "\tSCENARIO {\r\n"
            + "\t\tname = Default\r\n"
            + "\t}\r\n"
            + "}\r\n";

        var document = SfsParser.Parse(source).Document!;
        var formatted = SfsFormatter.Format(document);
        var reparsed = SfsParser.Parse(formatted.Text).Document!;

        Assert.That(formatted.Refused, Is.False);
        Assert.That(formatted.SelfCheckPassed, Is.True);
        Assert.That(SfsValueIndex.Build(reparsed), Is.EqualTo(SfsValueIndex.Build(document)));
    }

    [Test]
    public void Format_PreservesByteOrderMark()
    {
        var body = new UTF8Encoding(false).GetBytes("GAME\r\n{\r\n}\r\n");
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);

        var document = SfsParser.ParseBytes(bytes).Document!;
        var result = SfsFormatter.Format(document);

        Assert.That(result.Refused, Is.False);
        Assert.That(result.Changed, Is.False);
        Assert.That(result.Text[0], Is.EqualTo('\uFEFF'));
        Assert.That(SfsWriter.WriteBytes(document), Is.EqualTo(bytes));
    }

    [Test]
    public void Format_KeepsCommentsAndReportsThem()
    {
        var source = "GAME\r\n{\r\n\t// a\r\n\t# b\r\n\tx = 1\r\n}\r\n";

        var result = Format(source);

        Assert.That(result.Text, Does.Contain("\t// a\r\n"));
        Assert.That(result.Text, Does.Contain("\t# b\r\n"));
        Assert.That(result.Stats.CommentsPreserved, Is.EqualTo(2));
    }

    [Test]
    public void FormatText_ConvenienceOverload_MatchesDocumentOverload()
    {
        var fromText = SfsFormatter.FormatText(SfsSamples.Messy, sourcePath: "savegame.sfs");
        var fromDocument = SfsFormatter.Format(SfsParser.Parse(SfsSamples.Messy).Document!);

        Assert.That(fromText.Text, Is.EqualTo(fromDocument.Text));
    }

    [Test]
    public void Format_IndentedBlankLineLosesItsWhitespace()
    {
        var source = "GAME\r\n{\r\n\t\r\n\tx = 1\r\n}\r\n";

        var result = Format(source);

        Assert.That(result.Text, Is.EqualTo("GAME\r\n{\r\n\r\n\tx = 1\r\n}\r\n"));
    }

    [Test]
    public void Format_OpaqueLines_AreKeptAndCounted()
    {
        var source = "GAME\r\n{\r\n\tbogus\r\n\tx = 1\r\n}\r\n";

        var result = Format(source);

        Assert.That(result.Refused, Is.True, "V-03 是错误，格式化器必须拒绝改写");
    }
}
