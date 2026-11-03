using System.Text;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsEditorTests
{
    private const string Career = SfsWorkbenchSamples.Career;

    [Test]
    public void SetValue_OnlyRewritesTargetLine()
    {
        var document = SfsWorkbenchSamples.Parse();
        var line = SfsWorkbenchSamples.LineOf(Career, "funds");

        var result = SfsEditor.SetValue(document, line, "777.25");

        Assert.That(result.Succeeded, Is.True, result.Summary);
        Assert.That(result.Text, Does.Contain("funds = 777.25"));
        AssertSameLinesExcept(Career, result.Text, line);
        var edited = SfsParser.Parse(result.Text).Document!;
        Assert.That(SfsEditor.FindPair(edited, line)!.Value, Is.EqualTo("777.25"));
    }

    [Test]
    public void SetValue_KeepsValueThatContainsEquals()
    {
        var document = SfsWorkbenchSamples.Parse();
        var line = SfsWorkbenchSamples.LineOf(Career, "name = Kerbal 1");

        var result = SfsEditor.SetValue(document, line, "a = b c");

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Issues));
        Assert.That(SfsParser.Parse(result.Text).Document!.Root!.Children
            .OfType<SfsNode>().Single(node => node.Name == "FLIGHTSTATE").Children
            .OfType<SfsNode>().Single().FindPair("name")!.Value, Is.EqualTo("a = b c"));
    }

    [Test]
    public void SetValue_RejectsNewline()
    {
        var document = SfsWorkbenchSamples.Parse();
        var result = SfsEditor.SetValue(document, SfsWorkbenchSamples.LineOf(Career, "funds"), "1\n2");

        Assert.That(result.Changed, Is.False);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Issues, Is.Not.Empty);
        Assert.That(result.Text, Is.EqualTo(Career));
    }

    [Test]
    public void SetValue_OnBlockLine_IsRejected()
    {
        var document = SfsWorkbenchSamples.Parse();
        var result = SfsEditor.SetValue(document, SfsWorkbenchSamples.LineOf(Career, "VESSEL"), "1");

        Assert.That(result.Changed, Is.False);
        Assert.That(result.Issues.Single(), Does.Contain("不是键值对"));
    }

    [Test]
    public void RenameKey_ChangesOnlyTheKey()
    {
        var document = SfsWorkbenchSamples.Parse();
        var line = SfsWorkbenchSamples.LineOf(Career, "version = 1.0");

        var result = SfsEditor.RenameKey(document, line, "saveVersion");

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Issues));
        Assert.That(result.Text, Does.Contain("saveVersion = 1.0"));
        Assert.That(result.Text, Does.Not.Contain("\tversion = 1.0"));
    }

    [Test]
    public void DeleteElement_RemovesWholeSubtree()
    {
        var document = SfsWorkbenchSamples.Parse();
        var moduleLine = SfsWorkbenchSamples.LineOf(Career, "MODULE");
        var expectedNodes = document.NodeCount;
        var expectedPairs = document.PairCount;

        var result = SfsEditor.DeleteElement(document, moduleLine);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Issues));
        var edited = SfsParser.Parse(result.Text).Document!;
        Assert.That(edited.NodeCount, Is.EqualTo(expectedNodes - 1));
        Assert.That(edited.PairCount, Is.EqualTo(expectedPairs - 1));
        Assert.That(result.Text, Does.Not.Contain("ModuleCommand"));
    }

    [Test]
    public void DeleteElement_OnComment_IsRejected()
    {
        const string text = "GAME\r\n{\r\n\t// 注释\r\n}\r\n";
        var document = SfsWorkbenchSamples.Parse(text);

        var result = SfsEditor.DeleteElement(document, 3);

        Assert.That(result.Changed, Is.False);
        Assert.That(result.Issues.Single(), Does.Contain("没有可删除的元素"));
    }

    [Test]
    public void DuplicateElement_ClonesPartWithItsModule()
    {
        var document = SfsWorkbenchSamples.Parse();
        var partLine = SfsWorkbenchSamples.LineOf(Career, "PART");

        var result = SfsEditor.DuplicateElement(document, partLine);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Issues));
        var edited = SfsParser.Parse(result.Text).Document!;
        Assert.That(edited.NodeCount, Is.EqualTo(document.NodeCount + 2), "PART 自己 + MODULE");
        Assert.That(edited.PairCount, Is.EqualTo(document.PairCount + 3));
        Assert.That(SfsOutline.Build(edited).Snapshot.PartCount, Is.EqualTo(2));
    }

    [Test]
    public void DuplicateElement_ClonesWholeVessel()
    {
        var document = SfsWorkbenchSamples.Parse();
        var vesselLine = SfsWorkbenchSamples.LineOf(Career, "VESSEL");

        var result = SfsEditor.DuplicateElement(document, vesselLine);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Issues));
        var edited = SfsParser.Parse(result.Text).Document!;
        Assert.That(edited.NodeCount, Is.EqualTo(document.NodeCount + 3));
        Assert.That(SfsOutline.Build(edited).Snapshot.VesselCount, Is.EqualTo(2));
    }

    [Test]
    public void InsertPair_AppendsIndentedFieldToBlock()
    {
        var document = SfsWorkbenchSamples.Parse();
        var flightLine = SfsWorkbenchSamples.LineOf(Career, "FLIGHTSTATE");

        var result = SfsEditor.InsertPair(document, flightLine, "moddedFlag", "1");

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Issues));
        Assert.That(result.Text, Does.Contain("\r\n\t\tmoddedFlag = 1\r\n"));
        var edited = SfsParser.Parse(result.Text).Document!;
        Assert.That(edited.PairCount, Is.EqualTo(document.PairCount + 1));
        Assert.That(edited.NodeCount, Is.EqualTo(document.NodeCount));
    }

    [Test]
    public void InsertPair_RejectsInvalidKey()
    {
        var document = SfsWorkbenchSamples.Parse();
        var flightLine = SfsWorkbenchSamples.LineOf(Career, "FLIGHTSTATE");

        var result = SfsEditor.InsertPair(document, flightLine, "bad key", "1");

        Assert.That(result.Changed, Is.False);
        Assert.That(result.Issues.Single(), Does.Contain("键名"));
    }

    [Test]
    public void InsertPair_OnPairLine_IsRejected()
    {
        var document = SfsWorkbenchSamples.Parse();
        var line = SfsWorkbenchSamples.LineOf(Career, "vesselCount");

        var result = SfsEditor.InsertPair(document, line, "x", "1");

        Assert.That(result.Changed, Is.False);
        Assert.That(result.Issues.Single(), Does.Contain("不是块节点"));
    }

    [Test]
    public void SetValue_PreservesBomAndCrlf()
    {
        var bytes = SfsWorkbenchSamples.WithBom(Career);
        var parsed = SfsParser.ParseBytes(bytes);
        var document = parsed.Document!;
        Assert.That(document.HasBom, Is.True);

        var result = SfsEditor.SetValue(document, SfsWorkbenchSamples.LineOf(Career, "sci"), "99");

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Issues));
        Assert.That(result.Text[0], Is.EqualTo('\uFEFF'));
        var noCrlf = result.Text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        Assert.That(noCrlf, Does.Not.Contain("\n"));
        Assert.That(noCrlf, Does.Not.Contain("\r"));

        var reread = SfsParser.ParseBytes(new UTF8Encoding(false).GetBytes(result.Text)).Document!;
        Assert.That(reread.HasBom, Is.True);
        Assert.That(SfsOutline.Build(reread).Snapshot.Science!.Value, Is.EqualTo("99"));
    }

    private static void AssertSameLinesExcept(string before, string after, int lineNumber)
    {
        var left = before.Split('\n');
        var right = after.Split('\n');
        Assert.That(right.Length, Is.EqualTo(left.Length));
        for (var index = 0; index < left.Length; index++)
        {
            if (index + 1 == lineNumber)
            {
                continue;
            }

            Assert.That(right[index], Is.EqualTo(left[index]), $"第 {index + 1} 行不该变");
        }
    }
}
