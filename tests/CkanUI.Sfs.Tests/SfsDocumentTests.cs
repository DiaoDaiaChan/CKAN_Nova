using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsDocumentTests
{
    private const string Sample = "GAME\n{\n\tname = 日本\n\tSCENARIO\n\t{\n\t\tname = Default\n\t}\n}\n";

    [Test]
    public void Document_CountsLinesAndNodesAndPairs()
    {
        var document = SfsParser.Parse(Sample).Document!;

        Assert.That(document.LineCount, Is.EqualTo(8));
        Assert.That(document.NodeCount, Is.EqualTo(2));
        Assert.That(document.PairCount, Is.EqualTo(2));
        Assert.That(document.MaxDepth, Is.EqualTo(2));
    }

    [Test]
    public void Document_GetLineContent_ReturnsRawLineWithoutTerminator()
    {
        var document = SfsParser.Parse(Sample).Document!;

        Assert.That(document.GetLineContent(1), Is.EqualTo("GAME"));
        Assert.That(document.GetLineContent(3), Is.EqualTo("\tname = 日本"));
    }

    [Test]
    public void Document_LineOffsets_AreCharacterBasedAndByteBased()
    {
        var document = SfsParser.Parse(Sample).Document!;

        Assert.That(document.GetLineCharOffset(1), Is.EqualTo(0));
        Assert.That(document.GetLineCharOffset(4), Is.EqualTo(18));
        Assert.That(document.GetLineByteOffset(4), Is.EqualTo(22));
    }

    [Test]
    public void Document_Root_IsFirstTopLevelNode()
    {
        var document = SfsParser.Parse("// c\nGAME\n{\n}\n").Document!;

        Assert.That(document.Root, Is.Not.Null);
        Assert.That(document.Root!.Name, Is.EqualTo("GAME"));
        Assert.That(document.Root.Depth, Is.EqualTo(0));
    }

    [Test]
    public void Document_SourceTextAndPath_AreExposed()
    {
        var result = SfsParser.Parse(Sample, "in-memory.sfs");

        Assert.That(result.Document!.SourceText, Is.EqualTo(Sample));
        Assert.That(result.Document.SourcePath, Is.EqualTo("in-memory.sfs"));
        Assert.That(result.Document.IsEmpty, Is.False);
        Assert.That(result.Document.IsTextLossy, Is.False);
        Assert.That(result.Document.HasBom, Is.False);
    }

    [Test]
    public void Document_WhitespaceOnlyText_IsEmpty()
    {
        var blank = SfsParser.Parse("\r\n   \r\n").Document!;

        Assert.That(blank.IsEmpty, Is.False, "只有空行，但没有节点");
        Assert.That(blank.Root, Is.Null);
        Assert.That(blank.NodeCount, Is.EqualTo(0));
        Assert.That(blank.Elements, Is.Not.Empty);

        var empty = SfsParser.Parse(string.Empty).Document!;
        Assert.That(empty.IsEmpty, Is.True);
    }

    [Test]
    public void Document_ParseDiagnostics_AreSortedByLine()
    {
        var result = SfsParser.Parse("GAME\n{\n\tbogus\n\talsoBogus\n}\n}\n");

        var lines = result.Diagnostics.Select(diagnostic => diagnostic.Line).ToArray();

        Assert.That(lines, Is.Ordered);
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Code), Does.Contain(SfsDiagnosticCodes.OpaqueLine));
        Assert.That(result.Document!.ParseDiagnostics, Is.EquivalentTo(result.Diagnostics));
    }
}
