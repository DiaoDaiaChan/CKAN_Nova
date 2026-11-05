using System.Linq;
using CkanUI.Plugin.Ai.Markdown;
using NUnit.Framework;

namespace CkanUI.Plugin.Ai.Tests;

[TestFixture]
public class MarkdownParserTests
{
    [Test]
    public void Parse_ParsesHeadingsAndFormatting()
    {
        var md = "# Title 1\n\nHere is **bold** and *italic* and `code`.";
        var doc = MarkdownParser.Parse(md);

        Assert.That(doc.Blocks, Has.Count.EqualTo(2));
        var h1 = (HeadingBlock)doc.Blocks[0];
        Assert.That(h1.Level, Is.EqualTo(1));
        Assert.That(h1.Inlines.Any(i => i is TextInline t && t.Text == "Title 1"), Is.True);

        var p = (ParagraphBlock)doc.Blocks[1];
        Assert.That(p.Inlines.Any(i => i is BoldInline), Is.True);
        Assert.That(p.Inlines.Any(i => i is ItalicInline), Is.True);
        Assert.That(p.Inlines.Any(i => i is CodeInline c && c.Code == "code"), Is.True);
    }

    [Test]
    public void Parse_ParsesInlineAndBlockMath()
    {
        var md = "The equation is $E = mc^2$ for energy.\n\n$$\n\\Delta v = I_{sp} g_0 \\ln\\frac{m_0}{m_f}\n$$";
        var doc = MarkdownParser.Parse(md);

        Assert.That(doc.Blocks, Has.Count.EqualTo(2));
        var p = (ParagraphBlock)doc.Blocks[0];
        Assert.That(p.Inlines.Any(i => i is MathInline m && m.Formula == "E = mc^2"), Is.True);

        var mathBlock = (MathBlock)doc.Blocks[1];
        Assert.That(mathBlock.Formula, Does.Contain("\\Delta v"));
    }

    [Test]
    public void Parse_ParsesMindmapAndMermaid()
    {
        var md = "```mermaid\nmindmap\n  root((KSP Mods))\n    Engine\n      RealFuels\n    Visual\n      Scatterer\n```";
        var doc = MarkdownParser.Parse(md);

        Assert.That(doc.Blocks, Has.Count.EqualTo(1));
        var mindmap = (MindmapBlock)doc.Blocks[0];
        Assert.That(mindmap.SyntaxType, Is.EqualTo("mindmap"));
        Assert.That(mindmap.RawContent, Does.Contain("RealFuels"));
    }

    [Test]
    public void Parse_ParsesTable()
    {
        var md = "| Mod | Version | Compatible |\n| --- | --- | --- |\n| Scatterer | 0.0838 | Yes |\n| EVE | 1.11 | Yes |";
        var doc = MarkdownParser.Parse(md);

        Assert.That(doc.Blocks, Has.Count.EqualTo(1));
        var table = (TableBlock)doc.Blocks[0];
        Assert.That(table.Headers, Has.Count.EqualTo(3));
        Assert.That(table.Headers[0], Is.EqualTo("Mod"));
        Assert.That(table.Rows, Has.Count.EqualTo(2));
        Assert.That(table.Rows[0][0], Is.EqualTo("Scatterer"));
    }
}