using System.Text;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// CFG-01 往返保真（15 篇 §9 T-CFG-01/02/03）：解析再序列化必须与源字节完全一致，
/// 改值只允许动目标行。这里所有断言都比"磁盘上读到的字节"，不假设行尾/编码，
/// 因为 `.gitattributes` 会把 `.cfg` 转成 CRLF。
/// </summary>
[TestFixture]
public sealed class ConfigNodeDocumentTests
{
    /// <summary>fixture 全集。</summary>
    public static IReadOnlyList<string> FixtureFileNames() => ModConfigFixtures.EnumerateCfgFileNames();

    [TestCaseSource(nameof(FixtureFileNames))]
    public void RenderBytes_IsByteIdenticalToSource(string fileName)
    {
        var bytes = ModConfigFixtures.ReadBytes(fileName);

        var document = ConfigNodeDocument.ParseBytes(bytes, fileName);

        Assert.That(document.RenderBytes(), Is.EqualTo(bytes), $"{fileName} 的往返结果必须逐字节一致");
        Assert.That(document.IsModified, Is.False, "没改任何值就不该有改动");
    }

    [TestCaseSource(nameof(FixtureFileNames))]
    public void Parse_TwiceIsStable(string fileName)
    {
        var bytes = ModConfigFixtures.ReadBytes(fileName);

        var first = ConfigNodeDocument.ParseBytes(bytes, fileName);
        var second = ConfigNodeDocument.ParseBytes(first.RenderBytes(), fileName);

        Assert.That(second.RenderBytes(), Is.EqualTo(bytes), "二次解析后仍应逐字节一致（T-CFG-02 幂等）");
        Assert.That(second.Pairs.Count, Is.EqualTo(first.Pairs.Count), "键值对数量不该在往返中变化");
    }

