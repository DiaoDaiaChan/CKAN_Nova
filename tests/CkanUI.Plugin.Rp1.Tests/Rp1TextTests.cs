using CkanUI.Plugin.Rp1.Career;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests;

[TestFixture]
public sealed class Rp1TextTests
{
    [Test]
    public void Clean_RemovesRichTextAndCollapsesBlankLines()
        => Assert.That(
            Rp1Text.Clean("<color=#fff>hello</color><br>\\n\\n<B>world</B>"),
            Is.EqualTo("hello\n\nworld"));

    [Test]
    public void Clean_ReturnsNullForWhitespace()
        => Assert.That(Rp1Text.Clean("  \n "), Is.Null);
}
