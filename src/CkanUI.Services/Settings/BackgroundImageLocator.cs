namespace CkanUI.Services.Settings;

/// <summary>
/// 「游戏截图」背景来源的定位器（20 篇 §9.1）：在 <c>&lt;GameDir&gt;/Screenshots</c> 里挑最新的一张图。
/// </summary>
/// <remarks>
/// 只碰文件系统、不引用 CKAN 与 Avalonia，所以能 headless 测。找不到目录、目录为空、
/// 没有受支持的图片、或读目录时被拒（权限/占用）一律返回 <c>null</c> ——
/// 背景层的既定策略是「读不出图就回到无背景」，静默降级比弹错误框合适。
/// </remarks>
public static class BackgroundImageLocator
{
    /// <summary>KSP 存截图的子目录名（相对游戏根目录）。</summary>
    public const string ScreenshotsFolderName = "Screenshots";

    /// <summary>认得的图片扩展名（Avalonia 的 <c>Bitmap</c> 能解码的常见位图）。</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    /// <summary>取最新一张截图；没有可用截图时返回 <c>null</c>。</summary>
    /// <param name="gameDirectory">游戏根目录；为空或不存在时返回 <c>null</c>。</param>
    public static string? FindLatestScreenshot(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        try
        {
            var folder = Path.Combine(gameDirectory, ScreenshotsFolderName);
            if (!Directory.Exists(folder))
            {
                return null;
            }

            string? best = null;
            var bestStamp = DateTime.MinValue;
            foreach (var candidate in Directory.EnumerateFiles(folder))
            {
                if (!IsSupported(candidate))
                {
                    continue;
                }

                DateTime stamp;
                try
                {
                    stamp = File.GetLastWriteTimeUtc(candidate);
                }
                catch (Exception)
                {
                    // 单个文件读不到时间戳（被占用/正被写入）就跳过，别让整张背景消失。
                    continue;
                }

                if (best is null || stamp > bestStamp || (stamp == bestStamp && string.CompareOrdinal(candidate, best) > 0))
                {
                    best = candidate;
                    bestStamp = stamp;
                }
            }

            return best;
        }
        catch (Exception)
        {
            // DirectoryNotFoundException / UnauthorizedAccessException / IOException 都算「没有截图」。
            return null;
        }
    }

    /// <summary>扩展名是否属于受支持的位图。</summary>
    public static bool IsSupported(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        foreach (var supported in SupportedExtensions)
        {
            if (string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
