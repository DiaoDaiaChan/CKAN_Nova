using System;
using System.Collections.Generic;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.SpaceDock.Tests;

[TestFixture]
public sealed class SpaceDockDuplicateMatcherTests
{
    [Test]
    public void RepositorySpacedockUrl_HidesSameNumericId()
    {
        var matcher = SpaceDockDuplicateMatcher.Create(
            Repository(Row("BetterBurnTime", "Better Burn Time", "https://spacedock.info/mod/21/BetterBurnTime")),
            RegistrySnapshot.Empty());

        Assert.That(matcher.IsDuplicate(Mod(21, "Completely Different Name", "/mod/21/Whatever")), Is.True);
    }

    [Test]
    public void RepositoryName_IsNormalizedBeforeMatching()
    {
        var matcher = SpaceDockDuplicateMatcher.Create(
            Repository(Row("BetterBurnTime", "Better Burn Time")),
            RegistrySnapshot.Empty());

        Assert.That(matcher.IsDuplicate(Mod(99, "better burn time!", "/mod/99/better-burn-time")), Is.True);
    }

    [Test]
    public void InstalledRegistrySpacedockUrl_IsAlsoMatched()
    {
        var installed = new Dictionary<string, InstalledModuleInfo>(StringComparer.Ordinal)
        {
            ["SomeOtherId"] = new("SomeOtherId", "1.2.3", false, null, null)
            {
                Facts = new ModSearchFacts { SpacedockUrl = "https://spacedock.info/mod/314/PiMod" },
            },
        };
        var matcher = SpaceDockDuplicateMatcher.Create(
            RepositorySnapshot.Empty,
            new RegistrySnapshot("test", null, installed, new HashSet<string>(), DateTime.UtcNow));

        Assert.That(matcher.IsDuplicate(Mod(314, "PiMod", "/mod/314/PiMod")), Is.True);
    }

    [Test]
    public void UrlSlug_MatchesCkanIdentifier()
    {
        var matcher = SpaceDockDuplicateMatcher.Create(
            Repository(Row("betterburntime", "Different Display Name")),
            RegistrySnapshot.Empty());

        Assert.That(matcher.IsDuplicate(Mod(77, "Unknown", "/mod/77/betterburntime")), Is.True);
    }

    [Test]
    public void UnrelatedMod_IsNotDuplicate()
    {
        var matcher = SpaceDockDuplicateMatcher.Create(
            Repository(Row("BetterBurnTime", "Better Burn Time", "https://spacedock.info/mod/21/BetterBurnTime")),
            RegistrySnapshot.Empty());

        Assert.That(matcher.IsDuplicate(Mod(22, "MechJeb", "/mod/22/MechJeb")), Is.False);
    }

    [Test]
    public void ShortNames_AreIgnoredToAvoidFalsePositives()
    {
        var matcher = SpaceDockDuplicateMatcher.Create(
            Repository(Row("KSP", "KSP")),
            RegistrySnapshot.Empty());

        Assert.That(matcher.IsDuplicate(Mod(1, "KSP", "/mod/1/KSP")), Is.False);
    }

    private static RepositorySnapshot Repository(params ModRowData[] rows) =>
        new(rows, Array.Empty<string>(), null, true);

    private static ModRowData Row(string identifier, string name, string? spacedockUrl = null) =>
        new(
            identifier,
            name,
            null,
            null,
            null,
            0,
            Array.Empty<string>(),
            null,
            false,
            false,
            true,
            false,
            false,
            false)
        {
            Facts = new ModSearchFacts { SpacedockUrl = spacedockUrl },
        };

    private static SpaceDockModInfo Mod(long id, string name, string url) =>
        new(
            id,
            name,
            "author",
            "short",
            "description",
            0,
            0,
            null,
            null,
            null,
            null,
            "https://spacedock.info" + url,
            Array.Empty<SpaceDockVersionInfo>());
}
