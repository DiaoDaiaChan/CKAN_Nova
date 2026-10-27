using System.Diagnostics;
using System.Text;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsParserTests
{
    [Test]
    public void Parse_SimpleDocument_BuildsTree()
    {
        var result = SfsParser.Parse("GAME\n{\n\tversion = 1.0\n}\n");

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Succeeded, Is.True);

        var document = result.Document!;
        Assert.That(document.Root, Is.Not.Null);
        Assert.That(document.Root!.Name, Is.EqualTo("GAME"));
        Assert.That(document.NodeCount, Is.EqualTo(1));
        Assert.That(document.PairCount, Is.EqualTo(1));
        Assert.That(document.Root.Children, Has.Count.EqualTo(1));
        Assert.That(document.Root.FindPair("version")!.Value, Is.EqualTo("1.0"));
    }

    [Test]
    public void Parse_PreservesKeyOrder()
    {
        var document = SfsParser.Parse("GAME\n{\n\tb = 2\n\ta = 1\n\tc = 3\n}\n").Document!;

        var keys = document.Root!.Pairs().Select(pair => pair.Key).ToArray();

        Assert.That(keys, Is.EqualTo(new[] { "b", "a", "c" }));
    }

    [Test]
    public void Parse_HundredLevelNesting_DoesNotOverflow()
    {
        var text = SfsSamples.Nested(100, "\n");

        var result = SfsParser.Parse(text);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Document!.MaxDepth, Is.GreaterThanOrEqualTo(99));
        Assert.That(result.Document.Root!.Name, Is.EqualTo("N0"));
        Assert.That(result.Document.NodeCount, Is.EqualTo(100));
    }

    [Test]
    public void Parse_QuotedAndEscapedValues_AreKeptLiteral()
    {
        var document = SfsParser.Parse("GAME\n{\n\tname = \"Jeb \\\"Kerman\\\"\"\n\tsingle = 'x'\n}\n").Document!;

        var name = document.Root!.FindPair("name")!;
        Assert.That(name.Value, Is.EqualTo("\"Jeb \\\"Kerman\\\"\""));
        Assert.That(name.ValueStyle, Is.EqualTo(SfsValueStyle.DoubleQuoted));

        var single = document.Root.FindPair("single")!;
        Assert.That(single.Value, Is.EqualTo("'x'"));
        Assert.That(single.ValueStyle, Is.EqualTo(SfsValueStyle.SingleQuoted));
    }

    [Test]
    public void Parse_BackslashValue_IsNotAnEscapeSequence()
    {
        var document = SfsParser.Parse("GAME\n{\n\tTechTreeUrl = GameData\\ModuleManager.TechTree\n}\n").Document!;

        Assert.That(
            document.Root!.FindPair("TechTreeUrl")!.Value,
            Is.EqualTo("GameData\\ModuleManager.TechTree"));
    }

    [Test]
    public void Parse_ValueContainingEqualsBraceAndBackslash_StaysOneValue()
    {
        var text = "GAME\n{\n\tmods = A=B|C=D\n\tbraces = {not a block}\n\turl = GameData\\ModuleManager.TechTree\n}\n";

        var document = SfsParser.Parse(text).Document!;

        Assert.That(document.Root!.FindPair("mods")!.Value, Is.EqualTo("A=B|C=D"));
        Assert.That(document.Root.FindPair("braces")!.Value, Is.EqualTo("{not a block}"));
        Assert.That(document.Root.FindPair("url")!.Value, Is.EqualTo("GameData\\ModuleManager.TechTree"));
        Assert.That(SfsWriter.Write(document), Is.EqualTo(text));
    }

    [Test]
    public void Parse_DuplicateKeys_AreNotMerged()
    {
        var document = SfsParser.Parse("GAME\n{\n\tx = 1\n\tx = 2\n}\n").Document!;

        var pairs = document.Root!.Pairs().Where(pair => pair.Key == "x").ToArray();

        Assert.That(pairs, Has.Length.EqualTo(2));
        Assert.That(pairs[0].Value, Is.EqualTo("1"));
        Assert.That(pairs[1].Value, Is.EqualTo("2"));
        Assert.That(document.PairCount, Is.EqualTo(2));
    }

    [Test]
    public void Parse_Comments_ArePreservedAtTheirDepth()
    {
        var document = SfsParser.Parse("// header\nGAME\n{\n\t# inner\n\tx = 1\n}\n").Document!;

        var topLevelComments = document.Elements.OfType<SfsComment>().ToArray();
        Assert.That(topLevelComments, Has.Length.EqualTo(1));
        Assert.That(topLevelComments[0].Text, Does.Contain("header"));

        var innerComments = document.Root!.Children.OfType<SfsComment>().ToArray();
        Assert.That(innerComments, Has.Length.EqualTo(1));
        Assert.That(innerComments[0].Depth, Is.EqualTo(1));
    }

    [Test]
    public void Parse_EmptyBlock_HasNoChildrenButHasCloseLine()
    {
        var document = SfsParser.Parse("GAME\n{\n\tEMPTY\n\t{\n\t}\n}\n").Document!;

        var empty = document.Root!.Children.OfType<SfsNode>().Single();
        Assert.That(empty.Name, Is.EqualTo("EMPTY"));
        Assert.That(empty.Children, Is.Empty);
        Assert.That(empty.CloseLine, Is.Not.Null);
        Assert.That(empty.BraceLine, Is.Not.Null);
    }

    [Test]
    public void Parse_SameLineBrace_IsUnderstoodAsNodeHeader()
    {
        var document = SfsParser.Parse("GAME\n{\n\tNODE {\n\t\tx = 1\n\t}\n}\n").Document!;

        var node = document.Root!.Children.OfType<SfsNode>().Single();
        Assert.That(node.Name, Is.EqualTo("NODE"));
        Assert.That(node.BraceLine, Is.Null);
        Assert.That(node.LineNumber, Is.EqualTo(3));
        Assert.That(node.FindPair("x")!.Value, Is.EqualTo("1"));
    }

    [Test]
    public void Parse_CrlfAndLf_AreBothRecognised()
    {
        var crlf = SfsParser.Parse("GAME\r\n{\r\n}\r\n").Document!;
        Assert.That(crlf.NewLines.Crlf, Is.EqualTo(3));
        Assert.That(crlf.NewLines.Lf, Is.EqualTo(0));
        Assert.That(crlf.NewLines.IsMixed, Is.False);
        Assert.That(crlf.LineCount, Is.EqualTo(3));

        var lf = SfsParser.Parse("GAME\n{\n}\n").Document!;
        Assert.That(lf.NewLines.Lf, Is.EqualTo(3));
        Assert.That(lf.NewLines.Crlf, Is.EqualTo(0));
        Assert.That(lf.NewLines.IsMixed, Is.False);
    }

    [Test]
    public void Parse_MixedLineEndings_AreReported()
    {
        var document = SfsParser.Parse("GAME\r\n{\n\tx = 1\r\n}\n").Document!;

        Assert.That(document.NewLines.IsMixed, Is.True);
        Assert.That(document.DeviantLineNumbers, Is.Not.Empty);
    }

    [Test]
    public void Parse_NonAsciiValues_AreKept()
    {
        var document = SfsParser.Parse("GAME\n{\n\tname = 日本語 Ünïcode 🚀\n}\n").Document!;

        Assert.That(document.Root!.FindPair("name")!.Value, Is.EqualTo("日本語 Ünïcode 🚀"));
    }

    [Test]
    public void Parse_MissingClosingBrace_ReportsV01WithLineNumber()
    {
        var result = SfsParser.Parse("GAME\n{\n\tx = 1\n");

        Assert.That(result.HasErrors, Is.True);

        var diagnostic = result.Diagnostics.First(d => d.Code == SfsDiagnosticCodes.UnbalancedBraces);
        Assert.That(diagnostic.Severity, Is.EqualTo(SfsSeverity.Error));
        Assert.That(diagnostic.Line, Is.EqualTo(1));
        Assert.That(diagnostic.Message, Is.Not.Empty);
    }

    [Test]
    public void Parse_StrayClosingBrace_ReportsV01AndKeepsOpaqueLine()
    {
        var result = SfsParser.Parse("GAME\n{\n}\n}\n");

        Assert.That(result.Diagnostics.Any(d => d.Code == SfsDiagnosticCodes.UnbalancedBraces), Is.True);
        Assert.That(result.Document!.Elements.OfType<SfsOpaque>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Parse_BareNameWithoutBrace_ReportsV03AndKeepsOpaqueLine()
    {
        var result = SfsParser.Parse("GAME\n{\n\tbogus\n}\n");

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == SfsDiagnosticCodes.OpaqueLine);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Line, Is.EqualTo(3));
        Assert.That(diagnostic.Column, Is.GreaterThan(0));
        Assert.That(result.Document!.Root!.Children.OfType<SfsOpaque>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ParseBytes_InvalidUtf8_ReportsV05AndFlagsLossy()
    {
        var bytes = new byte[] { 0x47, 0x41, 0x4D, 0x45, 0x0A, 0xFF, 0xFE, 0x0A };

        var result = SfsParser.ParseBytes(bytes);

        Assert.That(result.Document, Is.Not.Null);
        Assert.That(result.Document!.IsTextLossy, Is.True);
        Assert.That(result.Diagnostics.Any(d => d.Code == SfsDiagnosticCodes.EncodingOrLineEnding), Is.True);
    }

    [Test]
    public void ParseBytes_ByteOrderMark_IsStrippedFromSourceText()
    {
        var body = new UTF8Encoding(false).GetBytes("GAME\r\n{\r\n}\r\n");
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);

        var document = SfsParser.ParseBytes(bytes).Document!;

        Assert.That(document.HasBom, Is.True);
        Assert.That(document.SourceText.StartsWith("GAME", StringComparison.Ordinal), Is.True);
        Assert.That(document.Root!.Name, Is.EqualTo("GAME"));
    }

    [Test]
    public void Parse_LastLineWithoutTerminator_RoundTrips()
    {
        var text = "GAME\n{\n\tx = 1\n}";

        var document = SfsParser.Parse(text).Document!;

        Assert.That(SfsWriter.Write(document), Is.EqualTo(text));
    }

    [Test]
    public void Parse_EmptyText_IsEmptyDocument()
    {
        var document = SfsParser.Parse(string.Empty).Document!;

        Assert.That(document.IsEmpty, Is.True);
        Assert.That(document.Root, Is.Null);
        Assert.That(document.Elements, Is.Empty);
    }

    [Test]
    public void ParseFile_ReadsFromDiskAndRecordsPath()
    {
        using var workspace = new TempWorkspace("sfs-parse-file");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.CanonicalCrlf);

        var document = SfsParser.ParseFile(path).Document!;

        Assert.That(document.SourcePath, Is.EqualTo(path));
        Assert.That(document.Root!.Name, Is.EqualTo("GAME"));
        Assert.That(document.NewLines.Crlf, Is.EqualTo(9));
    }

    [Test]
    public void Parse_LargeFile_IsLinearNotQuadratic()
    {
        var builder = new StringBuilder();
        builder.Append("GAME\r\n{\r\n");
        var index = 0;
        while (builder.Length < 5 * 1024 * 1024)
        {
            builder.Append("\tkey").Append(index).Append(" = value").Append(index).Append("\r\n");
            index++;
        }

        builder.Append("}\r\n");
        var text = builder.ToString();
        Assert.That(text.Length, Is.GreaterThan(5 * 1024 * 1024));

        var stopwatch = Stopwatch.StartNew();
        var document = SfsParser.Parse(text).Document!;
        var roundTrip = SfsWriter.Write(document);
        stopwatch.Stop();

        Assert.That(document.PairCount, Is.EqualTo(index));
        Assert.That(roundTrip, Is.EqualTo(text));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)), "5MB 解析+写回应远快于 20 秒（防 O(n²)）");
    }
}
