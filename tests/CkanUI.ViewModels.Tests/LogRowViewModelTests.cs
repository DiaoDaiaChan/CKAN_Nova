using System;
using System.Collections.Generic;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.ViewModels.Pages.Logs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>单行视图模型（14 篇 §4.2③ 的等高行 + 详情面板数据源）。</summary>
[TestFixture]
public sealed class LogRowViewModelTests
{
    private static LogRowViewModel Row(
        LogLevel level = LogLevel.Info,
        string message = "hello",
        LogFamily family = LogFamily.Net,
        string? detail = null,
        IReadOnlyDictionary<string, string>? tags = null) =>
        new(LogTestData.Entry(level, message, family, detail: detail, tags: tags));

    [Test]
    public void Constructor_ThrowsOnNullEntry() =>
        Assert.That(() => new LogRowViewModel(null!), Throws.ArgumentNullException);

    [Test]
    public void TimeText_UsesFixedWidthMillisecondFormat()
    {
        var entry = LogTestData.Entry(LogLevel.Info, "hello") with
        {
            TimeLocal = new DateTime(2026, 3, 7, 14, 5, 9, 123, DateTimeKind.Local),
        };
        var row = new LogRowViewModel(entry);

        Assert.That(row.TimeText, Is.EqualTo("14:05:09.123"));
        Assert.That(row.TimeText.Length, Is.EqualTo(12), "等宽字体下固定 12 字符才能对齐");
    }

    [Test]
    public void LevelMetadata_ComesFromGlyphTable()
    {
        var row = Row(LogLevel.Error);

        Assert.That(row.Glyph, Is.EqualTo(LogGlyphs.Glyph(LogLevel.Error)));
        Assert.That(row.LevelLabel, Is.EqualTo("错误"));
        Assert.That(row.LevelClass, Is.EqualTo("error"));
        Assert.That(row.FamilyLabel, Is.EqualTo("网络"));
    }

    [TestCase(LogLevel.Error, true, false, false)]
    [TestCase(LogLevel.Fatal, true, false, false)]
    [TestCase(LogLevel.Warn, false, true, false)]
    [TestCase(LogLevel.User, false, false, true)]
    [TestCase(LogLevel.Info, false, false, false)]
    public void SeverityFlags_AreConsistent(LogLevel level, bool isError, bool isWarning, bool isUser)
    {
        var row = Row(level);

        Assert.That(row.IsError, Is.EqualTo(isError));
        Assert.That(row.IsWarning, Is.EqualTo(isWarning));
        Assert.That(row.IsUser, Is.EqualTo(isUser));
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("at CKAN.ModuleInstaller.Install()", true)]
    public void HasDetail_IgnoresBlankDetail(string? detail, bool expected) =>
        Assert.That(Row(detail: detail).HasDetail, Is.EqualTo(expected));

    [Test]
    public void HasTags_ReflectsTagsDictionary()
    {
        var without = Row();
        var with = Row(tags: new Dictionary<string, string> { ["mod"] = "RealChute" });

        Assert.That(without.HasTags, Is.False);
        Assert.That(with.HasTags, Is.True);
    }

    [Test]
    public void SourceText_IsRawSourceForDetailPanel()
    {
        var row = new LogRowViewModel(LogTestData.Entry(LogLevel.Warn, "hello", source: "log4net:CKAN.RegistryManager"));

        Assert.That(row.SourceText, Is.EqualTo("log4net:CKAN.RegistryManager"));
    }

    [Test]
    public void PlainText_IsExportableOneLineRepresentation()
    {
        var row = Row(LogLevel.Warn, "connection reset");

        Assert.That(row.PlainText, Does.Contain("WARN"));
        Assert.That(row.PlainText, Does.Contain("connection reset"));
    }

    [TestCase("NEEDLE", true)]
    [TestCase("needle", true)]
    [TestCase("zzz", false)]
    public void Matches_IsCaseInsensitiveSubstringSearch(string keyword, bool expected) =>
        Assert.That(Row(LogLevel.Info, "found a needle here").Matches(keyword), Is.EqualTo(expected));

    [Test]
    public void Matches_LooksAtDetailToo()
    {
        var row = Row(detail: "System.Net.WebException: connection was closed");

        Assert.That(row.Matches("webexception"), Is.True);
    }

    [Test]
    public void HighlightFlags_RaisePropertyChanged()
    {
        var row = Row();
        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        row.IsHighlighted = true;
        row.IsCurrentMatch = true;

        Assert.That(changed, Does.Contain(nameof(LogRowViewModel.IsHighlighted)));
        Assert.That(changed, Does.Contain(nameof(LogRowViewModel.IsCurrentMatch)));
        Assert.That(row.IsHighlighted, Is.True);
        Assert.That(row.IsCurrentMatch, Is.True);
    }
}
