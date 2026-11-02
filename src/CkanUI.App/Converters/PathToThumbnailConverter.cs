using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CkanUI.Services.Assets;

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
            bitmap = DecodeBitmap(path, width);
        }
        catch (Exception)
        {
            bitmap = null;
        }

        Store(key, bitmap);
        lock (Cache)
        {
            // 并发解码同一张图：留先到的那份，后来者丢弃。
            return Cache.TryGetValue(key, out var stored) ? stored : bitmap;
        }
    }

    /// <summary>
    /// 预热一批缩略图：在后台线程把图解码进缓存，等界面真正绑定时直接命中。
    /// </summary>
    /// <remarks>
    /// 解码是同步的（<see cref="Bitmap.DecodeToWidth"/>），绑定时在 UI 线程上跑；旗帜页 50 多张、
    /// 载具页几十张，一次性绑完就是几百毫秒的卡顿。预热把这些解码挪到后台，绑定只做字典命中。
    /// </remarks>
    public static void Prewarm(IEnumerable<string?> paths, int width = ThumbnailWidth)
    {
        var targets = paths
            .Where(static path => !string.IsNullOrEmpty(path))
            .Select(static path => path!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            foreach (var path in targets)
            {
                try
                {
                    DecodeIntoCache(path, width);
                }
                catch (Exception)
                {
                    // 预热失败留给绑定时按原路径重试：损坏文件、瞬时占用都要保持原有降级行为。
                }
            }
        });
    }

    /// <summary>解码并写入缓存；已命中则跳过。</summary>
    private static void DecodeIntoCache(string path, int width)
    {
        if (TryBuildKey(path, width) is not { } key)
        {
            return;
        }

        lock (Cache)
        {
            if (Cache.ContainsKey(key))
            {
                return;
            }
        }

        Store(key, DecodeBitmap(path, width));
    }

    /// <summary>缓存键：路径 + 修改时间 + 目标宽度（原图被覆盖后会自然失效）。</summary>
    private static string? TryBuildKey(string path, int width)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? path + "|" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + width.ToString(CultureInfo.InvariantCulture)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>按扩展名选解码器：DDS 走自研块解压，其余交给 Avalonia。</summary>
    private static Bitmap? DecodeBitmap(string path, int width)
    {
        // 停用的加载图带 .disabled 后缀，解码要按真实扩展名判断，否则缩略图会整片空掉。
        var effective = path.EndsWith(LoadingImageService.DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? path[..^LoadingImageService.DisabledSuffix.Length]
            : path;

        return string.Equals(Path.GetExtension(effective), ".dds", StringComparison.OrdinalIgnoreCase)
            ? DecodeDds(path, width)
            : DecodeWithAvalonia(path, width);
    }

    /// <summary>写入缓存并按上限淘汰最老的键。</summary>
    private static void Store(string key, Bitmap? bitmap)
    {
        lock (Cache)
        {
            if (Cache.ContainsKey(key))
            {
                return;
            }

            if (CacheOrder.Count >= MaxEntries && CacheOrder.Count > 0)
            {
                // 只把键移出索引，不 Dispose：已绑到控件上的位图可能还在渲染。
                Cache.Remove(CacheOrder.Dequeue());
            }

            Cache[key] = bitmap;
            CacheOrder.Enqueue(key);
        }
    }

    private static Bitmap? DecodeWithAvalonia(string path, int width)
    {
        using var stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
    }

    private static Bitmap? DecodeDds(string path, int width)
    {
        var image = DdsDecoder.TryDecode(path, width);
        if (image is null)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
        {
            var row = image.Width * 4;
            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(image.Pixels, y * row, buffer.Address + (y * buffer.RowBytes), row);
            }
        }

        return bitmap;
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
