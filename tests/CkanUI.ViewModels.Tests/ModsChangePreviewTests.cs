using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>选择集 → 变更集预览（03 篇 §3.6 / 04 篇 §6）。</summary>
[TestFixture]
public sealed class ModsChangePreviewTests
{
    [Test]
    public void EmptySelection_ProducesEmptyPreview()
    {
        var preview = ModsChangePreviewBuilder.Build(Array.Empty<ModRowData>(), Array.Empty<string>());

        Assert.That(preview.IsEmpty, Is.True);
        Assert.That(preview.EstimatedDownloadBytes, Is.EqualTo(0));
    }

    [Test]
    public void NotInstalledRow_ProducesInstall()
    {
        var row = ModsRowFactory.Create("A", "模组 A", latestVersion: "1.0.0", sizeBytes: 2048);

        var preview = ModsChangePreviewBuilder.Build(new[] { row }, new[] { "A" });

        Assert.That(preview.Items, Has.Count.EqualTo(1));
        Assert.That(preview.Items[0].Action, Is.EqualTo(ChangeAction.Install));
        Assert.That(preview.Items[0].ToVersion, Is.EqualTo("1.0.0"));
        Assert.That(preview.Items[0].Origin, Is.EqualTo(ChangeOrigin.UserRequested));
        Assert.That(preview.EstimatedDownloadBytes, Is.EqualTo(2048));
    }

    [Test]
    public void InstalledOutdatedRow_ProducesUpgradeWithVersions()
    {
        var row = ModsRowFactory.Create("B", "模组 B", installedVersion: "1.0.0", latestVersion: "2.0.0");

        var preview = ModsChangePreviewBuilder.Build(new[] { row }, new[] { "B" });

        Assert.That(preview.Items[0].Action, Is.EqualTo(ChangeAction.Upgrade));
        Assert.That(preview.Items[0].FromVersion, Is.EqualTo("1.0.0"));
        Assert.That(preview.Items[0].ToVersion, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void InstalledUpToDateRow_ProducesRemove()
    {
        var row = ModsRowFactory.Create("C", "模组 C", installedVersion: "2.0.0", latestVersion: "2.0.0");

        var preview = ModsChangePreviewBuilder.Build(new[] { row }, new[] { "C" });

        Assert.That(preview.Items[0].Action, Is.EqualTo(ChangeAction.Remove));
        Assert.That(preview.Items[0].ToVersion, Is.Null);
    }

    [Test]
    public void IncompatibleRow_ProducesConflict()
    {
        var row = ModsRowFactory.Create("D", "模组 D", isCompatible: false);

        var preview = ModsChangePreviewBuilder.Build(new[] { row }, new[] { "D" });

        Assert.That(preview.HasConflicts, Is.True);
        Assert.That(preview.Conflicts[0], Does.Contain("模组 D"));
    }

    [Test]
    public void UnknownIdentifier_IsIgnored()
    {
        var preview = ModsChangePreviewBuilder.Build(Array.Empty<ModRowData>(), new[] { "Missing" });

        Assert.That(preview.IsEmpty, Is.True);
    }
}
