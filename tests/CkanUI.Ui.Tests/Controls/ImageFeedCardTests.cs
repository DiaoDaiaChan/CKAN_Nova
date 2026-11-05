using Avalonia.Controls;
using CkanUI.Ui.Controls;
using NUnit.Framework;

namespace CkanUI.Ui.Tests.Controls;

/// <summary>
/// 图片信息流卡片的构造锁：XAML 没加载时控件只剩空壳，卡片会在列表里静默消失。
/// </summary>
[TestFixture]
public sealed class ImageFeedCardTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessHost.EnsureInitialized();
    }

    [Test]
    public void Constructor_LoadsCardTemplate()
    {
        var card = new ImageFeedCard
        {
            Title = "KerbalOverengineering",
            Subtitle = "rockmeowstar",
            MetaText = "下载 84",
            ImageHeight = 116,
        };

        Assert.Multiple(() =>
        {
            Assert.That(card.Content, Is.Not.Null, "XAML 未加载时卡片只剩空壳");
            Assert.That(card.Content, Is.TypeOf<Border>(), "卡片根节点应是带图片与文字的 Border");
        });
    }
}
