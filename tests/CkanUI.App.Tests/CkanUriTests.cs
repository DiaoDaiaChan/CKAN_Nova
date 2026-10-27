using CkanUI.Platform.SingleInstance;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>ckan:// 深链解析（ARC-05）。</summary>
[TestFixture]
public class CkanUriTests
{
    [TestCase("ckan://mod/B9PartSwitch", "mod", "B9PartSwitch")]
    [TestCase("CKAN://instance/My%20KSP", "instance", "My KSP")]
    [TestCase("ckan://repo/default", "repo", "default")]
    public void TryParse_AcceptsKnownKinds(string raw, string kind, string value)
    {
        Assert.That(CkanUri.TryParse(raw, out var command), Is.True);
        Assert.That(command!.Kind, Is.EqualTo(kind));
        Assert.That(command.Value, Is.EqualTo(value));
    }

    [TestCase("")]
    [TestCase("https://example.com/mod/x")]
    [TestCase("ckan://unknown/x")]
    [TestCase("ckan://mod")]
    [TestCase("ckan://mod/a/b")]
    [TestCase("ckan://mod/a?x=1")]
    [TestCase("ckan://mod/%20")]
    public void TryParse_RejectsMalformed(string raw)
    {
        Assert.That(CkanUri.TryParse(raw, out var command), Is.False);
        Assert.That(command, Is.Null);
    }

    [Test]
    public void Build_RoundTripsThroughTryParse()
    {
        var original = new CkanUriCommand("mod", "Kerbal Engineer / Redux");
        Assert.That(CkanUri.TryParse(CkanUri.Build(original), out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(original));
    }
}
