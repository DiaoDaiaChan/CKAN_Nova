using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class VersionDisplayFormatTests
{
    [TestCase("1:2.0", true, false, "2.0")]
    [TestCase("v1.2", false, true, "1.2")]
    [TestCase("1:v1.2", true, true, "1.2")]
    [TestCase("", true, true, "")]
    [TestCase("not-a-version", true, true, "not-a-version")]
    [TestCase("x:2.0", true, false, "x:2.0")]
    [TestCase("vbeta", false, true, "vbeta")]
    public void Format_RemovesOnlyValidConfiguredPrefixes(string input, bool epoch, bool prefix, string expected) =>
        Assert.That(VersionDisplayFormat.Format(input, epoch, prefix), Is.EqualTo(expected));
}
