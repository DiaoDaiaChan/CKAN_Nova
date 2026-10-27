using CkanUI.Localization;
using NUnit.Framework;

namespace CkanUI.Localization.Tests;

[TestFixture]
public sealed class LocalizerTests
{
    [Test]
    public void MissingKey_FallsBackToKey()
    {
        var loc = new Localizer();
        Assert.That(loc.Get("missing.key"), Is.EqualTo("missing.key"));
    }
}
