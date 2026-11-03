using CkanUI.Plugin.Rp1.Career;
using CkanUI.Plugin.Rp1.Tests.Support;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests;

[TestFixture]
public sealed class Rp1SaveReaderTests
{
    [Test]
    public void Read_ExtractsHeaderAndOnlyTargetScenarios()
    {
        using var game = new TempGame();
        var path = game.WriteSave("Test", Rp1TestData.SaveText("""
          SCENARIO
          {
            name = CareerLog
            _periodDict { version = 3 Values { LogPeriod { StartUT = 0 EndUT = 100 } } }
          }
          SCENARIO
          {
            name = Funding
            funds = 1234.5
          }
          SCENARIO
          {
            name = SomethingElse
            value = 1
          }
        """));

        var result = Rp1SaveReader.Read(path);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var set = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(set.CurrentUt, Is.EqualTo(5000));
            Assert.That(set.GameVersion, Is.EqualTo("1.12.5"));
            Assert.That(set.SaveTitle, Is.EqualTo("Test Save"));
            Assert.That(set.Scenarios.Keys, Is.EquivalentTo(new[] { "CareerLog", "Funding" }));
            Assert.That(set.Scenarios["Funding"].FindPair("funds")?.Value, Is.EqualTo("1234.5"));
            Assert.That(set.Diagnostics.Any(line => line.Contains("MilestoneHandler")), Is.True);
        });
    }

    [Test]
    public void Read_MissingFileFailsWithoutThrowing()
    {
        var result = Rp1SaveReader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nope.sfs"));
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不存在"));
    }
}
