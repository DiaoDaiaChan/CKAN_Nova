using CkanUI.Plugin.Rp1.Career;
using CkanUI.Plugin.Rp1.Tests.Support;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests;

[TestFixture]
public sealed class Rp1TimelineBuilderTests
{
    [Test]
    public void Build_MergesContractsMilestonesProgramsCrewAndPeriods()
    {
        using var game = new TempGame();
        WriteDefinitions(game.Root);

        var scenarioSet = Rp1TestData.Set(
            ("CareerLog", Rp1TestData.Scenario("""
                name = CareerLog
                _contractDict
                {
                  version = 0
                  ContractEvent
                  {
                    InternalName = LunarImpactor
                    DisplayName = First Lunar Impactor
                    Type = Complete
                    UT = 600
                  }
                  ContractEvent
                  {
                    InternalName = KarmanLine
                    DisplayName = Karman Line
                    Type = Complete
                    UT = 500
                  }
                  ContractEvent
                  {
                    InternalName = KarmanLine
                    DisplayName = Karman Line
                    Type = Complete
                    UT = 450
                  }
                  ContractEvent
                  {
                    InternalName = exclude_Test
                    DisplayName = Skipped
                    Type = Complete
                    UT = 1
                  }
                }
                _periodDict
                {
                  version = 3
                  Keys
                  {
                    key = 0
                    key = 1
                  }
                  Values
                  {
                    LogPeriod
                    {
                      StartUT = 0
                      EndUT = 1000
                      CurrentFunds = 1000
                      CurrentSci = 10
                      ScienceEarned = 5
                      Confidence = 10
                      Reputation = 20
                      NumNautsKilled = 1
                    }
                    LogPeriod
                    {
                      StartUT = 1000
                      EndUT = 2000
                      CurrentFunds = 2000
                      CurrentSci = 20
                      ScienceEarned = 7
                      Confidence = 11
                      Reputation = 21
                      NumNautsKilled = 0
                    }
                  }
                }
            """)),
            ("MilestoneHandler", Rp1TestData.Scenario("""
                name = MilestoneHandler
                seenMilestones
                {
                  item = KarmanLineMilestone
                  item = LunarImpactorMilestone
                }
                milestoneData
                {
                  version = 3
                  Keys
                  {
                    key = KarmanLineMilestone
                    key = LunarImpactorMilestone
                  }
                  Values
                  {
                    PersistentListValueType`1
                    {
                      item = 
                      item = "探空1号"
                    }
                    PersistentListValueType`1
                    {
                      item = "Bob"
                    }
                  }
                }
            """)),
            ("RP1ContractTracker", Rp1TestData.Scenario("""
                name = RP1ContractTracker
                ABCDEF0123456789ABCDEF0123456789
                {
                  Contract
                  {
                    Name = KarmanLine
                    CompletionTime = 500
                  }
                }
            """)),
            ("ProgramHandler", Rp1TestData.Scenario("""
                name = ProgramHandler
                ActivatedStrategies
                {
                  SuborbRocketDev = 100
                  leaderFoo = -1
                  EarlySatellites = 200
                }
                ACTIVEPROGRAM
                {
                  name = NASAFunding
                  acceptedUT = 800
                  completedUT = 0
                }
                ACTIVEPROGRAM
                {
                  name = EarlySatellites
                  acceptedUT = 200
                  completedUT = 900
                }
            """)),
            ("CrewHandler", Rp1TestData.Scenario("""
                name = CrewHandler
                _retireTimes
                {
                  Jeb = 700
                }
            """)),
            ("Funding", Rp1TestData.Scenario("name = Funding\nfunds = 1234.5")),
            ("ResearchAndDevelopment", Rp1TestData.Scenario("name = ResearchAndDevelopment\nsci = 67.8")),
            ("Confidence", Rp1TestData.Scenario("name = Confidence\nconfidence = 90")),
            ("Reputation", Rp1TestData.Scenario("name = Reputation\nrep = 12.3")));

        var catalog = Rp1DefinitionCatalog.Load(game.Root);
        var timeline = Rp1TimelineBuilder.Build(game.Root, "Test", Path.Combine(game.Root, "saves", "Test", "persistent.sfs"), scenarioSet, catalog, "4.7.0.0", null);

        Assert.Multiple(() =>
        {
            Assert.That(timeline.ContractCount, Is.EqualTo(2));
            Assert.That(timeline.MilestoneCount, Is.EqualTo(2));
            Assert.That(timeline.ProgramCount, Is.EqualTo(3));
            Assert.That(timeline.CrewRetiredCount, Is.EqualTo(1));
            Assert.That(timeline.CrewLostCount, Is.EqualTo(1));
            Assert.That(timeline.Periods.Count, Is.EqualTo(2));
            Assert.That(timeline.CurrentFunds, Is.EqualTo(1234.5));
            Assert.That(timeline.CurrentScience, Is.EqualTo(67.8));
            Assert.That(timeline.CurrentConfidence, Is.EqualTo(90));
            Assert.That(timeline.CurrentReputation, Is.EqualTo(12.3));
            Assert.That(timeline.CurrentUt, Is.EqualTo(5000));

            var karman = timeline.Events.Single(item => item.InternalName == "KarmanLine" && item.Kind == Rp1EventKind.Contract);
            Assert.That(karman.Ut, Is.EqualTo(450), "同一合同多条完成记录取最早一次");
            Assert.That(karman.Title, Is.EqualTo("Karman Line"));

            var milestone = timeline.Events.Single(item => item.InternalName == "KarmanLineMilestone");
            Assert.That(milestone.Ut, Is.EqualTo(450));
            Assert.That(milestone.Title, Is.EqualTo("ROCKET REACHES SPACE"));
            Assert.That(milestone.Subtitle, Is.EqualTo("探空1号"));

            Assert.That(timeline.Events.Any(item => item.InternalName == "exclude_Test"), Is.False);
            Assert.That(timeline.Events.Any(item => item.Kind == Rp1EventKind.ProgramCompleted), Is.True);
            Assert.That(timeline.Events.Any(item => item.InternalName == "Jeb"), Is.True);
        });
    }