    [TestCaseSource(nameof(FixtureFileNames))]
    public void ParsingEachFixture_KeepsTextLossyFalse(string fileName)
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes(fileName), fileName);

        Assert.That(document.IsTextLossy, Is.False, "fixture 都是合法 UTF-8");
    }

    [TestCaseSource(nameof(FixtureFileNames))]
    public void EveryPairLine_ResolvesBackToItsOwnKey(string fileName)
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes(fileName), fileName);

        foreach (var pair in document.Pairs)
        {
            var content = document.GetLineContent(pair.LineNumber);
            Assert.That(content, Does.Contain(pair.Key), $"{fileName}:{pair.LineNumber} 行内容应含键「{pair.Key}」");
        }
    }

    [Test]
    public void Render_KeepsUtf8Bom()
    {
        var payload = Encoding.UTF8.GetBytes("KSP_COMMUNITY_FIXES\n{\n  a = 1\n}\n");
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. payload];

        var document = ConfigNodeDocument.ParseBytes(bytes);

        Assert.That(document.HasBom, Is.True);
        Assert.That(document.RenderBytes(), Is.EqualTo(bytes));
    }

    [Test]
    public void Render_KeepsCrlfLineEndings()
    {
        const string text = "root\r\n{\r\n  a = 1\r\n}\r\n";
        var document = ConfigNodeDocument.Parse(text);

        Assert.That(document.NewLines.Crlf, Is.EqualTo(4));
        Assert.That(document.Render(), Is.EqualTo(text));
    }

    [Test]
    public void Render_KeepsLoneCrLineEndings()
    {
        const string text = "a = 1\ra = 2\r";
        var document = ConfigNodeDocument.Parse(text);

        Assert.That(document.NewLines.Cr, Is.EqualTo(2));
        Assert.That(document.Render(), Is.EqualTo(text));
    }

    [Test]
    public void Render_KeepsMissingFinalNewline()
    {
        const string text = "a = 1\nb = 2";
        var document = ConfigNodeDocument.Parse(text);

        Assert.That(document.Render(), Is.EqualTo(text));
    }

    [Test]
    public void Render_HandlesEmptyFile()
    {
        var document = ConfigNodeDocument.Parse(string.Empty);

        Assert.That(document.IsEmpty, Is.True);
        Assert.That(document.LineCount, Is.Zero);
        Assert.That(document.Render(), Is.Empty);
    }

    [Test]
    public void Render_KeepsMixedLineEndings()
    {
        const string text = "root\n{\r\n  a = 1\r  b = 2\n}\n";
        var document = ConfigNodeDocument.Parse(text);

        Assert.That(document.NewLines.IsMixed, Is.True);
        Assert.That(document.Render(), Is.EqualTo(text));
    }

    [Test]
    public void SetValue_ChangesOnlyTheTargetLine()
    {
        var bytes = ModConfigFixtures.ReadBytes("memgraph-settings.cfg");
        var original = Encoding.UTF8.GetString(bytes);
        var document = ConfigNodeDocument.ParseBytes(bytes, "memgraph-settings.cfg");
        var pair = document.FindPair("keyScaleUp");

        Assert.That(pair, Is.Not.Null);
        Assert.That(document.TrySetValue(pair!.LineNumber, "Keypad8", out var error), Is.True, error);

        var changed = DiffLineNumbers(original, document.Render());

        Assert.That(changed, Is.EqualTo(new[] { pair.LineNumber }), "只允许目标行变化（T-CFG-01）");
        Assert.That(document.Render(), Does.Contain("keyScaleUp = Keypad8"));
    }

    [Test]
    public void SetValue_ThreeFields_ChangesExactlyThreeLines()
    {
        var bytes = ModConfigFixtures.ReadBytes("memgraph-settings.cfg");
        var original = Encoding.UTF8.GetString(bytes);
        var document = ConfigNodeDocument.ParseBytes(bytes, "memgraph-settings.cfg");

        Assert.That(document.TrySetValue("visible", "True", null, out var e1), Is.True, e1);
        Assert.That(document.TrySetValue("enableLogging", "True", null, out var e2), Is.True, e2);
        Assert.That(document.TrySetValue("keyMark", "Insert", null, out var e3), Is.True, e3);

        var changed = DiffLineNumbers(original, document.Render());

        Assert.That(changed.Count, Is.EqualTo(3), "改 3 项只能变 3 行，其余逐字节不变");
        Assert.That(document.Pairs.Count, Is.EqualTo(10), "其余 7 项仍在");
    }

    [Test]
    public void SetValue_SameValue_LeavesDocumentUnmodified()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("memgraph-settings.cfg"));
        var pair = document.FindPair("applyPadding");

        Assert.That(document.TrySetValue(pair!.LineNumber, pair.Value, out var error), Is.True, error);

        Assert.That(document.IsModified, Is.False, "写回同样的值不应产生改动（T-CFG-02 幂等）");
        Assert.That(document.RenderBytes(), Is.EqualTo(ModConfigFixtures.ReadBytes("memgraph-settings.cfg")));
    }

    [Test]
    public void SetValue_KeepsIndentAndSeparator()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("kspcf-settings.cfg"));
        var pair = document.FindPair("ApplyKrakenFix");

        Assert.That(pair, Is.Not.Null);
        Assert.That(document.TrySetValue(pair!.LineNumber, "False", out var error), Is.True, error);

        var line = document.GetLineContent(pair.LineNumber);
        Assert.That(document.Render(), Does.Contain("  ApplyKrakenFix = False"), "2 空格缩进与 \" = \" 分隔符都要原样");
        Assert.That(line, Does.StartWith("  "));
    }

    [Test]
    public void SetValue_KeepsTrailingComment()
    {
        const string text = "root\n{\n  a = 1 // 手写的行尾注释\n}\n";
        var document = ConfigNodeDocument.Parse(text);
        var pair = document.FindPair("a");

        Assert.That(document.TrySetValue(pair!.LineNumber, "2", out var error), Is.True, error);

        Assert.That(document.Render(), Does.Contain("a = 2 // 手写的行尾注释"), "行尾注释属于原文，不许被值替换吃掉");
    }

    [Test]
    public void SetValue_QuotesValueContainingSpaces()
    {
        var document = ConfigNodeDocument.Parse("root\n{\n  title = old\n}\n");
        var pair = document.FindPair("title");

        Assert.That(document.TrySetValue(pair!.LineNumber, "Hello World", out var error), Is.True, error);
        Assert.That(document.Render(), Does.Contain("title = \"Hello World\""));
    }

    [Test]
    public void SetValue_RejectsValueWithNewline()
    {
        var document = ConfigNodeDocument.Parse("root\n{\n  a = 1\n}\n");
        var pair = document.FindPair("a");

        Assert.That(document.TrySetValue(pair!.LineNumber, "1\n  b = 2", out var error), Is.False);
        Assert.That(error, Does.Contain("换行"));
        Assert.That(document.IsModified, Is.False);
    }

    [Test]
    public void SetValue_RejectsLineThatIsNotAPair()
    {
        var document = ConfigNodeDocument.Parse("root\n{\n  // 注释\n  a = 1\n}\n");

        Assert.That(document.TrySetValue(3, "x", out var error), Is.False);
        Assert.That(error, Does.Contain("不是键值对"));
    }

    [Test]
    public void SetValue_RejectsKeyThatDoesNotExist()
    {
        var document = ConfigNodeDocument.Parse("root\n{\n  a = 1\n}\n");

        Assert.That(document.TrySetValue("missing", "1", null, out var error), Is.False);
        Assert.That(error, Does.Contain("missing"));
    }

    [Test]
    public void SetValue_RejectsUnquotableValue()
    {
        var document = ConfigNodeDocument.Parse("root\n{\n  a = 1\n}\n");
        var pair = document.FindPair("a");

        Assert.That(document.TrySetValue(pair!.LineNumber, "a b \"c\"", out var error), Is.False);
        Assert.That(error, Does.Contain("双引号"));
    }

    [Test]
    public void Revert_RestoresOriginalBytes()
    {
        var bytes = ModConfigFixtures.ReadBytes("kspcf-settings.cfg");
        var document = ConfigNodeDocument.ParseBytes(bytes);
        var pair = document.FindPair("VerboseLogging");

        Assert.That(document.TrySetValue(pair!.LineNumber, "True", out var error), Is.True, error);
        Assert.That(document.IsModified, Is.True);

        document.Revert();

        Assert.That(document.IsModified, Is.False);
        Assert.That(document.RenderBytes(), Is.EqualTo(bytes));
    }

    [Test]
    public void Pairs_CarryNodePathForNestedNodes()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("kspcf-settings-nested.cfg"));

        Assert.That(document.FindPair("WarmupThreads")!.NodePath, Is.EqualTo("KSP_COMMUNITY_FIXES/Experimental"));
        Assert.That(document.FindPair("VerboseLogging")!.NodePath, Is.EqualTo("KSP_COMMUNITY_FIXES"));
        Assert.That(document.RootNodeName, Is.EqualTo("KSP_COMMUNITY_FIXES"));
    }

    [Test]
    public void Pairs_FlatLayoutHasEmptyNodePath()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("memgraph-settings.cfg"));

        Assert.That(document.Pairs.Count, Is.EqualTo(10));
        Assert.That(document.RootNodeName, Is.Null, "flat 形态没有根节点");
        foreach (var pair in document.Pairs)
        {
            Assert.That(pair.NodePath, Is.Empty);
        }
    }

    [Test]
    public void Comment_IsAttachedToTheKeyBelow()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("kspcf-settings.cfg"));
        var pair = document.FindPair("ApplyKrakenFix");

        Assert.That(pair!.Comment, Is.Not.Null);
        Assert.That(pair.Comment, Does.Contain("Kraken"));
        Assert.That(pair.Comment, Does.Contain("Type: bool"), "多行注释要合并成一块");
        Assert.That(pair.Comment, Does.Not.Contain("//"), "注释符要被剥掉，展示用");
    }

    [Test]
    public void Comment_IsNullWhenNoCommentAbove()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("memgraph-settings.cfg"));

        Assert.That(document.FindPair("visible")!.Comment, Is.Null);
    }

    [Test]
    public void CustomSyntax_ReportsParseErrorButStillRoundTrips()
    {
        var bytes = ModConfigFixtures.ReadBytes("memgraph-padheap.cfg");

        var document = ConfigNodeDocument.ParseBytes(bytes, "memgraph-padheap.cfg");

        Assert.That(document.HasParseErrors, Is.True, "私有语法「size: count」无法按 ConfigNode 分词，必须报错而不是假装成功");
        Assert.That(document.RenderBytes(), Is.EqualTo(bytes), "即便解析报错，原文也必须能原样写回");
        Assert.That(document.Pairs, Is.Empty);
    }

    [Test]
    public void MmPatch_IsParsedButKeepsOriginalBytes()
    {
        var bytes = ModConfigFixtures.ReadBytes("mm-patch-sample.cfg");

        var document = ConfigNodeDocument.ParseBytes(bytes, "mm-patch-sample.cfg");

        Assert.That(document.RenderBytes(), Is.EqualTo(bytes));
        Assert.That(document.LineCount, Is.GreaterThan(10));
    }

    [Test]
    public void DuplicateKeys_AreBothKept()
    {
        const string text = "root\n{\n  a = 1\n  a = 2\n}\n";
        var document = ConfigNodeDocument.Parse(text);

        Assert.That(document.Pairs.Count, Is.EqualTo(2), "重复键按 L5 原样保留，不合并");
        Assert.That(document.Render(), Is.EqualTo(text));
    }

    [Test]
    public void OddLines_ArePreservedAsOpaque()
    {
        const string text = "root\n{\n  a = 1\n  这不是键值对也没有花括号\n}\n";
        var document = ConfigNodeDocument.Parse(text);

        Assert.That(document.HasParseErrors, Is.True, "无法分词的行要报 V-03，绝不静默丢弃");
        Assert.That(document.Render(), Is.EqualTo(text));
    }

    [Test]
    public void SetValue_ThenReparse_IsSemanticallyStable()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("kspcf-settings.cfg"));
        var target = document.FindPair("MaxPatchedConicsCount");

        Assert.That(document.TrySetValue(target!.LineNumber, "16", out var error), Is.True, error);

        var reparsed = ConfigNodeDocument.Parse(document.Render());

        Assert.That(reparsed.HasParseErrors, Is.False, "改完必须仍是合法 ConfigNode");
        Assert.That(reparsed.Pairs.Count, Is.EqualTo(document.Pairs.Count));
        Assert.That(reparsed.FindPair("MaxPatchedConicsCount")!.Value, Is.EqualTo("16"));
    }

    [Test]
    public void TrySetValue_WithNodePath_TargetsTheRightNode()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("kspcf-settings-nested.cfg"));

        Assert.That(
            document.TrySetValue("WarmupThreads", "4", "KSP_COMMUNITY_FIXES/Experimental", out var error),
            Is.True,
            error);
        Assert.That(document.Render(), Does.Contain("WarmupThreads = 4"));
    }

    [Test]
    public void TrySetValue_WithWrongNodePath_Fails()
    {
        var document = ConfigNodeDocument.ParseBytes(ModConfigFixtures.ReadBytes("kspcf-settings-nested.cfg"));

        Assert.That(document.TrySetValue("WarmupThreads", "4", "KSP_COMMUNITY_FIXES", out var error), Is.False);
        Assert.That(error, Does.Contain("WarmupThreads"));
    }

    [Test]
    public void LossyUtf8_IsFlaggedAndNotSilentlyRewritten()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("root\n{\n  a = "), 0xFF, 0xFE, (byte)'\n', (byte)'}', (byte)'\n'];

        var document = ConfigNodeDocument.ParseBytes(bytes);

        Assert.That(document.IsTextLossy, Is.True, "非法 UTF-8 必须被标记，写盘入口据此拒绝");
    }

    /// <summary>按行比较两段文本，返回发生变化的 1 基行号。用 \n 切分对 CRLF 也成立（两侧同样保留 \r）。</summary>
    private static IReadOnlyList<int> DiffLineNumbers(string before, string after)
    {
        var beforeLines = before.Split('\n');
        var afterLines = after.Split('\n');
        var count = Math.Max(beforeLines.Length, afterLines.Length);
        var changed = new List<int>();

        for (var index = 0; index < count; index++)
        {
            var oldLine = index < beforeLines.Length ? beforeLines[index] : "<缺失>";
            var newLine = index < afterLines.Length ? afterLines[index] : "<缺失>";
            if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                changed.Add(index + 1);
            }
        }

        return changed;
    }
}
