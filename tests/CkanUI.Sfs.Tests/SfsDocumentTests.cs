using CkanUI.Sfs;
using NUnit.Framework;

namespace CkanUI.Sfs.Tests;

[TestFixture]
public sealed class SfsDocumentTests
{
    [Test]
    public void Constructor_KeepsText()
    {
        var doc = new SfsDocument("a.sfs", "GAME\n{}\n");
        Assert.That(doc.Text, Does.Contain("GAME"));
    }
}
