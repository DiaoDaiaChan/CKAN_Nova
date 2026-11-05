using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Changes;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>变更集文案与格式化（03 篇 §4 摘要/明细列文案、§9 状态双通道、§11 格式化集中一处）。</summary>
[TestFixture]
public sealed class ChangesFormatterTests
{
    [Test]
    public void ActionText_CoversAllActions()
    {
        Assert.That(ChangesFormatter.ActionText(ChangeAction.Install), Is.EqualTo("安装"));
        Assert.That(ChangesFormatter.ActionText(ChangeAction.Upgrade), Is.EqualTo("升级"));
        Assert.That(ChangesFormatter.ActionText(ChangeAction.Downgrade), Is.EqualTo("降级"));
        Assert.That(ChangesFormatter.ActionText(ChangeAction.Remove), Is.EqualTo("卸载"));
        Assert.That(ChangesFormatter.ActionText(ChangeAction.Replace), Is.EqualTo("替换"));
    }

    [Test]
    public void OriginText_AndBadge_CoverAllOrigins()
    {
        Assert.That(ChangesFormatter.OriginBadgeText(ChangeOrigin.UserRequested), Is.EqualTo("用户"));
        Assert.That(ChangesFormatter.OriginBadgeText(ChangeOrigin.Dependency), Is.EqualTo("依赖"));
        Assert.That(ChangesFormatter.OriginBadgeText(ChangeOrigin.Recommendation), Is.EqualTo("推荐"));
        Assert.That(ChangesFormatter.OriginBadgeText(ChangeOrigin.AutoInstalled), Is.EqualTo("自动"));
        Assert.That(ChangesFormatter.OriginText(ChangeOrigin.UserRequested), Is.EqualTo("用户选择"));
        Assert.That(ChangesFormatter.OriginText(ChangeOrigin.AutoInstalled), Is.EqualTo("自动安装"));
    }

    [Test]
    public void DownloadText_UsesSharedByteFormatter()
    {
        Assert.That(ChangesFormatter.DownloadText(0), Is.EqualTo("无需下载"));
        Assert.That(ChangesFormatter.DownloadText(-1), Is.EqualTo("无需下载"));
        Assert.That(ChangesFormatter.DownloadText(20_000_000), Is.EqualTo("预计下载 19.1 MB"));
        Assert.That(ChangesFormatter.Bytes(null), Is.EqualTo("—"));
        Assert.That(ChangesFormatter.Bytes(2_048), Is.EqualTo("2.0 KB"));
    }

    [Test]
    public void Summary_MentionsReplaceOnlyWhenPresent()
    {
        var plain = ChangesFakes.Preview(new[] { ChangesFakes.Item("a") }, 1_024);
        Assert.That(ChangesFormatter.Summary(plain), Does.Not.Contain("替换"));

        var withReplace = ChangesFakes.Preview(
            new[] { ChangesFakes.Item("a"), ChangesFakes.Item("b", action: ChangeAction.Replace) },
            1_024);
        Assert.That(ChangesFormatter.Summary(withReplace), Does.Contain("替换 1 个"));
    }

    [Test]
    public void Summary_LeavesDownloadVolumeToItsOwnLine()
    {
        // 摘要卡上是两行：粗体数量 + 小字体积。Summary 再拼一遍体积就是同一张卡说两遍。
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a") }, 20_000_000);

        Assert.That(ChangesFormatter.Summary(preview), Does.Not.Contain("预计下载"));
        Assert.That(ChangesFormatter.Summary(preview), Does.Not.Contain("19.1 MB"));
        Assert.That(ChangesFormatter.DownloadText(preview.EstimatedDownloadBytes), Is.EqualTo("预计下载 19.1 MB"));
    }

    [Test]
    public void StateText_IsNeverEmptyForAnyState()
    {
        foreach (var state in Enum.GetValues<ChangeExecutionState>())
        {
            Assert.That(ChangesFormatter.StateText(state, 0), Is.Not.Empty, state.ToString());
            Assert.That(ChangesFormatter.StateBadge(state), Is.Not.Empty, state.ToString());
        }
    }

    [Test]
    public void StateText_ClampsPercent()
    {
        Assert.That(ChangesFormatter.StateText(ChangeExecutionState.Applying, 140), Does.Contain("100%"));
        Assert.That(ChangesFormatter.StateText(ChangeExecutionState.Applying, -5), Does.Contain("0%"));
    }

    [Test]
    public void InstallResult_DistinguishesFullSuccessFromPartialFailure()
    {
        Assert.That(ChangesFormatter.InstallResult(ChangesFakes.SuccessReport()), Does.Contain("全部成功"));
        Assert.That(ChangesFormatter.InstallResult(ChangesFakes.PartialFailureReport()), Does.Contain("部分失败"));
    }

    [Test]
    public void Lines_JoinsAndHandlesEmpty()
    {
        Assert.That(ChangesFormatter.Lines(null), Is.Empty);
        Assert.That(ChangesFormatter.Lines(Array.Empty<string>()), Is.Empty);
        Assert.That(ChangesFormatter.Lines(new[] { "a", "b" }), Is.EqualTo("a" + Environment.NewLine + "b"));
    }

    [Test]
    public void Counts_AreZeroForEmptyPreview()
    {
        var counts = ChangesFormatter.Count(ChangesFormatter.EmptyPreview);
        Assert.That(counts.Total, Is.Zero);
        Assert.That(counts.Install, Is.Zero);
        Assert.That(ChangesFormatter.Count(null).Total, Is.Zero);
    }
}
