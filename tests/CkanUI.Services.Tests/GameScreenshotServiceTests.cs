using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Instances;
using CkanUI.Services.Settings;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 游戏截图枚举（主菜单「游戏截图」页签的数据源）：目录缺失、扩展名过滤、按拍摄时间倒序、张数上限。
/// </summary>
/// <remarks>
/// 口径：在「什么算截图」上必须与背景层共用 <see cref="BackgroundImageLocator"/>，
/// 否则同一份 <c>Screenshots</c> 目录会出现「背景用了、主菜单没列」的分裂。
/// 目录缺失按「这个实例还没截过图」处理（Ok + 空），只有整目录读不动才算失败。
/// </remarks>
[TestFixture]
public sealed class GameScreenshotServiceTests
{
    private TempWorkspace? _workspace;
    private string _gameDir = string.Empty;
    private string _screenshotDir = string.Empty;
    private readonly GameScreenshotService _service = new();

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("screenshots");
        _gameDir = _workspace.Path;
        _screenshotDir = _workspace.Combine(BackgroundImageLocator.ScreenshotsFolderName);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    [Test]
    public void GetScreenshotsDirectory_JoinsScreenshotsSubfolder()
    {
        Assert.That(_service.GetScreenshotsDirectory(_gameDir), Is.EqualTo(_screenshotDir));
        Assert.That(_service.GetScreenshotsDirectory(""), Is.Empty);
    }

    [Test]
    public async Task ListAsync_MissingFolder_ReturnsEmptySuccess()
    {
        var result = await _service.ListAsync(_gameDir);

        Assert.That(result.IsSuccess, Is.True, "没截过图不是错误，空态文案由页面自己给。");
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task ListAsync_EmptyGameDir_ReturnsEmptySuccess()
    {
        var result = await _service.ListAsync("   ");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task ListAsync_IgnoresUnsupportedFiles_AndSortsByCapturedAtDescending()
    {
        Directory.CreateDirectory(_screenshotDir);
        var older = WriteShot("older.png", new DateTime(2024, 4, 1, 8, 0, 0, DateTimeKind.Utc), 2048);
        var newer = WriteShot("newer.jpg", new DateTime(2024, 5, 2, 9, 30, 0, DateTimeKind.Utc), 4096);
        File.WriteAllText(Path.Combine(_screenshotDir, "notes.txt"), "不是截图");

        var result = await _service.ListAsync(_gameDir);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(item => item.FileName), Is.EqualTo(new[] { "newer.jpg", "older.png" }));
        Assert.That(result.Value![0].FilePath, Is.EqualTo(newer));
        Assert.That(result.Value![0].Bytes, Is.EqualTo(4096));
        Assert.That(result.Value![1].FilePath, Is.EqualTo(older));
    }

    [Test]
    public async Task ListAsync_Limit_KeepsOnlyNewest()
    {
        Directory.CreateDirectory(_screenshotDir);
        for (var index = 0; index < 5; index++)
        {
            WriteShot($"shot{index}.png", new DateTime(2024, 6, 1, 0, index, 0, DateTimeKind.Utc), 1024 + index);
        }

        var result = await _service.ListAsync(_gameDir, limit: 2);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(2));
        Assert.That(result.Value!.Select(item => item.FileName), Is.EqualTo(new[] { "shot4.png", "shot3.png" }));
    }

    [Test]
    public async Task ListAsync_NonPositiveLimit_FallsBackToDefault()
    {
        Directory.CreateDirectory(_screenshotDir);
        for (var index = 0; index < GameScreenshotService.DefaultLimit + 3; index++)
        {
            WriteShot($"bulk{index:D2}.png", new DateTime(2024, 7, 1, 0, 0, index, DateTimeKind.Utc), 1024);
        }

        var result = await _service.ListAsync(_gameDir, limit: 0);

        Assert.That(result.Value, Has.Count.EqualTo(GameScreenshotService.DefaultLimit));
    }

    private string WriteShot(string name, DateTime stampUtc, int bytes)
    {
        var path = Path.Combine(_screenshotDir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, stampUtc);
        return path;
    }
}
