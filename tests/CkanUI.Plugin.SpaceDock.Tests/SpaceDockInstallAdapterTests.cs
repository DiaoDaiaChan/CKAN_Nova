using System.IO;
using CkanUI.Services.Spacedock;
using NUnit.Framework;

namespace CkanUI.Plugin.SpaceDock.Tests;

[TestFixture]
public sealed class SpaceDockInstallAdapterTests
{
    [Test]
    public void Analyze_GameDataAndGameRoot_ReturnsMixedPlan()
    {
        var plan = SpaceDockInstallAdapter.Analyze(
            [
                "Wrapper/GameData/MyMod/plugin.dll",
                "Wrapper/GameData/MyMod/Textures/tex.png",
                "Wrapper/Ships/VAB/demo.craft",
            ],
            "My Mod");

        Assert.That(plan.Strategy, Is.EqualTo(SpaceDockInstallAdapter.Strategy.MixedRoots));
        Assert.That(plan.InstalledRoot, Is.EqualTo("MyMod"));
        Assert.That(plan.Entries, Has.Count.EqualTo(3));
        Assert.That(
            plan.Entries,
            Has.Some.Matches<SpaceDockInstallAdapter.Entry>(
                entry => entry is { Target: SpaceDockInstallAdapter.Target.GameData, RelativePath: "MyMod/plugin.dll" }));
        Assert.That(
            plan.Entries,
            Has.Some.Matches<SpaceDockInstallAdapter.Entry>(
                entry => entry is { Target: SpaceDockInstallAdapter.Target.GameRoot, RelativePath: "Ships/VAB/demo.craft" }));
    }

    [Test]
    public void Analyze_SingleTopFolder_StripsWrapperAndUsesModName()
    {
        var plan = SpaceDockInstallAdapter.Analyze(
            ["FancyMod/plugin.dll", "FancyMod/Textures/tex.png"],
            "My Mod");

        Assert.That(plan.Strategy, Is.EqualTo(SpaceDockInstallAdapter.Strategy.SingleFolder));
        Assert.That(plan.InstalledRoot, Is.EqualTo("My Mod"));
        Assert.That(plan.Entries.Select(entry => entry.RelativePath), Is.EqualTo(new[]
        {
            "My Mod/plugin.dll",
            "My Mod/Textures/tex.png",
        }));
    }

    [Test]
    public void Analyze_FlatPayload_WrapsUnderSanitizedModName()
    {
        var plan = SpaceDockInstallAdapter.Analyze(
            ["plugin.dll", "Textures/tex.png"],
            "Fancy: Mod");

        Assert.That(plan.Strategy, Is.EqualTo(SpaceDockInstallAdapter.Strategy.FlatPayload));
        Assert.That(plan.InstalledRoot, Is.EqualTo("Fancy_ Mod"));
        Assert.That(plan.Entries.Select(entry => entry.RelativePath), Is.EqualTo(new[]
        {
            "Fancy_ Mod/plugin.dll",
            "Fancy_ Mod/Textures/tex.png",
        }));
    }

    [Test]
    public void Analyze_GameRootOnly_DoesNotTouchGameData()
    {
        var plan = SpaceDockInstallAdapter.Analyze(
            ["Ships/VAB/demo.craft", "Missions/tutorial.mission"],
            "Craft Pack");

        Assert.That(plan.Strategy, Is.EqualTo(SpaceDockInstallAdapter.Strategy.GameRootTree));
        Assert.That(plan.InstalledRoot, Is.Empty);
        Assert.That(plan.Entries, Has.Count.EqualTo(2));
        Assert.That(plan.Entries.All(entry => entry.Target == SpaceDockInstallAdapter.Target.GameRoot), Is.True);
    }

    [Test]
    public void Analyze_GameDataWithoutModFolder_AddsModFolder()
    {
        var plan = SpaceDockInstallAdapter.Analyze(["GameData/plugin.dll"], "My Mod");

        Assert.That(plan.Strategy, Is.EqualTo(SpaceDockInstallAdapter.Strategy.GameDataTree));
        Assert.That(plan.InstalledRoot, Is.EqualTo("My Mod"));
        Assert.That(plan.Entries.Single().RelativePath, Is.EqualTo("My Mod/plugin.dll"));
    }

    [Test]
    public void Analyze_ShipsFolderInsideGameData_StaysInGameData()
    {
        var plan = SpaceDockInstallAdapter.Analyze(["GameData/Ships/plugin.dll"], "Ships Mod");

        Assert.That(plan.Strategy, Is.EqualTo(SpaceDockInstallAdapter.Strategy.GameDataTree));
        Assert.That(plan.InstalledRoot, Is.EqualTo("Ships"));
        Assert.That(plan.Entries.Single().Target, Is.EqualTo(SpaceDockInstallAdapter.Target.GameData));
        Assert.That(plan.Entries.Single().RelativePath, Is.EqualTo("Ships/plugin.dll"));
    }

    [Test]
    public void Analyze_EmptyArchive_ReturnsEmptyFlatPlan()
    {
        var plan = SpaceDockInstallAdapter.Analyze([], "My Mod");

        Assert.That(plan.Strategy, Is.EqualTo(SpaceDockInstallAdapter.Strategy.FlatPayload));
        Assert.That(plan.InstalledRoot, Is.EqualTo("My Mod"));
        Assert.That(plan.Entries, Is.Empty);
    }

    [TestCase("GameData/MyMod/../../evil.txt")]
    [TestCase("/GameData/MyMod/evil.txt")]
    [TestCase("GameData/MyMod/evil:name.txt")]
    public void Analyze_UnsafePath_Throws(string entry)
    {
        Assert.That(
            () => SpaceDockInstallAdapter.Analyze([entry], "My Mod"),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void SanitizeName_ReplacesInvalidCharactersAndFallsBack()
    {
        Assert.That(SpaceDockInstallAdapter.SanitizeName("  Fancy: Mod.  "), Is.EqualTo("Fancy_ Mod"));
        Assert.That(SpaceDockInstallAdapter.SanitizeName("   "), Is.EqualTo("SpaceDockMod"));
    }
}
