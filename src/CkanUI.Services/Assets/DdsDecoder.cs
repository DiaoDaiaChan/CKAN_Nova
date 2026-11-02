using System.Buffers.Binary;
namespace CkanUI.Services.Assets;

/// <summary>解码后的一张 DDS：像素按 BGRA8888 排列，可直接喂给 <c>WriteableBitmap</c>。</summary>
/// <param name="Width">宽。</param>
/// <param name="Height">高。</param>
/// <param name="Pixels">BGRA8888 像素，长度 = Width × Height × 4。</param>
public sealed record DdsImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// 最小 DDS 解码器（纯 BCL，无第三方依赖）。
/// </summary>
/// <remarks>
/// <para>KSP 的加载图替换模组（ROLoadingImages）只用 DDS，且实测一台 RP-1 安装的 72 张全是 DXT1；
/// <c>LoadingImageReplacer</c> 自己实现了解压，支持 DXT1/DXT5/A8/RGB24/BGR24/RGBA32/RGB565/ARGB4444/RGBA4444，
/// 这里覆盖其中最常见的块压缩（DXT1/DXT3/DXT5）与未压缩的 32/24/8 位。</para>
/// <para>只解第一层（不读 mip 链）：缩略图用不到 mip，跳过可以把 2560×1440 的解码控制在十几毫秒。</para>
/// </remarks>
public static class DdsDecoder
{
    private const int HeaderLength = 128;

    private const uint FourCcFlag = 0x00000004;
    private const uint RgbFlag = 0x00000040;
    private const uint AlphaPixelsFlag = 0x00000001;

