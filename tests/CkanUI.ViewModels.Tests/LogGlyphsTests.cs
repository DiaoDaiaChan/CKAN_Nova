using System;
using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.ViewModels.Pages.Logs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>字形与中文标签（14 篇 §3.4）：颜色永不单独承载语义，所以级别必须有可区分字形。</summary>
[TestFixture]
public sealed class LogGlyphsTests
{
    [TestCase(LogLevel.Trace, "·")]
    [TestCase(LogLevel.Debug, "·")]
    [TestCase(LogLevel.Info, "i")]
    [TestCase(LogLevel.Warn, "!")]
    [TestCase(LogLevel.Error, "×")]
    [TestCase(LogLevel.Fatal, "!!")]
    [TestCase(LogLevel.User, "»")]
    public void Glyph_UsesShapeForEachLevel(LogLevel level, string expected) =>
        Assert.That(LogGlyphs.Glyph(level), Is.EqualTo(expected));

    [Test]
    public void Glyph_WarningErrorAndUserAreDistinct()
    {
        var shapes = new[]
        {
            LogGlyphs.Glyph(LogLevel.Warn),
            LogGlyphs.Glyph(LogLevel.Error),
            LogGlyphs.Glyph(LogLevel.User),
        };

        Assert.That(shapes.Distinct().Count(), Is.EqualTo(3), "警告 / 错误 / 用户必须一眼能分开");
    }

    [TestCase(LogLevel.Trace, "详细", "trace")]
    [TestCase(LogLevel.Debug, "调试", "debug")]
    [TestCase(LogLevel.Info, "信息", "info")]
    [TestCase(LogLevel.Warn, "警告", "warn")]
    [TestCase(LogLevel.Error, "错误", "error")]
    [TestCase(LogLevel.Fatal, "致命", "fatal")]
    [TestCase(LogLevel.User, "用户", "user")]
    public void LevelLabelAndClass_AreMapped(LogLevel level, string label, string styleKey)
    {
        Assert.That(LogGlyphs.LevelLabel(level), Is.EqualTo(label));
        Assert.That(LogGlyphs.LevelClass(level), Is.EqualTo(styleKey));
    }

    [Test]
    public void LevelClass_IsLowerCamelStyleKeyForEveryLevel()
    {
        foreach (var level in Enum.GetValues<LogLevel>())
        {
            var key = LogGlyphs.LevelClass(level);
            Assert.That(key, Is.Not.Null.And.Not.Empty);
            Assert.That(key, Is.EqualTo(key.ToLowerInvariant()), "样式键必须是纯小写，方便拼 Log.{key} 资源名");
        }
    }

    [TestCase(LogFamily.Net, "网络")]
    [TestCase(LogFamily.Download, "下载")]
    [TestCase(LogFamily.Solve, "求解")]
    [TestCase(LogFamily.Install, "安装")]
    [TestCase(LogFamily.Fs, "文件")]
    [TestCase(LogFamily.Reg, "注册表")]
    [TestCase(LogFamily.Save, "存档")]
    [TestCase(LogFamily.Sec, "安全")]
    [TestCase(LogFamily.Ui, "界面")]
    [TestCase(LogFamily.Other, "其他")]
    public void FamilyLabel_IsMappedToChinese(LogFamily family, string expected) =>
        Assert.That(LogGlyphs.FamilyLabel(family), Is.EqualTo(expected));

    [Test]
    public void FamilyLabel_CoversEveryFamily()
    {
        var labels = Enum.GetValues<LogFamily>().Select(LogGlyphs.FamilyLabel).ToArray();
        Assert.That(labels.Any(string.IsNullOrWhiteSpace), Is.False);
        Assert.That(labels.Distinct().Count(), Is.EqualTo(labels.Length), "每个族的中文名唯一");
    }

    [TestCase(LogSourceKind.Log4Net, "内核日志")]
    [TestCase(LogSourceKind.User, "用户消息")]
    [TestCase(LogSourceKind.Exception, "异常")]
    [TestCase(LogSourceKind.Child, "子进程")]
    [TestCase(LogSourceKind.Legacy, "导入日志")]
    [TestCase(LogSourceKind.Ui, "界面事件")]
    public void SourceLabel_IsMappedToChinese(LogSourceKind kind, string expected) =>
        Assert.That(LogGlyphs.SourceLabel(kind), Is.EqualTo(expected));
}
