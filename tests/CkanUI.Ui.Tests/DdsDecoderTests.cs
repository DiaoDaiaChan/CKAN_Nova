using System;
using System.IO;
using System.Linq;
using System.Text;
using CkanUI.Ui.Assets;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Ui.Tests.Assets;

/// <summary>
/// DdsDecoder 的行为锁：ROLoadingImages 的加载图全是 DXT1，解错一个字节就是花屏。
/// </summary>
[TestFixture]
public sealed class DdsDecoderTests
{
    private TempWorkspace? _workspace;

    [SetUp]
    public void SetUp() => _workspace = new TempWorkspace("dds");

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    [Test]
    public void TryDecode_Dxt1_SolidBlock_ReturnsRed()
    {
        var bytes = Header("DXT1", flags: 0x4, height: 4, width: 4, pitch: 8, bitCount: 0, masks: default);
        var block = new byte[8];
        WriteUInt16(block, 0, 0xF800); // 红色（R5G6B5）
        WriteUInt16(block, 2, 0x001F); // 蓝色，4 位索引全是 0 → 整块都是红色
        var file = WriteTemp(bytes.Concat(block).ToArray());

        var image = DdsDecoder.TryDecode(file);

        Assert.That(image, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(image!.Width, Is.EqualTo(4));
            Assert.That(image.Height, Is.EqualTo(4));
            Assert.That(image.Pixels[0], Is.EqualTo(0), "B");
            Assert.That(image.Pixels[1], Is.EqualTo(0), "G");
            Assert.That(image.Pixels[2], Is.EqualTo(255), "R");
            Assert.That(image.Pixels[3], Is.EqualTo(255), "A");
        });
    }

    [Test]
    public void TryDecode_Uncompressed32Bit_UsesMasks()
    {
        var bytes = Header(
            fourCc: string.Empty,
            flags: 0x41,
            height: 1,
            width: 1,
            pitch: 4,
            bitCount: 32,
            masks: (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000));
        var file = WriteTemp(bytes.Concat(new byte[] { 0x33, 0x22, 0x11, 0x44 }).ToArray()); // 0x44112233

        var image = DdsDecoder.TryDecode(file);

        Assert.That(image, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(image!.Pixels[0], Is.EqualTo(0x33), "B");
            Assert.That(image.Pixels[1], Is.EqualTo(0x22), "G");
            Assert.That(image.Pixels[2], Is.EqualTo(0x11), "R");
            Assert.That(image.Pixels[3], Is.EqualTo(0x44), "A");
        });
    }

    [Test]
    public void TryDecode_Uncompressed32Bit_WhitePixel_IsNotMistakenForFailure()
    {
        var bytes = Header(
            fourCc: string.Empty,
            flags: 0x41,
            height: 1,
            width: 1,
            pitch: 4,
            bitCount: 32,
            masks: (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000));
        var file = WriteTemp(bytes.Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }).ToArray());

        var image = DdsDecoder.TryDecode(file);

        Assert.That(image, Is.Not.Null, "纯白像素是合法数据，不能当成解码失败把整张图扔掉。");
        Assert.That(image!.Pixels[0], Is.EqualTo(255));
    }

    [Test]
    public void TryDecode_MaxWidth_DownscalesKeepingAspect()
    {
        var bytes = Header("DXT1", flags: 0x4, height: 8, width: 8, pitch: 32, bitCount: 0, masks: default);
        var blocks = new byte[8 * 8 / 16 * 8];
        for (var i = 0; i < blocks.Length; i += 8)
        {
            WriteUInt16(blocks, i, 0xF800);
            WriteUInt16(blocks, i + 2, 0xF800);
        }

        var image = DdsDecoder.TryDecode(WriteTemp(bytes.Concat(blocks).ToArray()), maxWidth: 4);

        Assert.That(image, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(image!.Width, Is.EqualTo(4));
            Assert.That(image.Height, Is.EqualTo(4), "按宽度等比缩放，不能拉扁。");
        });
    }

    [Test]
    public void TryDecode_WrongMagic_ReturnsNull()
    {
        var file = WriteTemp(Encoding.ASCII.GetBytes("NOPE"));

        Assert.That(DdsDecoder.TryDecode(file), Is.Null, "不是 dds 就老老实实返回 null，让界面留空而不是崩。");
    }

    private static byte[] Header(string fourCc, uint flags, int height, int width, uint pitch, uint bitCount, (uint R, uint G, uint B, uint A) masks)
    {
        var header = new byte[128];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(header, 0);
        WriteUInt32(header, 4, 124);
        WriteUInt32(header, 8, 0x1007);
        WriteUInt32(header, 12, (uint)height);
        WriteUInt32(header, 16, (uint)width);
        WriteUInt32(header, 20, pitch);
        WriteUInt32(header, 28, 1);
        WriteUInt32(header, 76, 32);
        WriteUInt32(header, 80, (uint)(string.IsNullOrEmpty(fourCc) ? 0x41 : 0x4));
        if (string.IsNullOrEmpty(fourCc))
        {
            WriteUInt32(header, 88, bitCount);
            WriteUInt32(header, 92, masks.R);
            WriteUInt32(header, 96, masks.G);
            WriteUInt32(header, 100, masks.B);
            WriteUInt32(header, 104, masks.A);
        }
        else
        {
            Encoding.ASCII.GetBytes(fourCc).CopyTo(header, 84);
        }

        WriteUInt32(header, 108, 0x1000);
        return header;
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private string WriteTemp(byte[] bytes)
    {
        // 用仓库内 artifacts/tmp：受限沙箱下系统临时目录不一定可写。
        var path = Path.Combine(_workspace!.Path, Guid.NewGuid().ToString("N") + ".dds");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
