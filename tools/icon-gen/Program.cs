using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace CkanUI.Tools.IconGen;

/// <summary>
/// 应用图标生成器：按设计令牌自绘一枚「轨道 + 模组列表」标记，输出多尺寸 ICO 与预览图。
/// 零第三方依赖（自带 SDF 光栅器与 PNG/ICO 编码器），可重复生成同样的字节，
/// 因此 ckan.ico 的变更可以由代码解释，不依赖任何二进制源文件。
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 128, 256];

    private static int Main(string[] args)
    {
        string repository = FindRepositoryRoot(AppContext.BaseDirectory);
        string assetsDirectory = Path.Combine(repository, "src", "CkanUI.App", "Assets");
        string outputDirectory = args.Length > 0 ? args[0] : assetsDirectory;
        string previewDirectory = Path.Combine(repository, "artifacts", "icon");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(previewDirectory);

        var dark = Sizes.Select(size => Render(size, Palette.Dark)).ToArray();
        var light = Sizes.Select(size => Render(size, Palette.Light)).ToArray();

        var icon = BuildIco(Sizes, dark);
        string iconPath = Path.Combine(outputDirectory, "ckan.ico");
        File.WriteAllBytes(iconPath, icon);

        string masterPath = Path.Combine(outputDirectory, "ckan-256.png");
        File.WriteAllBytes(masterPath, EncodePng(256, dark[^1]));
        string lightPath = Path.Combine(outputDirectory, "ckan-256-light.png");
        File.WriteAllBytes(lightPath, EncodePng(256, light[^1]));
        File.WriteAllBytes(Path.Combine(previewDirectory, "preview-sheet.png"), BuildPreview(dark, light));

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "icon: {0} sizes, {1} bytes -> {2}", Sizes.Length, icon.Length, iconPath));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "icon: preview -> {0}", Path.Combine(previewDirectory, "preview-sheet.png")));
        return 0;
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ckan-ui.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// <summary>把一枚图标光栅化到 RGBA8（直通 alpha）。</summary>
    private static byte[] Render(int size, Palette palette)
    {
        var pixels = new byte[checked(size * size * 4)];
        double pixel = 1d / size;
        double detail = size >= 48 ? 1 : size >= 24 ? 0.55 : 0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 像素中心采样，避免边缘系统性偏移半像素。
                Composite((x + 0.5) * pixel, (y + 0.5) * pixel, pixel, size, palette, detail, out byte r, out byte g, out byte b, out byte a);
                int index = (y * size + x) * 4;
                pixels[index] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = a;
            }
        }

        return pixels;
    }

    private static void Composite(
        double u, double v, double pixel, int size, Palette palette, double detail,
        out byte red, out byte green, out byte blue, out byte alpha)
    {
        // 1. 砖块（圆角方形）：底色上浅下深，贴合卡面 → 页面背景的层次。
        double tile = Coverage(RoundedBoxSignedDistance(u - 0.5, v - 0.5, 0.5, 0.5, 0.225 * size * pixel), pixel);
        if (tile <= 0.001)
        {
            red = green = blue = alpha = 0;
            return;
        }

        (double r, double g, double b) color = Lerp(palette.Top, palette.Bottom, Ease(v));

        // 2. 环境辉光：右下强调色径向渐变，对应 CkanAmbientGlowBrush 的用法。
        double glow = RadialAlpha(u - 0.74, v - 0.78, 0.34);
        color = Over(color, palette.Accent, glow * palette.GlowStrength * tile);

        // 3. 轨道环 + 节点：呼应星空层的轨道线。环只在 ≥32px 出现——16/24 上它退化成一团泥，
        // 不如干净的两条行；节点与星点仍按细节档（≥48px）叠加。
        if (size >= 32)
        {
            const double rotation = -0.35;
            double cosine = Math.Cos(rotation);
            double sine = Math.Sin(rotation);
            double dx = u - 0.5;
            double dy = v - 0.52;
            double eu = dx * cosine - dy * sine;
            double ev = dx * sine + dy * cosine;
            double ringWidth = Math.Max(0.010, 1.1 * pixel);
            double ring = Coverage(Math.Abs(EllipseSignedDistance(eu, ev, 0.335, 0.126)) - ringWidth / 2, pixel);
            color = Over(color, palette.Accent, ring * (detail < 1 ? 0.30 : 0.42) * tile);

            if (detail >= 1)
            {
                double angle = -1.02;
                double nu = 0.335 * Math.Cos(angle) * cosine - 0.126 * Math.Sin(angle) * sine + 0.5;
                double nv = 0.335 * Math.Cos(angle) * sine + 0.126 * Math.Sin(angle) * cosine + 0.52;
                double node = Coverage(CircleSignedDistance(u - nu, v - nv, Math.Max(0.016, 1.7 * pixel)), pixel);
                color = Over(color, palette.AccentBright, node * 0.95 * tile);

                // 4. 星点：星空层的语汇，只在大尺寸出现，16/24 上不糊成脏点。
                foreach ((double su, double sv, double sr, double sa) in Stars)
                {
                    double star = Coverage(CircleSignedDistance(u - su, v - sv, sr), pixel);
                    color = Over(color, palette.Star, star * sa * tile);
                }
            }
        }

        // 5. 前景「列表行」：自家页签/变更集/胶囊的语汇——一行强调 + 两行钢灰。
        (double x, double y, double width, double height, double tone)[] rows = size <= 24
            ? SmallRows
            : FullRows;
        foreach ((double x, double y, double width, double height, double tone) in rows)
        {
            double bar = Coverage(RoundedBoxSignedDistance(u - (x + width / 2), v - y, width / 2, height / 2, height / 2), pixel);
            if (bar <= 0)
            {
                continue;
            }

            (double r, double g, double b) fill = tone >= 0.99 ? palette.Accent : Mix(palette.Accent, palette.Secondary, tone);
            color = Over(color, fill, bar * tile);
        }

        // 6. 内描边：亮色主题下砖块会糊进白底，靠 1px 边框撑住轮廓。
        double rim = Coverage(Math.Abs(RoundedBoxSignedDistance(u - 0.5, v - 0.5, 0.5, 0.5, 0.225 * size * pixel)) - pixel, pixel);
        color = Over(color, palette.Rim, rim * palette.RimStrength * tile);

        red = ToByte(color.r);
        green = ToByte(color.g);
        blue = ToByte(color.b);
        alpha = ToByte(tile);
    }

    private static readonly (double X, double Y, double Width, double Height, double Tone)[] FullRows =
    [
        (0.235, 0.372, 0.46, 0.072, 1.00),
        (0.235, 0.500, 0.335, 0.072, 0.55),
        (0.235, 0.628, 0.235, 0.072, 0.32),
    ];

    private static readonly (double X, double Y, double Width, double Height, double Tone)[] SmallRows =
    [
        (0.225, 0.400, 0.50, 0.108, 1.00),
        (0.225, 0.592, 0.32, 0.108, 0.45),
    ];

    private static readonly (double X, double Y, double Radius, double Alpha)[] Stars =
    [
        (0.775, 0.225, 0.011, 0.55),
        (0.865, 0.335, 0.008, 0.38),
        (0.665, 0.150, 0.007, 0.30),
        (0.150, 0.255, 0.009, 0.34),
        (0.105, 0.700, 0.007, 0.26),
        (0.880, 0.620, 0.007, 0.24),
    ];

    // ---- 几何与合成 ----

    private static double RoundedBoxSignedDistance(double x, double y, double halfWidth, double halfHeight, double radius)
    {
        double qx = Math.Abs(x) - halfWidth + radius;
        double qy = Math.Abs(y) - halfHeight + radius;
        return Math.Min(Math.Max(qx, qy), 0) + Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) - radius;
    }

    private static double CircleSignedDistance(double x, double y, double radius) => Math.Sqrt(x * x + y * y) - radius;

    /// <summary>椭圆近似符号距离：用隐函数的梯度长度做一阶折算，环带足够准。</summary>
    private static double EllipseSignedDistance(double x, double y, double radiusX, double radiusY)
    {
        double nx = x / radiusX;
        double ny = y / radiusY;
        double k = Math.Sqrt(nx * nx + ny * ny);
        if (k < 1e-6)
        {
            return -Math.Min(radiusX, radiusY);
        }

        double gradient = Math.Sqrt((nx * nx) / (radiusX * radiusX) + (ny * ny) / (radiusY * radiusY));
        return (k - 1) * k / gradient;
    }

    private static double RadialAlpha(double x, double y, double radius)
    {
        double d = Math.Sqrt(x * x + y * y) / radius;
        return d >= 1 ? 0 : (1 - d) * (1 - d);
    }

    private static double Coverage(double distance, double pixel) => Math.Clamp(0.5 - distance / pixel, 0, 1);

    private static double Ease(double v) => v * v * (3 - 2 * v);

    private static (double R, double G, double B) Lerp((double R, double G, double B) from, (double R, double G, double B) to, double t) =>
        (from.R + (to.R - from.R) * t, from.G + (to.G - from.G) * t, from.B + (to.B - from.B) * t);

    private static (double R, double G, double B) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t) => Lerp(a, b, t);

    private static (double R, double G, double B) Over((double R, double G, double B) backdrop, (double R, double G, double B) ink, double alpha) =>
        (backdrop.R + (ink.R - backdrop.R) * alpha, backdrop.G + (ink.G - backdrop.G) * alpha, backdrop.B + (ink.B - backdrop.B) * alpha);

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);

    // ---- 预览图 ----

    private static byte[] BuildPreview(byte[][] dark, byte[][] light)
    {
        const int width = 640;
        const int height = 560;
        var sheet = new byte[width * height * 4];
        Fill(sheet, width, height, 0, 0, width, height / 2, (31, 34, 42));
        Fill(sheet, width, height, 0, height / 2, width, height / 2, (245, 249, 252));

        int x = 20;
        for (int i = 0; i < Sizes.Length; i++)
        {
            int cell = Sizes[i];
            Blit(sheet, width, height, x, height / 4 - cell / 2, dark[i], cell);
            Blit(sheet, width, height, x, height - height / 4 - cell / 2, light[i], cell);
            x += cell + 18;
        }

        return sheet;
    }

    private static void Fill(byte[] pixels, int width, int height, int left, int top, int right, int bottom, (int R, int G, int B) color)
    {
        for (int y = Math.Max(0, top); y < Math.Min(height, bottom); y++)
        {
            for (int x = Math.Max(0, left); x < Math.Min(width, right); x++)
            {
                int index = (y * width + x) * 4;
                pixels[index] = (byte)color.R;
                pixels[index + 1] = (byte)color.G;
                pixels[index + 2] = (byte)color.B;
                pixels[index + 3] = 255;
            }
        }
    }

    private static void Blit(byte[] pixels, int width, int height, int left, int top, byte[] source, int size)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int targetY = top + y;
                int targetX = left + x;
                if (targetX < 0 || targetY < 0 || targetX >= width || targetY >= height)
                {
                    continue;
                }

                int sourceIndex = (y * size + x) * 4;
                float alpha = source[sourceIndex + 3] / 255f;
                int target = (targetY * width + targetX) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    pixels[target + channel] = (byte)Math.Round(pixels[target + channel] * (1 - alpha) + source[sourceIndex + channel] * alpha);
                }

                pixels[target + 3] = 255;
            }
        }
    }

    // ---- 编码器 ----

    private static byte[] BuildIco(int[] sizes, byte[][] frames)
    {
        var payloads = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            payloads[i] = EncodePng(sizes[i], frames[i]);
        }

        using var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);

        int offset = 6 + (16 * sizes.Length);
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(payloads[i].Length);
            writer.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (byte[] payload in payloads)
        {
            writer.Write(payload);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EncodePng(int size, byte[] rgba)
    {
        int stride = size * 4;
        var raw = new byte[size * (stride + 1)];
        for (int y = 0; y < size; y++)
        {
            int row = y * (stride + 1);
            raw[row] = 0; // 全部使用 filter type 0（None）：图标平坦，压缩足够。
            Buffer.BlockCopy(rgba, y * stride, raw, row + 1, stride);
        }

        using var image = new MemoryStream();
        image.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], size);
        BinaryPrimitives.WriteInt32BigEndian(header[4..8], size);
        header[8] = 8;   // bit depth
        header[9] = 6;   // RGBA
        header[10] = 0;  // no interlace
        WriteChunk(image, "IHDR", header);

        using (var deflate = new MemoryStream())
        {
            using (var zlib = new ZLibStream(deflate, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(raw, 0, raw.Length);
            }

            WriteChunk(image, "IDAT", deflate.ToArray());
        }

        WriteChunk(image, "IEND", []);
        return image.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var body = new byte[type.Length + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, 0, type.Length, body, 0);
        data.CopyTo(body.AsSpan(type.Length));
        stream.Write(body);
        stream.Write(Crc(body));
    }

    private static byte[] Crc(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        crc ^= 0xFFFFFFFF;
        Span<byte> result = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(result, crc);
        return result.ToArray();
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    /// <summary>调色全部取自 Tokens.axaml，改令牌就重生成图标，不留第二套颜色。</summary>
    private readonly record struct Palette(
        (double R, double G, double B) Top,
        (double R, double G, double B) Bottom,
        (double R, double G, double B) Accent,
        (double R, double G, double B) AccentBright,
        (double R, double G, double B) Secondary,
        (double R, double G, double B) Star,
        (double R, double G, double B) Rim,
        double GlowStrength,
        double RimStrength,
        bool IsLight)
    {
        public static Palette Dark { get; } = new(
            Hex(0x26, 0x2B, 0x36),   // 砖面顶部：比 CkanCardBackgroundBrush 略提亮
            Hex(0x0F, 0x12, 0x18),   // 砖面底部：CkanSurfaceBackgroundBrush
            Hex(0x72, 0xA7, 0xFF),   // CkanAccentBrush（暗色）
            Hex(0x8A, 0xB6, 0xFF),   // CkanAccentHoverBrush（暗色）
            Hex(0x9A, 0xA4, 0xB2),   // CkanTextSecondaryBrush（暗色）
            Hex(0xE6, 0xEA, 0xF2),
            Hex(0x2E, 0x36, 0x46),   // CkanBorderBrush 一提亮
            0.30,
            0.55,
            false);

        public static Palette Light { get; } = new(
            Hex(0xFF, 0xFF, 0xFF),   // CkanCardBackgroundBrush（亮色）
            Hex(0xE8, 0xF0, 0xFA),   // CkanSurfaceBackgroundBrush（亮色）偏蓝
            Hex(0x27, 0x5D, 0xAD),   // CkanAccentBrush（亮色）
            Hex(0x1F, 0x4E, 0x96),   // CkanAccentHoverBrush（亮色）
            Hex(0x5A, 0x64, 0x72),   // CkanTextSecondaryBrush（亮色）
            Hex(0x8B, 0x94, 0x9E),
            Hex(0xC9, 0xD4, 0xE4),
            0.22,
            0.85,
            true);

        private static (double R, double G, double B) Hex(int r, int g, int b) => (r / 255d, g / 255d, b / 255d);
    }
}
