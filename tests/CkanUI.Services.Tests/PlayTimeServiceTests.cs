using CkanUI.Services.PlayTime;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class PlayTimeServiceTests
{
    private TempWorkspace _temp = null!;

    [SetUp]
    public void SetUp() => _temp = new TempWorkspace("playtime");

    [TearDown]
    public void TearDown() => _temp.Dispose();

    private string TimesFile => _temp.Combine("times.json");

    [Test]
    public void MissingOrCorruptFileLoadsEmpty()
    {
        var service = new PlayTimeService(TimesFile);
        Assert.That(service.Load(), Is.Empty);
        File.WriteAllText(TimesFile, "{");
        Assert.That(service.Load(), Is.Empty);
    }

    [Test]
    public void SavesRoundedValuesAndReplacesNamedInstance()
    {
        var service = new PlayTimeService(TimesFile);
        Assert.That(service.Save("Alpha", 2.5), Is.True);
        Assert.That(service.Save("Alpha", 4.4), Is.True);
        var entry = service.Load().Single();
        Assert.That(entry.Hours, Is.EqualTo(4));
    }

    [Test]
    public void RejectsNegativeAndNonFiniteValues()
    {
        var service = new PlayTimeService(TimesFile);
        Assert.That(service.Save("Alpha", -1), Is.False);
        Assert.That(service.Save("Alpha", double.NaN), Is.False);
    }
}
