using CkanUI.Services.Abstractions.Commands;
using CkanUI.ViewModels.CommandPalette;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.CommandPalette;

/// <summary>
/// 标题高亮切段（纯函数）：命中区间 → 文本段。命令面板的行内高亮完全靠它，
/// 因此乱序、重叠、越界三种脏数据都必须有确定行为，视图不用再兜底。
/// </summary>
[TestFixture]
public sealed class CommandTitleSegmentTests
{
    [Test]
    public void FromRanges_EmptyTitle_ReturnsNoSegments()
    {
        Assert.That(
            CommandTitleSegment.FromRanges(string.Empty, new[] { new TextMatchRange(0, 1) }),
            Is.Empty,
            "空标题没有可切的内容");
        Assert.That(CommandTitleSegment.FromRanges(null, null), Is.Empty, "null 标题同样返回空数组");
    }

    [Test]
    public void FromRanges_NoRanges_ReturnsSinglePlainSegment()
    {
        var segments = CommandTitleSegment.FromRanges("日志", CommandMatch.NoHighlights);

        Assert.That(segments.Count, Is.EqualTo(1), "没有命中区间时整段都不高亮");
        Assert.That(segments[0], Is.EqualTo(new CommandTitleSegment("日志", false)));
    }

    [Test]
    public void FromRanges_HighlightInMiddle_ReturnsThreeSegments()
    {
        var segments = CommandTitleSegment.FromRanges("打开日志目录", new[] { new TextMatchRange(2, 2) });

        Assert.That(segments.Select(segment => segment.Text), Is.EqualTo(new[] { "打开", "日志", "目录" }));
        Assert.That(segments.Select(segment => segment.IsHighlighted), Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void FromRanges_UnsortedRanges_AreSortedByPosition()
    {
        var segments = CommandTitleSegment.FromRanges(
            "abcdef",
            new[] { new TextMatchRange(4, 2), new TextMatchRange(0, 1) });

        Assert.That(segments.Select(segment => segment.Text), Is.EqualTo(new[] { "a", "bcd", "ef" }));
        Assert.That(segments.Select(segment => segment.IsHighlighted), Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void FromRanges_OverlappingRanges_DoNotRepeatText()
    {
        var segments = CommandTitleSegment.FromRanges(
            "abcd",
            new[] { new TextMatchRange(0, 2), new TextMatchRange(1, 2) });

        Assert.That(
            string.Concat(segments.Select(segment => segment.Text)),
            Is.EqualTo("abcd"),
            "切段既不能丢字符也不能重复字符");
        Assert.That(segments.Select(segment => segment.IsHighlighted), Is.EqualTo(new[] { true, true, false }));
    }

    [Test]
    public void FromRanges_OutOfBoundsAndEmptyRanges_AreClampedOrDropped()
    {
        var segments = CommandTitleSegment.FromRanges(
            "abc",
            new[] { new TextMatchRange(1, 99), new TextMatchRange(5, 1), new TextMatchRange(0, 0) });

        Assert.That(string.Concat(segments.Select(segment => segment.Text)), Is.EqualTo("abc"), "越界区间被裁到标题末尾");
        Assert.That(segments.Count, Is.EqualTo(2), "起点越界与长度为 0 的区间直接丢弃");
        Assert.That(segments[0], Is.EqualTo(new CommandTitleSegment("a", false)));
        Assert.That(segments[1], Is.EqualTo(new CommandTitleSegment("bc", true)));
    }
}
