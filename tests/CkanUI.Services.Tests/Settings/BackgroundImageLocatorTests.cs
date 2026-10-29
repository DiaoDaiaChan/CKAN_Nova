using CkanUI.Services.Settings;
using NUnit.Framework;

namespace CkanUI.Services.Tests.Settings;

/// <summary>
/// 20 篇 §9.1「背景来源 = 游戏截图」的定位器契约测试：
/// 只认 <c>Screenshots</c> 目录里的位图、取最新一张、任何读盘失败都退化成「没有截图」。
/// </summary>
/// <remarks>
/// 全部用临时目录造真实文件（不打桩文件系统），因为这里要验证的正是文件系统语义。
/// 时间先后用手工 <c>File.SetLastWriteTimeUtc</c> 固定，不依赖创建顺序。
/// </remarks>
[TestFixture]
public sealed class BackgroundImageLocatorTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ckanui-bg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不该让用例变红（Windows 上偶发的句柄延迟释放）。
        }
    }

    [Test]
    public void FindLatestScreenshot_PicksTheNewestSupportedImage()
    {
        var folder = EnsureScreenshotsFolder();
        WriteImage(folder, "old.png", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteImage(folder, "new.jpg", new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));

        var found = BackgroundImageLocator.FindLatestScreenshot(_root);

        Assert.That(found, Is.EqualTo(Path.Combine(folder, "new.jpg")));
    }

    [Test]
    public void FindLatestScreenshot_IgnoresUnsupportedExtensions()
    {
        var folder = EnsureScreenshotsFolder();
        WriteImage(folder, "shot.png", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(folder, "notes.txt", new DateTime(2024, 9, 9, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(folder, "shot.png.bak", new DateTime(2024, 9, 9, 0, 0, 0, DateTimeKind.Utc));

        var found = BackgroundImageLocator.FindLatestScreenshot(_root);

        Assert.That(found, Is.EqualTo(Path.Combine(folder, "shot.png")));
    }

    [Test]
    public void FindLatestScreenshot_ReturnsNullWhenFolderIsMissing()
    {
        Assert.That(BackgroundImageLocator.FindLatestScreenshot(_root), Is.Null);
    }

    [Test]
    public void FindLatestScreenshot_ReturnsNullWhenFolderHasNoImages()
    {
        var folder = EnsureScreenshotsFolder();
        WriteFile(folder, "readme.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(BackgroundImageLocator.FindLatestScreenshot(_root), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void FindLatestScreenshot_ReturnsNullForBlankGameDirectory(string? gameDirectory)
    {
        Assert.That(BackgroundImageLocator.FindLatestScreenshot(gameDirectory), Is.Null);
    }

    [Test]
    public void FindLatestScreenshot_UsesOrdinalTieBreakWhenTimestampsMatch()
    {
        var folder = EnsureScreenshotsFolder();
        var stamp = new DateTime(2024, 3, 3, 3, 3, 3, DateTimeKind.Utc);
        WriteImage(folder, "a.png", stamp);
        WriteImage(folder, "b.png", stamp);

        var found = BackgroundImageLocator.FindLatestScreenshot(_root);

        // 时间戳完全相同时按序数比较取字典序更大的一个，保证结果稳定（不会每次换一张）。
        Assert.That(found, Is.EqualTo(Path.Combine(folder, "b.png")));
    }

    [Test]
    public void FindLatestScreenshot_DoesNotLookOutsideScreenshotsFolder()
    {
        WriteImage(_root, "outside.png", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(BackgroundImageLocator.FindLatestScreenshot(_root), Is.Null);
    }

    [TestCase("shot.png", true)]
    [TestCase("shot.PNG", true)]
    [TestCase("shot.jpeg", true)]
    [TestCase("shot.webp", true)]
    [TestCase("shot.bmp", true)]
    [TestCase("shot.gif", false)]
    [TestCase("shot.png.txt", false)]
    [TestCase("noext", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsSupported_ChecksTheExtensionCaseInsensitively(string? path, bool expected)
    {
        Assert.That(BackgroundImageLocator.IsSupported(path), Is.EqualTo(expected));
    }

    [Test]
    public void IsSupported_HandlesDirectoriesAndDotsUpFront()
    {
        Assert.That(BackgroundImageLocator.IsSupported(".png"), Is.True, "只有扩展名也算受支持：Path.GetExtension 返回 .png。");
        Assert.That(BackgroundImageLocator.IsSupported(Path.Combine(_root, ".hidden")), Is.False);
    }

    private string EnsureScreenshotsFolder()
    {
        var folder = Path.Combine(_root, BackgroundImageLocator.ScreenshotsFolderName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void WriteImage(string folder, string name, DateTime stamp) => WriteFile(folder, name, stamp);

    private static void WriteFile(string folder, string name, DateTime stamp)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        File.SetLastWriteTimeUtc(path, stamp);
    }
}
