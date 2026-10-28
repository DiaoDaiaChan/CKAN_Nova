using CkanUI.Services.Unmanaged;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// installed_dlls 合并（12 篇 F-42 / INT-09）：上游 SetDlls 是整体替换，合并必须由调用方做，
/// 否则接管一个 DLL 会把其它手工安装的条目全清掉。
/// </summary>
[TestFixture]
public sealed class ManagedDllMergeTests
{
    [Test]
    public void Merge_KeepsExistingEntriesThatAreNotInTheAdditions()
    {
        var existing = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OldMod"] = "GameData/OldMod/OldMod.dll",
            ["Other"] = "GameData/Other/Other.dll",
        };
        var additions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JunkMod"] = "GameData/Stray/JunkMod.dll",
        };

        var merged = ManagedDllMerge.Merge(existing, additions);

        Assert.That(merged, Has.Count.EqualTo(3), "旧条目必须原样保留");
        Assert.That(merged["OldMod"], Is.EqualTo("GameData/OldMod/OldMod.dll"));
        Assert.That(merged["JunkMod"], Is.EqualTo("GameData/Stray/JunkMod.dll"));
        Assert.That(existing, Has.Count.EqualTo(2), "输入字典不能被就地改写");
    }

    [Test]
    public void Merge_LetsTheAdditionsWinOnTheSameIdentifier()
    {
        var existing = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JunkMod"] = "GameData/Elsewhere/JunkMod.dll",
        };
        var additions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JunkMod"] = "GameData/Stray/JunkMod.dll",
        };

        var merged = ManagedDllMerge.Merge(existing, additions);

        Assert.That(merged["JunkMod"], Is.EqualTo("GameData/Stray/JunkMod.dll"));
    }

    [Test]
    public void Merge_SkipsBlankIdentifiersAndPaths()
    {
        var existing = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [" "] = "GameData/Nowhere.dll",
            ["NoPath"] = "",
        };
        var additions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JunkMod"] = "GameData/Stray/JunkMod.dll",
        };

        var merged = ManagedDllMerge.Merge(existing, additions);

        Assert.That(merged.Keys, Is.EqualTo(new[] { "JunkMod" }));
    }

    [Test]
    public void Merge_ToleratesNullsOnBothSides()
    {
        Assert.That(ManagedDllMerge.Merge(null, null), Is.Empty);
        Assert.That(ManagedDllMerge.Merge(null, new Dictionary<string, string> { ["A"] = "GameData/A.dll" }), Has.Count.EqualTo(1));
    }

    [Test]
    public void SnapshotFileName_IsTimestamped()
    {
        var name = ManagedDllMerge.SnapshotFileName(new DateTime(2026, 10, 28, 10, 10, 10, DateTimeKind.Utc));

        Assert.That(name, Is.EqualTo("registry.json.bak-20261028-101010"));
        Assert.That(name, Does.StartWith(ManagedDllMerge.SnapshotPrefix));
    }
}
