using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace CkanUI.App.Converters;

/// <summary>
/// 图片路径 → 缩略图（主菜单「游戏截图」页签）。
/// </summary>
/// <remarks>
/// <para>KSP 截图常见 1920×1080 甚至更大，原图直接绑到界面上会白吃几十 MB 显存，
/// 因此统一按 <see cref="ThumbnailWidth"/> 降采样；解码失败（损坏文件、无权限）返回 null，
/// 模板会退化成占位块，而不是让整个列表绑定炸掉。</para>
/// <para>缓存键带文件修改时间：截图被覆盖写入后会自动重新解码，不用等重启。</para>
/// </remarks>
public sealed class PathToThumbnailConverter : IValueConverter
{
    /// <summary>默认缩略图宽度：卡片宽度 ~320 DIP，2 倍屏下仍然清晰。</summary>
    private const int ThumbnailWidth = 640;

    /// <summary>允许的目标宽度范围，防止写成 0 或离谱值把解码拖死。</summary>
    private const int MinWidth = 16;

    /// <summary>允许的目标宽度上限。</summary>
    private const int MaxWidth = 1024;

    /// <summary>缓存上限：一屏 12 张 × 几页，够用且不会无限增长。</summary>
    private const int MaxEntries = 64;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> CacheOrder = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0)
        {
            return null;
        }

        var width = ResolveWidth(parameter);
        string key;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            key = path + "|" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + width.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        Bitmap? bitmap = null;
        try
        {
            using var stream = File.OpenRead(path);
            bitmap = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception)
        {
            bitmap = null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var existing))
            {
                // 并发解码同一张图：留先到的那份，后来者丢弃。
                return existing;
            }

            if (CacheOrder.Count >= MaxEntries && CacheOrder.Count > 0)
            {
                // 只把键移出索引，不 Dispose：已绑到控件上的位图可能还在渲染。
                Cache.Remove(CacheOrder.Dequeue());
            }

            Cache[key] = bitmap;
            CacheOrder.Enqueue(key);
            return bitmap;
        }
    }

    /// <summary>从 <c>ConverterParameter</c> 取目标宽度；不传就用默认值。</summary>
    private static int ResolveWidth(object? parameter)
    {
        var width = ThumbnailWidth;
        if (parameter is not null)
        {
            if (parameter is int direct)
            {
                width = direct;
            }
            else if (int.TryParse(global::System.Convert.ToString(parameter, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                width = parsed;
            }
        }

        return Math.Clamp(width, MinWidth, MaxWidth);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("图片转换仅支持单向绑定。");
}
