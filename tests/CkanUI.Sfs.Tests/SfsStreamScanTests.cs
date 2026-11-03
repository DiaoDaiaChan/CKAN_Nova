using System.Text;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

/// <summary>
/// 流式扫描 / 按行打补丁的测试：大文件路径必须和完整解析给出同样的计数，
/// 补丁必须逐字节保真。
/// </summary>
[TestFixture]
public sealed class SfsStreamScanTests
{
    private TempWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TempWorkspace("sfs-stream");

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private string Write(string text, string name = "persistent.sfs", bool bom = false)
    {
        var path = _workspace.Combine(name);
        if (bom)
        {
            File.WriteAllBytes(path, SfsWorkbenchSamples.WithBom(text));
        }
        else
        {
            SfsSamples.WriteFile(path, text);
        }

        return path;
    }

    [Test]
    public void Scan_CountsMatchFullParse()
    {
        foreach (var (text, label) in new[]
                 {
                     (SfsWorkbenchSamples.Career, "career"),
                     (SfsSamples.CanonicalCrlf, "canonical"),
                     (SfsSamples.Messy, "messy"),
                 })
        {
            var parsed = SfsParser.Parse(text);
            Assert.That(parsed.Document, Is.Not.Null, label);

            var path = Write(text, label + ".sfs");
            var profile = SfsStreamScanner.Scan(path);

            Assert.Multiple(() =>
            {
                Assert.That(profile.NodeCount, Is.EqualTo(parsed.Document!.NodeCount), label + " 块数");
                Assert.That(profile.PairCount, Is.EqualTo(parsed.Document.PairCount), label + " 键值数");
                Assert.That(profile.LineCount, Is.EqualTo(parsed.Document.LineCount), label + " 行数");
                Assert.That(profile.MaxDepth, Is.EqualTo(parsed.Document.MaxDepth), label + " 深度");
                Assert.That(profile.HasErrors, Is.False, label + " 不应报错");
                Assert.That(profile.RootName, Is.EqualTo("GAME"), label + " 根名");
                Assert.That(profile.ByteLength, Is.EqualTo(new FileInfo(path).Length), label + " 字节数");
            });
        }
    }

    [Test]
    public void Scan_CountsMatchFullParseWithBomAndLf()
    {
        var parsed = SfsParser.Parse(SfsSamples.CanonicalLf);
        var path = Write(SfsSamples.CanonicalLf, "lf.sfs", bom: true);
        var profile = SfsStreamScanner.Scan(path);

        Assert.Multiple(() =>
        {
            Assert.That(profile.LineCount, Is.EqualTo(parsed.Document!.LineCount));
            Assert.That(profile.PairCount, Is.EqualTo(parsed.Document.PairCount));
            Assert.That(profile.NodeCount, Is.EqualTo(parsed.Document.NodeCount));
            Assert.That(profile.ByteLength, Is.EqualTo(new FileInfo(path).Length));
        });
    }

    [Test]
    public void Scan_ListsShallowBlocksWithSubtreeCounts()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var profile = SfsStreamScanner.Scan(path);

        var flight = profile.Blocks.Single(block => block.Name == "FLIGHTSTATE");
        var vessel = profile.Blocks.Single(block => block.Name == "VESSEL");
        var part = profile.Blocks.FirstOrDefault(block => block.Name == "PART");

