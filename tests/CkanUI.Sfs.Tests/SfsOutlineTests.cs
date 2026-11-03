using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsOutlineTests
{
    [Test]
    public void Build_SurfacesCareerNumbers()
    {
        var outline = SfsOutline.Build(SfsWorkbenchSamples.Parse());
        var snapshot = outline.Snapshot;

        Assert.That(snapshot.Funds, Is.Not.Null);
        Assert.That(snapshot.Funds!.Value, Is.EqualTo("1234.5"));
        Assert.That(snapshot.Science!.Value, Is.EqualTo("42"));
        Assert.That(snapshot.Reputation, Is.Null);
        Assert.That(snapshot.GameTime!.Value, Is.EqualTo("5005129.6190400701"));
        Assert.That(snapshot.Version, Is.EqualTo("1.0"));
    }

    [Test]
    public void Build_CountsScaleInOnePass()
    {
        var snapshot = SfsOutline.Build(SfsWorkbenchSamples.Parse()).Snapshot;

        Assert.That(snapshot.VesselCount, Is.EqualTo(1));
        Assert.That(snapshot.PartCount, Is.EqualTo(1));
        Assert.That(snapshot.ScenarioCount, Is.EqualTo(2));
        Assert.That(snapshot.CrewCount, Is.EqualTo(1));
        Assert.That(snapshot.NodeCount, Is.EqualTo(9));
        Assert.That(snapshot.PairCount, Is.EqualTo(14));
        Assert.That(snapshot.Truncated, Is.False);
    }

    [Test]
    public void Build_PutsKnownScenariosFirst()
    {
        var outline = SfsOutline.Build(SfsWorkbenchSamples.Parse());
        var titles = outline.Sections.Select(section => section.Title).ToArray();

        Assert.That(titles, Is.EqualTo(new[] { "存档信息", "资金", "科技", "飞行器", "乘员" }));
        Assert.That(outline.Sections[3].Kind, Is.EqualTo(SfsSectionKind.Flight));
    }

    [Test]
    public void Build_HighlightStartsWithFunds()
    {
        var snapshot = SfsOutline.Build(SfsWorkbenchSamples.Parse()).Snapshot;

        Assert.That(snapshot.Highlights, Is.Not.Empty);
        Assert.That(snapshot.Highlights[0].Key, Is.EqualTo("funds"));
        Assert.That(snapshot.Highlights.Select(field => field.Key), Does.Contain("UT"));
    }

    [Test]
    public void Build_KeepsUnknownKeysFromMods()
    {
        var text = SfsWorkbenchSamples.Career.Replace(
            "\tversion = 1.0\r\n",
            "\tversion = 1.0\r\n\tSomeModThing = 7\r\n",
            StringComparison.Ordinal);
        var outline = SfsOutline.Build(SfsWorkbenchSamples.Parse(text));
        var field = outline.Sections.SelectMany(section => section.Fields)
            .FirstOrDefault(candidate => candidate.Key == "SomeModThing");

        Assert.That(field, Is.Not.Null);
        Assert.That(field!.IsKnown, Is.False);
        Assert.That(field.Value, Is.EqualTo("7"));
        Assert.That(field.Label, Is.EqualTo("SomeModThing"));
    }

    [Test]
    public void Search_MatchesKeyValueAndName()
    {
        var outline = SfsOutline.Build(SfsWorkbenchSamples.Parse());

        var funds = outline.Search("funds");
        Assert.That(funds.Fields.Select(field => field.Key), Does.Contain("funds"));

        var value = outline.Search("1234");
        Assert.That(value.Fields.Select(field => field.Key), Does.Contain("funds"));

        var byName = outline.Search("Kerbal 1");
        Assert.That(byName.Nodes.Select(node => node.Name), Does.Contain("VESSEL"));

        var multi = outline.Search("name Funding");
        Assert.That(multi.Fields.Select(field => field.Key), Does.Contain("name"));

        Assert.That(outline.Search(string.Empty).Total, Is.Zero);
    }

    [Test]
    public void DescribeNode_ExpandsArbitraryBlock()
    {
        var document = SfsWorkbenchSamples.Parse();
        var outline = SfsOutline.Build(document);
        var vesselLine = SfsWorkbenchSamples.LineOf(SfsWorkbenchSamples.Career, "VESSEL");

        var detail = outline.DescribeNode(vesselLine);

        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.Name, Is.EqualTo("VESSEL"));
        Assert.That(detail.Fields.Select(field => field.Key), Is.EquivalentTo(new[] { "name", "persistentId" }));
        Assert.That(detail.Nodes.Select(node => node.Name), Does.Contain("PART"));
        Assert.That(detail.Path, Does.Contain("VESSEL#0"));
        Assert.That(outline.DescribeNode(3), Is.Null, "键值对行不是块节点");
    }

    [Test]
    public void Build_RespectsSectionFieldBudget()
    {
        var options = new SfsOutlineOptions { MaxFieldsPerSection = 0 };
        var outline = SfsOutline.Build(SfsWorkbenchSamples.Parse(), options: options);
        var flight = outline.Sections.Single(section => section.Title == "飞行器");

        Assert.That(flight.Truncated, Is.True);
        Assert.That(flight.Fields, Is.Empty);
        Assert.That(outline.Snapshot.Truncated, Is.True);
    }
}