    /// <summary>解码文件；不支持的格式或损坏文件返回 <see langword="null"/>，不抛异常。</summary>
    /// <param name="path">DDS 路径。</param>
    /// <param name="maxWidth">目标宽度上限（等比缩到不超过它），0 表示不缩放。</param>
    public static DdsImage? TryDecode(string path, int maxWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Decode(stream, maxWidth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>从流里解码一张 DDS；格式不支持返回 <see langword="null"/>。</summary>
    /// <param name="stream">可读流。</param>
    /// <param name="maxWidth">目标宽度上限，0 表示不缩放。</param>
    public static DdsImage? Decode(Stream stream, int maxWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderLength];
        if (stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) < HeaderLength)
        {
            return null;
        }

        if (header[0] != (byte)'D' || header[1] != (byte)'D' || header[2] != (byte)'S' || header[3] != (byte)' ')
        {
            return null;
        }

        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
        {
            return null;
        }

        var pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80));
        var fourCc = System.Text.Encoding.ASCII.GetString(header, 84, 4);
        var bitsPerPixel = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(88));
        var redMask = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(92));
        var greenMask = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(96));
        var blueMask = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(100));
        var alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(104));

        var data = ReadRemaining(stream);
        var full = (pixelFlags & FourCcFlag) != 0
            ? DecodeFourCc(fourCc, data, width, height, maxWidth)
            : DecodeUncompressed(pixelFlags, bitsPerPixel, redMask, greenMask, blueMask, alphaMask, data, width, height);
        if (full is null)
        {
            return null;
        }

        return maxWidth > 0 && full.Width > maxWidth ? Downscale(full, maxWidth) : full;
    }

    private static DdsImage? DecodeFourCc(string fourCc, byte[] data, int width, int height, int maxWidth)
    {
        var (blockBytes, kind) = fourCc switch
        {
            "DXT1" => (8, BlockKind.Dxt1),
            "DXT3" => (16, BlockKind.Dxt3),
            "DXT5" => (16, BlockKind.Dxt5),
            _ => (0, BlockKind.Unsupported),
        };
        if (blockBytes == 0)
        {
            return null;
        }

        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        var required = (long)blocksWide * blocksHigh * blockBytes;
        if (data.LongLength < required)
        {
            return null;
        }

        var colors = new byte[16][];
        var alpha = new byte[16];
        // 缩略图只要目标宽度：按输出像素反查所在块，跳过绝大多数块。2560 宽的加载图从 65ms 掉到个位数。
        return maxWidth > 0 && width > maxWidth
            ? DecodeFourCcScaled(data, width, height, maxWidth, blocksWide, blockBytes, kind, colors, alpha)
            : DecodeFourCcFull(data, width, height, blocksWide, blocksHigh, blockBytes, kind, colors, alpha);
    }

    private static DdsImage DecodeFourCcFull(
        byte[] data,
        int width,
        int height,
        int blocksWide,
        int blocksHigh,
        int blockBytes,
        BlockKind kind,
        byte[][] colors,
        byte[] alpha)
    {
        var pixels = new byte[width * height * 4];
        var offset = 0;
        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                var hasAlpha = DecodeBlock(data, offset, kind, colors, alpha);
                for (var py = 0; py < 4; py++)
                {
                    var y = (by * 4) + py;
                    if (y >= height)
                    {
                        break;
                    }

                    for (var px = 0; px < 4; px++)
                    {
                        var x = (bx * 4) + px;
                        if (x >= width)
                        {
                            break;
                        }

                        WritePixel(pixels, ((y * width) + x) * 4, colors[(py * 4) + px], hasAlpha, alpha[(py * 4) + px]);
                    }
                }

                offset += blockBytes;
            }
        }

        return new DdsImage(width, height, pixels);
    }

    private static DdsImage DecodeFourCcScaled(
        byte[] data,
        int width,
        int height,
        int maxWidth,
        int blocksWide,
        int blockBytes,
        BlockKind kind,
        byte[][] colors,
        byte[] alpha)
    {
        var outWidth = maxWidth;
        var outHeight = Math.Max(1, (int)((long)height * outWidth / width));
        var pixels = new byte[outWidth * outHeight * 4];
        var cachedBlock = -1;
        var hasAlpha = false;
        for (var oy = 0; oy < outHeight; oy++)
        {
            var sy = (int)((long)oy * height / outHeight);
            var by = sy / 4;
            for (var ox = 0; ox < outWidth; ox++)
            {
                var sx = (int)((long)ox * width / outWidth);
                var block = (by * blocksWide) + (sx / 4);
                if (block != cachedBlock)
                {
                    hasAlpha = DecodeBlock(data, block * blockBytes, kind, colors, alpha);
                    cachedBlock = block;
                }

                var index = ((sy % 4) * 4) + (sx % 4);
                WritePixel(pixels, ((oy * outWidth) + ox) * 4, colors[index], hasAlpha, alpha[index]);
            }
        }

        return new DdsImage(outWidth, outHeight, pixels);
    }

    private static void WritePixel(byte[] pixels, int target, byte[] color, bool hasAlpha, byte alphaValue)
    {
        pixels[target] = color[0];
        pixels[target + 1] = color[1];
        pixels[target + 2] = color[2];
        pixels[target + 3] = hasAlpha ? alphaValue : color[3];
    }

    /// <summary>解一个块的颜色（+DXT3/DXT5 的 alpha）。</summary>
    private static bool DecodeBlock(byte[] data, int offset, BlockKind kind, byte[][] colors, byte[] alpha)
    {
        switch (kind)
        {
            case BlockKind.Dxt3:
                ReadDxt3Alpha(data, offset, alpha);
                DecodeColorBlock(data, offset + 8, forceFourColor: true, colors);
                return true;
            case BlockKind.Dxt5:
                ReadDxt5Alpha(data, offset, alpha);
                DecodeColorBlock(data, offset + 8, forceFourColor: true, colors);
                return true;
            default:
                DecodeColorBlock(data, offset, forceFourColor: false, colors);
                return false;
        }
    }

    /// <summary>解一个 4×4 颜色块，按 2 位索引摊成 16 个 BGRA 像素。</summary>
    private static void DecodeColorBlock(byte[] data, int offset, bool forceFourColor, byte[][] colors)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2));
        var indices = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));

        var palette = new byte[4][];
        palette[0] = FromRgb565(c0);
        palette[1] = FromRgb565(c1);
        if (c0 > c1 || forceFourColor)
        {
            palette[2] =
            [
                (byte)(((2 * palette[0][0]) + palette[1][0]) / 3),
                (byte)(((2 * palette[0][1]) + palette[1][1]) / 3),
                (byte)(((2 * palette[0][2]) + palette[1][2]) / 3),
                255,
            ];
            palette[3] =
            [
                (byte)((palette[0][0] + (2 * palette[1][0])) / 3),
                (byte)((palette[0][1] + (2 * palette[1][1])) / 3),
                (byte)((palette[0][2] + (2 * palette[1][2])) / 3),
                255,
            ];
        }
        else
        {
            palette[2] =
            [
                (byte)((palette[0][0] + palette[1][0]) / 2),
                (byte)((palette[0][1] + palette[1][1]) / 2),
                (byte)((palette[0][2] + palette[1][2]) / 2),
                255,
            ];
            palette[3] = [0, 0, 0, 0];
        }

        for (var i = 0; i < 16; i++)
        {
            colors[i] = palette[(indices >> (2 * i)) & 0x3];
        }
    }

    private static byte[] FromRgb565(ushort value)
    {
        var r = (byte)((value >> 11) & 0x1F);
        var g = (byte)((value >> 5) & 0x3F);
        var b = (byte)(value & 0x1F);
        // 调用方按 BGRA 写像素，这里就按 B/G/R/A 出数组，别在里面再翻一次。
        return
        [
            (byte)((b * 255) / 31),
            (byte)((g * 255) / 63),
            (byte)((r * 255) / 31),
            255,
        ];
    }

    private static void ReadDxt3Alpha(byte[] data, int offset, byte[] alpha)
    {
        for (var i = 0; i < 16; i++)
        {
            var nibble = (data[offset + (i / 2)] >> ((i % 2) * 4)) & 0x0F;
            alpha[i] = (byte)((nibble * 255) / 15);
        }
    }

    private static void ReadDxt5Alpha(byte[] data, int offset, byte[] alpha)
    {
        var a0 = data[offset];
        var a1 = data[offset + 1];
        Span<byte> palette = stackalloc byte[8];
        palette[0] = a0;
        palette[1] = a1;
        if (a0 > a1)
        {
            for (var i = 1; i <= 6; i++)
            {
                palette[i + 1] = (byte)((((7 - i) * a0) + (i * a1)) / 7);
            }
        }
        else
        {
            for (var i = 1; i <= 4; i++)
            {
                palette[i + 1] = (byte)((((5 - i) * a0) + (i * a1)) / 5);
            }

            palette[6] = 0;
            palette[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)data[offset + 2 + i] << (8 * i);
        }

        for (var i = 0; i < 16; i++)
        {
            alpha[i] = palette[(int)((bits >> (3 * i)) & 0x7)];
        }
    }

    private static DdsImage? DecodeUncompressed(uint pixelFlags, uint bitsPerPixel, uint redMask, uint greenMask, uint blueMask, uint alphaMask, byte[] data, int width, int height)
    {
        if (bitsPerPixel is not (8 or 16 or 24 or 32))
        {
            return null;
        }

        var stride = width * (int)bitsPerPixel / 8;
        if (stride <= 0 || (long)stride * height > data.LongLength)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = (y * stride) + (x * (int)bitsPerPixel / 8);
                var target = ((y * width) + x) * 4;
                if (bitsPerPixel == 8)
                {
                    pixels[target] = data[source];
                    pixels[target + 1] = data[source];
                    pixels[target + 2] = data[source];
                    pixels[target + 3] = 255;
                    continue;
                }

                var value = bitsPerPixel switch
                {
                    32 => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(source)),
                    24 => (uint)(data[source] | (data[source + 1] << 8) | (data[source + 2] << 16)),
                    _ => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(source)),
                };

                pixels[target] = ExtractChannel(value, blueMask);
                pixels[target + 1] = ExtractChannel(value, greenMask);
                pixels[target + 2] = ExtractChannel(value, redMask);
                pixels[target + 3] = (pixelFlags & AlphaPixelsFlag) != 0 ? ExtractChannel(value, alphaMask) : (byte)255;
            }
        }

        return new DdsImage(width, height, pixels);
    }

    private static byte ExtractChannel(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 255;
        }

        var shift = 0;
        while (((mask >> shift) & 1u) == 0 && shift < 31)
        {
            shift++;
        }

        var bits = 0;
        while (bits < 8 && ((mask >> (shift + bits)) & 1u) != 0)
        {
            bits++;
        }

        var max = (1u << bits) - 1;
        var raw = (value >> shift) & max;
        return (byte)((raw * 255) / max);
    }

    private static byte[] ReadRemaining(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static DdsImage Downscale(DdsImage source, int maxWidth)
    {
        var width = Math.Max(1, maxWidth);
        var height = Math.Max(1, (int)((long)source.Height * width / source.Width));
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)((long)y * source.Height / height));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)((long)x * source.Width / width));
                var from = ((sourceY * source.Width) + sourceX) * 4;
                var to = ((y * width) + x) * 4;
                pixels[to] = source.Pixels[from];
                pixels[to + 1] = source.Pixels[from + 1];
                pixels[to + 2] = source.Pixels[from + 2];
                pixels[to + 3] = source.Pixels[from + 3];
            }
        }

        return new DdsImage(width, height, pixels);
    }

    private enum BlockKind
    {
        Unsupported,
        Dxt1,
        Dxt3,
        Dxt5,
    }
}