        Assert.Multiple(() =>
        {
            Assert.That(flight.Path, Is.EqualTo("GAME/FLIGHTSTATE"));
            Assert.That(flight.Depth, Is.EqualTo(1));
            Assert.That(flight.PairCount, Is.EqualTo(6), "vesselCount + VESSEL 子树的 5 个键值");
            Assert.That(flight.NodeCount, Is.EqualTo(3), "VESSEL + PART + MODULE");
            Assert.That(vessel.Depth, Is.EqualTo(2));
            Assert.That(vessel.PairCount, Is.EqualTo(5), "name/persistentId + PART 的 2 个 + MODULE 的 1 个");
            Assert.That(vessel.NodeCount, Is.EqualTo(2), "PART + MODULE");
            Assert.That(part, Is.Null, "默认 BlockDepth=2 不列 PART");
            Assert.That(profile.Blocks[0].Name, Is.EqualTo("GAME"), "按行号排序，父块在前");
            Assert.That(profile.Blocks[0].Line, Is.EqualTo(1));
            Assert.That(profile.Blocks[0].EndLine, Is.EqualTo(SfsWorkbenchSamples.Career.Split('\n').Length - 1));
        });
    }

    [Test]
    public void Scan_CapturesValuableFieldsWithPath()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var profile = SfsStreamScanner.Scan(path);

        var funds = profile.Fields.Single(field => field.Key == "funds" && field.Value == "1234.5");
        var sci = profile.Fields.Single(field => field.Key == "sci");

        Assert.Multiple(() =>
        {
            Assert.That(funds.Path, Is.EqualTo("GAME/SCENARIO"));
            Assert.That(funds.Depth, Is.EqualTo(2));
            Assert.That(funds.Line, Is.EqualTo(SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "funds = 1234.5")));
            Assert.That(sci.Value, Is.EqualTo("42"));
            Assert.That(profile.Fields.Count, Is.EqualTo(13), "全文件 14 个键值；MODULE 里的 name 在第 5 层，超出默认 FieldDepth=4");
            Assert.That(profile.PairCount, Is.EqualTo(14));
        });
    }

    [Test]
    public void Scan_RespectsFieldDepthBudget()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var shallow = SfsStreamScanner.Scan(path, new SfsStreamOptions { FieldDepth = 0 });
        var deep = SfsStreamScanner.Scan(path, new SfsStreamOptions { FieldDepth = 4 });

        Assert.Multiple(() =>
        {
            Assert.That(shallow.Fields, Is.Empty, "FieldDepth=0 只收根层，而 Career 没有根层键值");
            Assert.That(deep.Fields.Any(field => field.Key == "amount"), Is.True);
        });
    }

    [Test]
    public void Scan_FlagsOversizedBlock()
    {
        var builder = new StringBuilder("GAME\r\n{\r\n\tSPEC\r\n\t{\r\n");
        for (var index = 0; index < 200; index++)
        {
            builder.Append("\t\tblob").Append(index).Append(" = ").Append(new string('x', 100)).Append("\r\n");
        }

        builder.Append("\t}\r\n}\r\n");
        var path = Write(builder.ToString(), "big-block.sfs");
        var profile = SfsStreamScanner.Scan(path, new SfsStreamOptions { OversizedBlockBytes = 4096 });

        Assert.Multiple(() =>
        {
            Assert.That(profile.OversizedBlocks.Select(block => block.Name), Is.EquivalentTo(new[] { "SPEC" }));
            Assert.That(profile.OversizedBlocks[0].PairCount, Is.EqualTo(200));
            Assert.That(profile.OversizedBlocks[0].ByteLength, Is.GreaterThan(4096));
            Assert.That(profile.Blocks.Any(block => block.Name == "GAME"), Is.True, "GAME 是根块范围，算普通块");
        });
    }

    [Test]
    public void Scan_IgnoresRootBlockAsOversized()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var profile = SfsStreamScanner.Scan(path, new SfsStreamOptions { OversizedBlockBytes = 16 });
        Assert.That(profile.OversizedBlocks.Any(block => block.Path == "GAME"), Is.False);
    }

    [Test]
    public void Scan_ReportsUnbalancedBraces()
    {
        var path = Write("GAME\r\n{\r\n\tversion = 1.0\r\n", "broken.sfs");
        var profile = SfsStreamScanner.Scan(path);

        Assert.Multiple(() =>
        {
            Assert.That(profile.HasErrors, Is.True);
            Assert.That(profile.Issues, Is.Not.Empty);
            Assert.That(profile.RootName, Is.EqualTo("GAME"));
            Assert.That(profile.PairCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Search_ReturnsLinesWithoutFullParse()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var hits = SfsStreamScanner.Search(path, "mk1pod");

        Assert.Multiple(() =>
        {
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0].Line, Is.EqualTo(SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "mk1pod")));
            Assert.That(hits[0].Key, Is.EqualTo("name"));
            Assert.That(hits[0].Value, Is.EqualTo("mk1pod"));
            Assert.That(hits[0].IsPair, Is.True);
        });
    }

    [Test]
    public void Search_RequiresAllTermsAndHonoursLimit()
    {
        var path = Write(SfsWorkbenchSamples.Career);

        Assert.Multiple(() =>
        {
            Assert.That(SfsStreamScanner.Search(path, "name kerbal 1", 10), Has.Count.EqualTo(1), "同义词全命中");
            Assert.That(SfsStreamScanner.Search(path, "name kerbal 2", 10), Is.Empty, "有一个词不命中就排除");
            Assert.That(SfsStreamScanner.Search(path, "name", 2), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Search_MatchesPlainLinesLikeBraceOrName()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var hits = SfsStreamScanner.Search(path, "FLIGHTSTATE", 10);
        Assert.Multiple(() =>
        {
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0].IsPair, Is.False);
            Assert.That(hits[0].Key, Is.Empty);
            Assert.That(hits[0].Line, Is.EqualTo(SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "FLIGHTSTATE")));
        });
    }

    [Test]
    public void Search_AppliesLabelPredicates()
    {
        var path = Write(SfsWorkbenchSamples.Career);

        Assert.Multiple(() =>
        {
            var byKey = SfsStreamScanner.Search(path, "资金", 10, keyMatch: key => key == "funds");
            Assert.That(byKey, Has.Count.EqualTo(1), "中文标签不在原文里，要靠键谓词命中");
            Assert.That(byKey[0].Key, Is.EqualTo("funds"));
            Assert.That(byKey[0].IsPair, Is.True);

            var byBlock = SfsStreamScanner.Search(path, "飞行器", 10, blockMatch: name => name == "FLIGHTSTATE");
            Assert.That(byBlock, Has.Count.EqualTo(1), "块标签同理，按块名谓词命中");
            Assert.That(byBlock[0].Line, Is.EqualTo(SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "FLIGHTSTATE")));
        });
    }

    [Test]
    public void Describe_ReturnsDirectFieldsAndChildBlocks()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var detail = SfsStreamScanner.Describe(path, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "VESSEL"));

        Assert.That(detail, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(detail!.Path, Is.EqualTo("GAME/FLIGHTSTATE/VESSEL"));
            Assert.That(detail.Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "name", "persistentId" }));
            Assert.That(detail.Nodes.Select(node => node.Name), Is.EquivalentTo(new[] { "PART" }));
            Assert.That(detail.Truncated, Is.False);
            Assert.That(detail.EndLine, Is.GreaterThan(detail.Line));
        });
    }

    [Test]
    public void Describe_HandlesNameOnItsOwnLine()
    {
        const string text = "GAME\r\n{\r\n\tTARGET\r\n\t{\r\n\t\tkey = 1\r\n\t}\r\n}\r\n";
        var path = Write(text, "brace-next-line.sfs");
        var detail = SfsStreamScanner.Describe(path, 4);
        Assert.Multiple(() =>
        {
            Assert.That(detail, Is.Not.Null);
            Assert.That(detail!.Path, Is.EqualTo("GAME/TARGET"));
            Assert.That(detail.Fields.Single().Value, Is.EqualTo("1"));
        });
    }

    [Test]
    public void Describe_ReturnsNullForPairLine()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        Assert.That(SfsStreamScanner.Describe(path, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "funds = 1234.5")), Is.Null);
    }

    [Test]
    public void RequiresStreaming_ComparesAgainstLimit()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        Assert.Multiple(() =>
        {
            Assert.That(SfsStreamScanner.RequiresStreaming(new FileInfo(path).Length), Is.False);
            Assert.That(SfsStreamScanner.RequiresStreaming(SfsStreamScanner.FullParseLimitBytes + 1), Is.True);
            Assert.That(SfsStreamScanner.RequiresStreaming(path), Is.False);
        });
    }

    [Test]
    public void ReplaceValue_OnlyRewritesTargetLine()
    {
        var source = Write(SfsWorkbenchSamples.Career);
        var before = SfsSamples.ReadBytes(source);
        var temp = _workspace.Combine("patched.tmp");
        var line = SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "funds = 1234.5");

        var result = SfsLinePatcher.ReplaceValue(source, line, null, "98765.25", temp);

        Assert.That(result.Succeeded, Is.True, result.Summary);
        var after = SfsSamples.ReadBytes(temp);
        Assert.Multiple(() =>
        {
            Assert.That(result.OldLine, Is.EqualTo("\t\tfunds = 1234.5"), "SCENARIO 在 GAME 里，两层缩进");
            Assert.That(result.NewLine, Is.EqualTo("\t\tfunds = 98765.25"));
            Assert.That(result.TempPath, Is.EqualTo(temp));
            Assert.That(after.Length, Is.EqualTo(before.Length + "98765.25".Length - "1234.5".Length));
            Assert.That(after.Take(3).ToArray(), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "原文件没有 BOM，补丁也不能加");
        });

        // 除目标行外逐字节相同。
        var beforeLines = Encoding.UTF8.GetString(before).Split('\n');
        var afterLines = Encoding.UTF8.GetString(after).Split('\n');
        Assert.That(afterLines, Has.Length.EqualTo(beforeLines.Length));
        for (var index = 0; index < beforeLines.Length; index++)
        {
            if (index == line - 1)
            {
                Assert.That(afterLines[index], Is.EqualTo("\t\tfunds = 98765.25\r"));
                continue;
            }

            Assert.That(afterLines[index], Is.EqualTo(beforeLines[index]), $"第 {index + 1} 行不该变");
        }
    }

    [Test]
    public void ReplaceValue_KeepsBomAndCrLf()
    {
        var source = Write(SfsWorkbenchSamples.Career, bom: true);
        var temp = _workspace.Combine("patched-bom.tmp");
        var result = SfsLinePatcher.ReplaceValue(source, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "sci = 42"), null, "7", temp);

        Assert.That(result.Succeeded, Is.True, result.Summary);
        var bytes = SfsSamples.ReadBytes(temp);
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Take(3).ToArray(), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(Encoding.UTF8.GetString(bytes), Does.Contain("\t\tsci = 7\r\n"));
        });
    }

    [Test]
    public void ReplaceValue_RenamesKeyInPlace()
    {
        var source = Write(SfsWorkbenchSamples.Career);
        var temp = _workspace.Combine("renamed.tmp");
        var result = SfsLinePatcher.ReplaceValue(source, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "funds = 1234.5"), "credits", "1234.5", temp);

        Assert.That(result.Succeeded, Is.True, result.Summary);
        Assert.That(result.NewLine, Is.EqualTo("\t\tcredits = 1234.5"));
    }

    [Test]
    public void ReplaceValue_ReportsNoOpWhenUnchanged()
    {
        var source = Write(SfsWorkbenchSamples.Career);
        var temp = _workspace.Combine("noop.tmp");
        var result = SfsLinePatcher.ReplaceValue(source, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "funds = 1234.5"), null, "1234.5", temp);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.TempPath, Is.Null);
            Assert.That(result.Summary, Does.Contain("相同"));
            Assert.That(File.Exists(temp), Is.False);
        });
    }

    [Test]
    public void ReplaceValue_RejectsBlockLineAndInvalidValue()
    {
        var source = Write(SfsWorkbenchSamples.Career);
        var temp = _workspace.Combine("rejected.tmp");

        var blockLine = SfsLinePatcher.ReplaceValue(source, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "FLIGHTSTATE"), null, "1", temp);
        var braceLine = SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "{");
        var stray = SfsLinePatcher.TryReadLine(source, braceLine, out var braceContent, out _);
        var newline = SfsLinePatcher.ReplaceValue(source, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "funds = 1234.5"), null, "1\n2", temp);

        Assert.Multiple(() =>
        {
            Assert.That(blockLine.Succeeded, Is.False, "块行不是键值对");
            Assert.That(blockLine.Issues, Is.Not.Empty);
            Assert.That(stray, Is.True, "读行本身不分类型");
            Assert.That(braceContent, Is.EqualTo("{"));
            Assert.That(newline.Succeeded, Is.False);
            Assert.That(File.Exists(temp), Is.False);
        });
    }

    [Test]
    public void ReplaceValue_RejectsMissingLine()
    {
        var source = Write(SfsWorkbenchSamples.Career);
        var result = SfsLinePatcher.ReplaceValue(source, 9999, null, "1", _workspace.Combine("nope.tmp"));
        Assert.That(result.Succeeded, Is.False);
        Assert.That(SfsLinePatcher.TryReadLine(source, 9999, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void TryReadLine_ReadsLastLineWithoutNewline()
    {
        var path = Write("GAME\r\n{\r\n\tversion = 1.0\r\n}", "no-trailing-newline.sfs");
        Assert.That(SfsLinePatcher.TryReadLine(path, 4, out var content, out _), Is.True);
        Assert.That(content, Is.EqualTo("}"));
    }

    [Test]
    public void PatchedFileStillParsesAndCountsAreStable()
    {
        var source = Write(SfsWorkbenchSamples.Career);
        var temp = _workspace.Combine("stable.tmp");
        var result = SfsLinePatcher.ReplaceValue(source, SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "funds = 1234.5"), null, "999", temp);
        Assert.That(result.Succeeded, Is.True, result.Summary);

        var parsed = SfsParser.ParseFile(temp);
        Assert.That(parsed.Document, Is.Not.Null);
        var original = SfsParser.Parse(SfsWorkbenchSamples.Career).Document!;
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Document!.NodeCount, Is.EqualTo(original.NodeCount));
            Assert.That(parsed.Document.PairCount, Is.EqualTo(original.PairCount));
            Assert.That(parsed.Document.LineCount, Is.EqualTo(original.LineCount));
            Assert.That(parsed.Document.SourceText, Does.Contain("funds = 999"));
        });
    }

    [Test]
    [Explicit("生成 ~120MB 存档，只在手动跑性能基线时使用。")]
    public void Scan_HundredMegabytes()
    {
        var path = _workspace.Combine("huge.sfs");
        var builder = new StringBuilder(1 << 20);
        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)))
        {
            writer.Write("GAME\r\n{\r\n\tversion = 1.0\r\n\tSPEC\r\n\t{\r\n");
            var payload = new string('a', 900);
            for (var index = 0; index < 120_000; index++)
            {
                builder.Clear();
                builder.Append("\t\tchunk").Append(index).Append(" = ").Append(payload).Append("\r\n");
                writer.Write(builder);
            }

            writer.Write("\t}\r\n}\r\n");
        }

        var size = new FileInfo(path).Length;
        Assert.That(size, Is.GreaterThan(100L * 1024 * 1024));
        Assert.That(SfsStreamScanner.RequiresStreaming(size), Is.True);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var profile = SfsStreamScanner.Scan(path);
        watch.Stop();
        TestContext.Out.WriteLine($"scan {size / 1024 / 1024}MB -> {watch.ElapsedMilliseconds}ms, peak {GC.GetTotalMemory(false) / 1024 / 1024}MB");

        Assert.Multiple(() =>
        {
            Assert.That(profile.PairCount, Is.EqualTo(120_001), "chunk* + version");
            Assert.That(profile.OversizedBlocks.Single().Name, Is.EqualTo("SPEC"));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(60_000));
        });

        var searchWatch = System.Diagnostics.Stopwatch.StartNew();
        var hits = SfsStreamScanner.Search(path, "chunk119999", 5);
        searchWatch.Stop();
        TestContext.Out.WriteLine($"search -> {searchWatch.ElapsedMilliseconds}ms");
        Assert.That(hits, Has.Count.EqualTo(1));

        var detailWatch = System.Diagnostics.Stopwatch.StartNew();
        var detail = SfsStreamScanner.Describe(path, 4);
        detailWatch.Stop();
        TestContext.Out.WriteLine($"describe -> {detailWatch.ElapsedMilliseconds}ms");
        Assert.That(detail, Is.Not.Null);
    }
}