    [Test]
    public void Build_MissingScenariosOnlyAddsDiagnostics()
    {
        using var game = new TempGame();
        var timeline = Rp1TimelineBuilder.Build(
            game.Root,
            "Empty",
            Path.Combine(game.Root, "saves", "Empty", "persistent.sfs"),
            Rp1TestData.Set(),
            Rp1DefinitionCatalog.Load(game.Root),
            null,
            null);

        Assert.That(timeline.Events, Is.Empty);
        Assert.That(timeline.HasEvents, Is.False);
        Assert.That(timeline.Diagnostics, Is.Not.Empty);
    }

    private static void WriteDefinitions(string root)
    {
        var contracts = Path.Combine(root, "GameData", "RP-1", "Contracts");
        Directory.CreateDirectory(contracts);
        File.WriteAllText(Path.Combine(contracts, "KarmanLine.cfg"), """
          CONTRACT_TYPE
          {
            name = KarmanLine
            title = Karman Line
            group = SuborbRocketDev
            synopsis = Reach space.
          }
        """);

        var strategies = Path.Combine(root, "GameData", "RP-1", "Strategies");
        Directory.CreateDirectory(strategies);
        File.WriteAllText(Path.Combine(strategies, "Milestones.cfg"), """
          RP0_MILESTONE
          {
            name = KarmanLineMilestone
            contractName = KarmanLine
            headline = ROCKET REACHES SPACE
            article = <color=#0000>Reached space.</color>
          }
          RP0_MILESTONE
          {
            name = LunarImpactorMilestone
            contractName = LunarImpactor
            headline = FIRST LUNAR IMPACT
          }
        """);

        var programs = Path.Combine(root, "GameData", "RP-1", "Programs");
        Directory.CreateDirectory(programs);
        File.WriteAllText(Path.Combine(programs, "Programs.cfg"), """
          RP0_PROGRAM
          {
            name = SuborbRocketDev
            title = Early Rocket Development
            description = Sounding rockets.
          }
        """);
    }
}
