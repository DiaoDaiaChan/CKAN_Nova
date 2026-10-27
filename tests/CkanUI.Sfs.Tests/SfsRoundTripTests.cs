using System.Security.Cryptography;
using System.Text;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

/// <summary>07-testing.md §9.1 的三条不变量：INV-1 往返字节一致、INV-2 幂等 SHA 相等、INV-3 值多重集相等。</summary>
[TestFixture]
public sealed class SfsRoundTripTests
{
    private static string Shape(SfsDocument document)
    {
        var builder = new StringBuilder();

        void Walk(IReadOnlyList<SfsElement> elements, int depth)
        {
            foreach (var element in elements)
            {
                builder.Append(depth).Append(':');
                switch (element)
                {
                    case SfsNode node:
                        builder.Append("node ").Append(node.Name).Append('\n');
                        Walk(node.Children, depth + 1);
                        break;
                    case SfsPair pair:
                        builder.Append("pair ").Append(pair.Key).Append('\n');
                        break;
                    case SfsComment comment:
                        builder.Append("comment ").Append(comment.Text).Append('\n');
                        break;
                    case SfsOpaque opaque:
                        builder.Append("opaque ").Append(opaque.Text).Append('\n');
                        break;
                    default:
                        builder.Append("blank").Append('\n');
                        break;
                }
            }
        }

        Walk(document.Elements, 0);
        return builder.ToString();
    }

    [Test]
    public void Inv1_WriteIsByteIdentical_ForCanonicalCrlf()
    {
        var document = SfsParser.Parse(SfsSamples.CanonicalCrlf).Document!;

        Assert.That(SfsWriter.Write(document), Is.EqualTo(SfsSamples.CanonicalCrlf));
    }

    [Test]
    public void Inv1_WriteIsByteIdentical_ForMessyText()
    {
        var document = SfsParser.Parse(SfsSamples.Messy).Document!;

        Assert.That(SfsWriter.Write(document), Is.EqualTo(SfsSamples.Messy));
    }

    [Test]
    public void Inv1_WriteIsByteIdentical_ForLfText()
    {
        var document = SfsParser.Parse(SfsSamples.CanonicalLf).Document!;

        Assert.That(SfsWriter.Write(document), Is.EqualTo(SfsSamples.CanonicalLf));
    }

    [Test]
    public void Inv1_FileBytesRoundTripExactly()
    {
        using var workspace = new TempWorkspace("sfs-roundtrip-bytes");
        var path = workspace.Combine("savegame.sfs");
        SfsSamples.WriteFile(path, SfsSamples.Messy);

        var document = SfsParser.ParseFile(path).Document!;

        Assert.That(SfsWriter.WriteBytes(document), Is.EqualTo(SfsSamples.ReadBytes(path)));
    }

    [Test]
    public void Inv2_FormatIsIdempotentAtSha256Level()
    {
        var first = SfsFormatter.Format(SfsParser.Parse(SfsSamples.Messy).Document!);
        var second = SfsFormatter.Format(SfsParser.Parse(first.Text).Document!);

        var firstHash = SHA256.HashData(Encoding.UTF8.GetBytes(first.Text));
        var secondHash = SHA256.HashData(Encoding.UTF8.GetBytes(second.Text));

        Assert.That(secondHash, Is.EqualTo(firstHash));
    }

    [Test]
    public void Inv3_ValueMultisetSurvivesFormat()
    {
        var document = SfsParser.Parse(SfsSamples.Messy).Document!;
        var formatted = SfsFormatter.Format(document);
        var reparsed = SfsParser.Parse(formatted.Text).Document!;

        Assert.That(SfsValueIndex.Build(reparsed), Is.EqualTo(SfsValueIndex.Build(document)));
    }

    [Test]
    public void Inv3_UtSeedAndModVersionsStayLiteral()
    {
        var modVersions = "Scale_Redist=3.3.2|ModuleManager=4.2.3";
        var source = "GAME\r\n"
            + "{\r\n"
            + "\tUT = 5005129.6190400701\r\n"
            + "\tSeed = 123456789\r\n"
            + "\t_modVersions = " + modVersions + "\r\n"
            + "}\r\n";

        var formatted = SfsFormatter.Format(SfsParser.Parse(source).Document!);
        var after = SfsParser.Parse(formatted.Text).Document!;

        Assert.That(after.Root!.FindPair("UT")!.Value, Is.EqualTo("5005129.6190400701"));
        Assert.That(after.Root.FindPair("Seed")!.Value, Is.EqualTo("123456789"));
        Assert.That(after.Root.FindPair("_modVersions")!.Value, Is.EqualTo(modVersions));
    }

    [Test]
    public void ParseFormatParse_KeepsStructuralShape()
    {
        var source = "GAME\r\n"
            + "{\r\n"
            + "\tSCENARIO\r\n"
            + "\t{\r\n"
            + "\t\tname = A\r\n"
            + "\t\tSUB\r\n"
            + "\t\t{\r\n"
            + "\t\t\tx = 1\r\n"
            + "\t\t}\r\n"
            + "\t}\r\n"
            + "\tSCENARIO\r\n"
            + "\t{\r\n"
            + "\t\tname = B\r\n"
            + "\t}\r\n"
            + "}\r\n";

        var document = SfsParser.Parse(source).Document!;
        var formatted = SfsFormatter.Format(document);
        var reparsed = SfsParser.Parse(formatted.Text).Document!;

        Assert.That(Shape(reparsed), Is.EqualTo(Shape(document)));
        Assert.That(reparsed.NodeCount, Is.EqualTo(document.NodeCount));
        Assert.That(reparsed.PairCount, Is.EqualTo(document.PairCount));
        Assert.That(reparsed.MaxDepth, Is.EqualTo(document.MaxDepth));
    }

    [Test]
    public void RepeatedSiblingNodes_AreNotMerged()
    {
        var source = "GAME\r\n"
            + "{\r\n"
            + "\tSCENARIO\r\n"
            + "\t{\r\n"
            + "\t\tname = A\r\n"
            + "\t}\r\n"
            + "\tSCENARIO\r\n"
            + "\t{\r\n"
            + "\t\tname = B\r\n"
            + "\t}\r\n"
            + "}\r\n";

        var document = SfsParser.Parse(source).Document!;
        var scenarios = document.Root!.Children.OfType<SfsNode>().Where(node => node.Name == "SCENARIO").ToArray();

        Assert.That(scenarios, Has.Length.EqualTo(2));
        Assert.That(scenarios[0].FindPair("name")!.Value, Is.EqualTo("A"));
        Assert.That(scenarios[1].FindPair("name")!.Value, Is.EqualTo("B"));
        Assert.That(SfsValueIndex.Build(document).Count, Is.EqualTo(2));
    }

    [Test]
    public void FormatThenWrite_IsStableAcrossThreeGenerations()
    {
        var text = SfsSamples.Messy;
        for (var generation = 0; generation < 3; generation++)
        {
            var document = SfsParser.Parse(text).Document!;
            var formatted = SfsFormatter.Format(document);

            Assert.That(formatted.Refused, Is.False);
            Assert.That(SfsWriter.Write(SfsParser.Parse(formatted.Text).Document!), Is.EqualTo(formatted.Text));

            text = formatted.Text;
        }
    }
}
