using CkanUI.Plugin.Rp1.Career;
using CkanUI.Plugin.Rp1.Tests.Support;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests;

[TestFixture]
public sealed class Rp1CareerServiceTests
{
    [Test]
    public async Task ListSaves_ReturnsPersistentSavesNewestFirst()
    {
        using var game = new TempGame();
        var older = game.WriteSave("Older", Rp1TestData.SaveText(""));
        var newer = game.WriteSave("Newer", Rp1TestData.SaveText(""));
        File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await new Rp1CareerService().ListSavesAsync(game.Root);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Select(save => save.Name), Is.EqualTo(new[] { "Newer", "Older" }));
        Assert.That(result.Value![0].PersistentPath, Is.EqualTo(newer));
    }

    [Test]
    public async Task Load_ReadsVersionAndCurrentValuesFromScenarios()
    {
        using var game = new TempGame();
        File.WriteAllText(Path.Combine(game.Root, "GameData", "RP-1", "RP-1.version"), """
          { "VERSION": { "MAJOR": 4, "MINOR": 7, "PATCH": 0, "BUILD": 0 } }
        """);
        game.WriteSave("Test", Rp1TestData.SaveText("""
          SCENARIO
          {
            name = Funding
            funds = 1234.5
          }
          SCENARIO
          {
            name = ResearchAndDevelopment
            sci = 67.8
          }
        """));

        var result = await new Rp1CareerService().LoadAsync(game.Root, "Test", "Instance", "1.12.5");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var timeline = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Rp1Version, Is.EqualTo("4.7.0.0"));
            Assert.That(timeline.GameVersion, Is.EqualTo("1.12.5"));
            Assert.That(timeline.CurrentUt, Is.EqualTo(5000));
            Assert.That(timeline.CurrentDateText, Is.EqualTo("1951-01-01 01:23"));
            Assert.That(timeline.CurrentFunds, Is.EqualTo(1234.5));
            Assert.That(timeline.CurrentScience, Is.EqualTo(67.8));
            Assert.That(timeline.SaveName, Is.EqualTo("Test"));
            Assert.That(timeline.Rp1Installed, Is.True);
        });
    }

    [Test]
    public async Task Load_RejectsEscapingSaveNamesAndMissingFiles()
    {
        using var game = new TempGame();
        var service = new Rp1CareerService();

        var escaping = await service.LoadAsync(game.Root, "../escape");
        var missing = await service.LoadAsync(game.Root, "NoSuchSave");

        Assert.That(escaping.IsSuccess, Is.False);
        Assert.That(escaping.Message, Does.Contain("不合法"));
        Assert.That(missing.IsSuccess, Is.False);
        Assert.That(missing.Message, Does.Contain("persistent.sfs"));
    }
}
