using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

/// <summary>
/// 流式大纲必须和完整解析给出同样的页首数字、同样的分区顺序；
/// 未知的 mod 块要照常成区，不能因为词表里没有就丢掉。
/// </summary>
[TestFixture]
public sealed class SfsStreamOutlineTests
{
    private TempWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TempWorkspace("sfs-stream-outline");

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private string Write(string text, string name = "persistent.sfs")
    {
        var path = _workspace.Combine(name);
        SfsSamples.WriteFile(path, text);
        return path;
    }

    private SfsStreamOutlineResult Build(string text, SfsStreamOptions? streamOptions = null)
    {
        var path = Write(text);
        var profile = SfsStreamScanner.Scan(path, streamOptions);
        return SfsStreamOutline.Build(profile);
    }

    [Test]
    public void Build_MatchesFullOutlineOnCareer()
    {
        var document = SfsWorkbenchSamples.Parse();
        var full = SfsOutline.Build(document);
        var stream = Build(SfsWorkbenchSamples.Career);

        Assert.Multiple(() =>
        {
            Assert.That(stream.Snapshot.PairCount, Is.EqualTo(full.Snapshot.PairCount));
            Assert.That(stream.Snapshot.NodeCount, Is.EqualTo(full.Snapshot.NodeCount));
            Assert.That(stream.Snapshot.LineCount, Is.EqualTo(full.Snapshot.LineCount));
            Assert.That(stream.Snapshot.VesselCount, Is.EqualTo(1));
            Assert.That(stream.Snapshot.PartCount, Is.EqualTo(1));
            Assert.That(stream.Snapshot.CrewCount, Is.EqualTo(1));
            Assert.That(stream.Snapshot.Funds?.Value, Is.EqualTo("1234.5"));
            Assert.That(stream.Snapshot.Funds?.Line, Is.EqualTo(full.Snapshot.Funds!.Line));
            Assert.That(stream.Snapshot.Science?.Value, Is.EqualTo("42"));
            Assert.That(stream.Snapshot.GameTime?.Value, Is.EqualTo("5005129.6190400701"));
            Assert.That(stream.Sections.Select(section => section.Id), Is.EqualTo(full.Sections.Select(section => section.Id)));
            Assert.That(stream.Snapshot.Truncated, Is.False);
        });
    }

