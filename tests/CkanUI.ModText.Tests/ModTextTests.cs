using CkanUI.ModText;
using NUnit.Framework;

namespace CkanUI.ModText.Tests;

[TestFixture]
public sealed class ModTextTests
{
    [Test]
    public void ModText_CarriesSource()
    {
        var text = new ModText("你好", ModTextSource.Human, false);
        Assert.That(text.Source, Is.EqualTo(ModTextSource.Human));
    }
}
