using System.Globalization;

using Avalonia.Media.Imaging;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 背景图片解码（20 篇 P-06 §14 的内存预算）：只按 1600 像素宽解码，
/// 1600×900×4 ≈ 5.8 MB，稳稳落在「单张位图 ≤8 MB」的预算里；原图多大都不影响。
/// </summary>
public static class BackgroundImageLoader
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(BackgroundImageLoader));

    /// <summary>解码目标宽度。想改大请先重算内存预算（宽度 × 高度 × 4 字节）。</summary>
    public const int DecodeWidth = 1600;

    /// <summary>
    /// 读一张背景图；路径为空、文件不存在、格式不认识都返回 <c>null</c> 并把原因写进 <paramref name="error"/>。
    /// 背景图坏掉不该把界面拖垮，所以这里吞掉异常只记日志。
    /// </summary>
    public static Bitmap? Load(string? path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "未设置背景图片路径。";
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                error = string.Create(CultureInfo.InvariantCulture, $"找不到背景图片：{path}");
                return null;
            }

            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, DecodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception ex)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"背景图片读取失败：{ex.Message}");
            Log.Warn($"背景图片读取失败：{path}", ex);
            return null;
        }
    }
}
