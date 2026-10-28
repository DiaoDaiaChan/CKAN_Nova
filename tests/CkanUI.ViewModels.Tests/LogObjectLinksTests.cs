using System.Linq;
using CkanUI.ViewModels.Pages.Logs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>日志行里的对象联动识别（14 篇 §4.3）。</summary>
[TestFixture]
public sealed class LogObjectLinksTests
{
    [TestCase("installed mod:RealChute successfully")]
    [TestCase("mod=RealChute")]
    [TestCase("MOD:RealChute")]
    public void Detect_RecognisesModReference(string message)
    {
        var link = LogObjectLinks.Detect(message);

        Assert.That(link, Is.Not.Null);
        Assert.That(link!.Kind, Is.EqualTo(LogLinkKind.Mod));
        Assert.That(link.Value, Is.EqualTo("RealChute"));
        Assert.That(link.Text, Is.EqualTo("查看模组"));
    }

    [Test]
    public void Detect_KeepsDotsAndDashesInModIdentifier()
    {
        var link = LogObjectLinks.Detect("failed mod:RealChute.1-2");

        Assert.That(link!.Value, Is.EqualTo("RealChute.1-2"));
    }

    [Test]
    public void Detect_DoesNotMatchModInsideAnotherWord()
    {
        Assert.That(LogObjectLinks.Detect("commit=abc123"), Is.Null);
        Assert.That(LogObjectLinks.Detect("node_modules=42"), Is.Null);
    }

    [Test]
    public void Detect_RecognisesInstanceReference()
    {
        var link = LogObjectLinks.Detect("switching instance:主力存档");

        Assert.That(link!.Kind, Is.EqualTo(LogLinkKind.Instance));
        Assert.That(link.Value, Is.EqualTo("主力存档"));
        Assert.That(link.Text, Is.EqualTo("切换实例"));
    }

    [Test]
    public void Detect_RecognisesUrlAndTrimsTrailingPunctuation()
    {
        var link = LogObjectLinks.Detect("see https://spacedock.info/mod/123.");

        Assert.That(link!.Kind, Is.EqualTo(LogLinkKind.Url));
        Assert.That(link.Value, Is.EqualTo("https://spacedock.info/mod/123"));
        Assert.That(link.Text, Is.EqualTo("打开链接"));
    }

    [Test]
    public void Detect_RecognisesInstallTaskReference()
    {
        var link = LogObjectLinks.Detect("task:install-42 failed");

        Assert.That(link!.Kind, Is.EqualTo(LogLinkKind.InstallTask));
        Assert.That(link.Value, Is.EqualTo("install-42"));
        Assert.That(link.Text, Is.EqualTo("重试安装"));
    }

    [Test]
    public void Detect_PrefersModOverOtherKinds()
    {
        var link = LogObjectLinks.Detect("mod:RealChute instance:Main https://example.com task:t1");

        Assert.That(link!.Kind, Is.EqualTo(LogLinkKind.Mod));
    }

    [Test]
    public void DetectAll_ReturnsEveryKindInPriorityOrder()
    {
        var links = LogObjectLinks.DetectAll("mod:RealChute instance:Main https://example.com task:t1");

        Assert.That(links.Select(link => link.Kind), Is.EqualTo(new[]
        {
            LogLinkKind.Mod,
            LogLinkKind.Instance,
            LogLinkKind.Url,
            LogLinkKind.InstallTask,
        }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("nothing to link here")]
    public void DetectAll_ReturnsEmptyWhenNothingToLink(string? message) =>
        Assert.That(LogObjectLinks.DetectAll(message), Is.Empty);

    [Test]
    public void Detect_ReturnsNullWhenNothingToLink() => Assert.That(LogObjectLinks.Detect("plain text"), Is.Null);
}
