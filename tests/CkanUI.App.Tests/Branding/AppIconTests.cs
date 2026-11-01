using System;
using System.IO;

using Avalonia.Controls;

using CkanUI.App.Branding;

using NUnit.Framework;

namespace CkanUI.App.Tests.Branding;

/// <summary>
/// 图标加载入口（M6 / PKG-02）：窗口与托盘都靠 <see cref="AppIcon"/> 拿同一份 ICO，
/// 一旦「随包复制没跟上」或「路径拼错」，两边会一起静默降级，所以这里把两件事钉住。
/// </summary>
[TestFixture]
public sealed class AppIconTests
{
    [Test]
    public void ResolvePath_PointsAtThePackagedIco()
    {
        var path = AppIcon.ResolvePath();

        Assert.That(Path.GetFileName(path), Is.EqualTo("ckan.ico"));
        Assert.That(Path.GetDirectoryName(path), Does.EndWith("Assets"));
        Assert.That(File.Exists(path), Is.True, "ckan.ico 没有复制到输出目录（csproj 的 Content 项或项目引用断了）");
    }

    [Test]
    public void TryLoad_ReadsThePackagedIco()
    {
        var icon = AppIcon.TryLoad();

        Assert.That(icon, Is.Not.Null, "输出目录里有 ckan.ico 但解析失败：ICO 结构或 PNG 条目坏了");
    }

    [Test]
    public void PackagedIco_IsPngEntryAndCarriesEverySize()
    {
        // 生成器写 16/24/32/48/64/128/256 七档 PNG 条目；退回 BMP 条目会让 256 档体积爆掉。
        var bytes = File.ReadAllBytes(AppIcon.ResolvePath());

        Assert.That(bytes.Length, Is.GreaterThan(8));
        Assert.That(BitConverter.ToUInt16(bytes, 2), Is.EqualTo((ushort)1), "ICO 类型字段应为 1");
        int count = BitConverter.ToUInt16(bytes, 4);
        Assert.That(count, Is.EqualTo(7));

        var sizes = new int[count];
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (16 * i);
            int width = bytes[entry] == 0 ? 256 : bytes[entry];
            sizes[i] = width;
            uint length = BitConverter.ToUInt32(bytes, entry + 8);
            uint offset = BitConverter.ToUInt32(bytes, entry + 12);
            Assert.That(offset + length, Is.LessThanOrEqualTo((uint)bytes.Length), $"第 {i} 档越界");

            // PNG 魔数 89 50 4E 47：ICO 的 256 档只有 PNG 条目才压得进合理体积。
            Assert.That(bytes[(int)offset], Is.EqualTo((byte)0x89), $"第 {i} 档不是 PNG 条目");
            Assert.That(bytes[(int)offset + 1], Is.EqualTo((byte)0x50));
        }

        Assert.That(sizes, Is.EqualTo(new[] { 16, 24, 32, 48, 64, 128, 256 }));
    }
}