    [Test]
    public void Build_SeparatesSamePathSiblingScenariosByLineRange()
    {
        var stream = Build(SfsWorkbenchSamples.Career);
        var funding = stream.Sections.Single(section => section.Subtitle == "Funding");
        var research = stream.Sections.Single(section => section.Subtitle == "ResearchAndDevelopment");

        Assert.Multiple(() =>
        {
            Assert.That(funding.Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "name", "funds" }));
            Assert.That(research.Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "name", "sci" }));
            Assert.That(funding.Fields.Select(field => field.Line), Is.Not.EqualTo(research.Fields.Select(field => field.Line)),
                "两个 SCENARIO 共用 Path，必须靠行区间分开，不能把兄弟字段列进同一个分区");
            Assert.That(funding.Nodes, Is.Empty);
            Assert.That(research.Nodes, Is.Empty);
        });
    }

    [Test]
    public void Build_LabelsKnownValuesAndKeepsThemAtTheTop()
    {
        var stream = Build(SfsWorkbenchSamples.Career);

        Assert.Multiple(() =>
        {
            Assert.That(stream.Sections[0].Id, Is.EqualTo("game"));
            Assert.That(stream.Snapshot.Funds!.Label, Is.EqualTo("资金"));
            Assert.That(stream.Snapshot.Funds.Priority, Is.EqualTo(0));
            Assert.That(stream.Snapshot.Funds.Unit, Is.EqualTo("√"));
            Assert.That(stream.Snapshot.Funds.IsHighlighted, Is.True);
            Assert.That(stream.Snapshot.Highlights, Is.Ordered.By(nameof(SfsOutlineField.Priority)));
            Assert.That(stream.Snapshot.Highlights.Select(field => field.Key),
                Does.Contain("funds").And.Contain("sci").And.Contain("UT"));
        });
    }

    [Test]
    public void Build_KeepsUnknownModBlocksAsTheirOwnSections()
    {
        const string modded =
            "GAME\n"
            + "{\n"
            + "\tMODDATA\n"
            + "\t{\n"
            + "\t\tcacheVersion = 7\n"
            + "\t\tPAYLOAD\n"
            + "\t\t{\n"
            + "\t\t\tblob = 12\n"
            + "\t\t}\n"
            + "\t}\n"
            + "}\n";

        var stream = Build(modded);
        var modSection = stream.Sections.Single(section => section.Subtitle == "MODDATA");

        Assert.Multiple(() =>
        {
            Assert.That(stream.Sections, Has.Count.EqualTo(2));
            Assert.That(modSection.Title, Is.EqualTo("MODDATA"));
            Assert.That(modSection.Kind, Is.EqualTo(SfsSectionKind.Other));
            Assert.That(modSection.Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "cacheVersion" }));
            Assert.That(modSection.Fields[0].IsKnown, Is.False);
            Assert.That(modSection.Fields[0].DisplayName, Is.EqualTo("cacheVersion"));
            Assert.That(modSection.Nodes.Select(node => node.Name), Is.EquivalentTo(new[] { "PAYLOAD" }));
            Assert.That(modSection.TotalPairCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Build_GivesNodeRefsOrdinalPathsAndSubtreeCounts()
    {
        var stream = Build(SfsWorkbenchSamples.Career);
        var flight = stream.Sections.Single(section => section.Kind == SfsSectionKind.Flight);
        var vessel = flight.Nodes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(vessel.Name, Is.EqualTo("VESSEL"));
            Assert.That(vessel.Path, Is.EqualTo("GAME/FLIGHTSTATE/VESSEL#0"));
            Assert.That(vessel.PairCount, Is.EqualTo(5));
            Assert.That(vessel.NodeCount, Is.EqualTo(2));
            Assert.That(vessel.Summary, Does.StartWith("Kerbal 1 · "), "块摘要要带出飞船名，方便认船");
            Assert.That(vessel.Summary, Does.Contain("5 键值 · 2 块"));
            Assert.That(vessel.SectionId, Is.EqualTo(flight.Id));
            Assert.That(vessel.EndLine, Is.GreaterThan(vessel.Line), "搜索定位要靠块的行区间");
            Assert.That(vessel.ByteLength, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Build_KeepsGlobalTruncationOnTheSnapshot()
    {
        var path = Write(SfsWorkbenchSamples.Career);
        var profile = SfsStreamScanner.Scan(path, new SfsStreamOptions { FieldBudget = 2 });
        var stream = SfsStreamOutline.Build(profile);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Truncated, Is.True);
            Assert.That(stream.Snapshot.Truncated, Is.True, "整份存档仍要标记截断");
        });
    }

    [Test]
    public void Build_MarksOnlyTheSectionsWhoseOwnListWasClipped()
    {
        const string sample =
            "GAME\r\n{\r\n\tversion = 1.0\r\n\tUT = 5\r\n\tSCENARIO\r\n\t{\r\n\t\tname = Funding\r\n\t}\r\n}\r\n";
        var path = Write(sample);
        var profile = SfsStreamScanner.Scan(path);
        var stream = SfsStreamOutline.Build(profile, options: new SfsOutlineOptions { MaxFieldsPerSection = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(stream.Snapshot.Truncated, Is.False, "文件本身没有超过扫描预算");
            Assert.That(stream.Sections.Single(section => section.Id == "game").Truncated, Is.True,
                "存档根有两个键值，超过每区预算");
            Assert.That(stream.Sections.Single(section => section.Subtitle == "Funding").Truncated, Is.False,
                "资金场景只有一个键值，没被裁就不该跟着喊已截断");
        });
    }

    [Test]
    public void Build_SurvivesSampleWithoutRootBlock()
    {
        const string bare = "version = 1.0\nname = loose\n";
        var stream = Build(bare);

        Assert.Multiple(() =>
        {
            Assert.That(stream.Sections, Has.Count.EqualTo(1));
            Assert.That(stream.Sections[0].Kind, Is.EqualTo(SfsSectionKind.FileHeader));
            Assert.That(stream.Sections[0].Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "version", "name" }));
        });
    }
}