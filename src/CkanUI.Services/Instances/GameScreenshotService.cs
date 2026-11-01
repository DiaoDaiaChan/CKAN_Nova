using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Settings;

namespace CkanUI.Services.Instances;

/// <summary>
/// 游戏截图服务实现：纯文件系统枚举，不碰 CKAN、不碰 Avalonia，因此能 headless 测。
/// </summary>
/// <remarks>
/// 目录不存在、图片删到一半、单个文件读不到时间戳都按「这张不算」处理；
/// 只有整个目录读不动（权限/占用）才返回 <c>Fail</c>，让主菜单能提示而不是把失败伪装成「没有截图」。
/// </remarks>
public sealed class GameScreenshotService : IGameScreenshotService
{
    /// <summary>默认返回张数。</summary>
    public const int DefaultLimit = 12;

    /// <summary>返回张数上限（防止实例里堆了几百张截图时把页面撑爆）。</summary>
    public const int MaxLimit = 60;

    /// <inheritdoc />
    public string GetScreenshotsDirectory(string gameDir)
        => string.IsNullOrWhiteSpace(gameDir)
            ? string.Empty
            : Path.Combine(gameDir, BackgroundImageLocator.ScreenshotsFolderName);

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<GameScreenshotInfo>>> ListAsync(
        string gameDir,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return Task.FromResult(OperationResult<IReadOnlyList<GameScreenshotInfo>>.Ok(Array.Empty<GameScreenshotInfo>()));
        }

        var folder = GetScreenshotsDirectory(gameDir);
        if (!Directory.Exists(folder))
        {
            return Task.FromResult(OperationResult<IReadOnlyList<GameScreenshotInfo>>.Ok(Array.Empty<GameScreenshotInfo>()));
        }

        var take = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        try
        {
            var found = new List<GameScreenshotInfo>();
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!BackgroundImageLocator.IsSupported(path))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    found.Add(new GameScreenshotInfo(
                        info.FullName,
                        info.Name,
                        info.Length,
                        new DateTimeOffset(info.LastWriteTimeUtc)));
                }
                catch (Exception)
                {
                    // 单张图被占用/正被写入：跳过这张，别让整个页签空掉。
                }
            }

            var ordered = found
                .OrderByDescending(item => item.CapturedAt)
                .ThenByDescending(item => item.FileName, StringComparer.Ordinal)
                .Take(take)
                .ToArray();

            return Task.FromResult(OperationResult<IReadOnlyList<GameScreenshotInfo>>.Ok(ordered));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<GameScreenshotInfo>>.Ok(Array.Empty<GameScreenshotInfo>()));
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<GameScreenshotInfo>>.Fail(
                "读取截图目录失败：" + exception.Message,
                folder,
                retryable: true));
        }
    }
}
