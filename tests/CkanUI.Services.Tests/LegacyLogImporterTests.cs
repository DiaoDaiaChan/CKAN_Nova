using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using CkanUI.TestSupport;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-02 旧 CKAN.log 导入（14 篇 §3.2 / §8）。
/// 为什么要有这条路：老用户升级上来时第一件事就是把历史日志贴进来，
/// 解析错一行就会让「染色快照」和排障结论整体偏一层。
/// </summary>
[TestFixture]
sealed class LegacyLogImporterTests
{
    private static LogStore NewStore() => new(null, new LogStoreOptions { Capacity = 1000 });

    private static string Header(string time, string level, string logger, string message) =>
        time + " [1] " + level + " " + logger + " - " + message;

    [Test]
    public void Parse_ReadsStandardHeader()
    {
        var events = LegacyLogImporter.Parse([Header("2026-01-02 03:04:05", "INFO", "CKAN.ModuleInstaller", "Installing RealChute")]);

        Assert.That(events, Has.Count.EqualTo(1));
        var item = events[0];
        Assert.That(item.Level, Is.EqualTo(LogLevel.Info));
        Assert.That(item.Kind, Is.EqualTo(LogSourceKind.Legacy));
        Assert.That(item.Source, Is.EqualTo("legacy:CKAN.ModuleInstaller"));
        Assert.That(item.Message, Is.EqualTo("Installing RealChute"));
        Assert.That(item.TimeLocal, Is.EqualTo(new DateTime(2026, 1, 2, 3, 4, 5)));
        Assert.That(item.Detail, Is.Null);
    }

    [TestCase("TRACE", LogLevel.Trace)]
    [TestCase("DEBUG", LogLevel.Debug)]
    [TestCase("INFO", LogLevel.Info)]
    [TestCase("WARN", LogLevel.Warn)]
    [TestCase("ERROR", LogLevel.Error)]
    [TestCase("FATAL", LogLevel.Fatal)]
    [TestCase("USER", LogLevel.User)]
    public void Parse_MapsEveryLevel(string level, LogLevel expected)
    {
        var events = LegacyLogImporter.Parse([Header("2026-01-02 03:04:05", level, "CKAN.Main", "x")]);

        Assert.That(events[0].Level, Is.EqualTo(expected));
    }

    // log4net 的默认 %d 就是 "yyyy-MM-dd HH:mm:ss,fff"（带毫秒），所以带毫秒的那一档必须把毫秒留住：
    // 旧日志里同一秒内的多行顺序全靠这三位区分，截掉就等于把「谁先谁后」抹平了。
    [TestCase("2026-01-02 03:04:05,123", 123)]
    [TestCase("2026-01-02T03:04:05", 0)]
    public void Parse_AcceptsTimestampVariants(string time, int expectedMillisecond)
    {
        var events = LegacyLogImporter.Parse([time + " INFO CKAN.Main - hello"]);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(
            events[0].TimeLocal,
            Is.EqualTo(new DateTime(2026, 1, 2, 3, 4, 5, expectedMillisecond)));
    }

    [Test]
    public void Parse_AttachesContinuationLinesToDetail()
    {
        var events = LegacyLogImporter.Parse(
        [
            Header("2026-01-02 03:04:05", "ERROR", "CKAN.Main", "安装失败"),
            "  at CKAN.ModuleInstaller.Install()",
            "  at CKAN.Main.Run()",
        ]);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Detail, Is.EqualTo("  at CKAN.ModuleInstaller.Install()" + "\n" + "  at CKAN.Main.Run()"));
    }

    [Test]
    public void Parse_IgnoresPreambleWithoutPendingEvent()
    {
        var events = LegacyLogImporter.Parse(["CKAN 启动中…", "", "  stray line"]);

        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Parse_HandlesEmptyInput()
    {
        Assert.That(LegacyLogImporter.Parse([]), Is.Empty);
    }

    [Test]
    public void Parse_KeepsBlankLinesOutOfDetail()
    {
        var events = LegacyLogImporter.Parse(
        [
            Header("2026-01-02 03:04:05", "WARN", "CKAN.Main", "w"),
            "",
            "   ",
            "真正的续行",
        ]);

        Assert.That(events[0].Detail, Is.EqualTo("真正的续行"));
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("no timestamp here", false)]
    [TestCase("2026-01-02 03:04:05 INFO CKAN.Main - ok", true)]
    public void TryParseLine_ValidatesHeader(string? line, bool expected) =>
        Assert.That(LegacyLogImporter.TryParseLine(line, out _, out _, out _, out _), Is.EqualTo(expected));

    [Test]
    public void TryParseLine_FillsOutputs()
    {
        var ok = LegacyLogImporter.TryParseLine(
            "2026-01-02 03:04:05 [7] ERROR CKAN.Net - timed out",
            out var time,
            out var level,
            out var logger,
            out var message);

        Assert.That(ok, Is.True);
        Assert.That(time, Is.EqualTo(new DateTime(2026, 1, 2, 3, 4, 5)));
        Assert.That(level, Is.EqualTo(LogLevel.Error));
        Assert.That(logger, Is.EqualTo("CKAN.Net"));
        Assert.That(message, Is.EqualTo("timed out"));
    }

    [Test]
    public void Parse_RequiresLines()
    {
        Assert.Throws<ArgumentNullException>(() => LegacyLogImporter.Parse(null!));
    }

    [Test]
    public void Import_RequiresPath()
    {
        var importer = new LegacyLogImporter(NewStore());

        Assert.That(importer.Import(" ").IsSuccess, Is.False);
        Assert.That(importer.Import(" ").Message, Is.EqualTo("日志文件路径为空"));
    }

    [Test]
    public void Import_MissingFileFails()
    {
        using var temp = new TempWorkspace("legacy-missing");
        var importer = new LegacyLogImporter(NewStore());

        var result = importer.Import(temp.Combine("nope.log"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("日志文件不存在"));
    }

    [Test]
    public void Import_AppendsParsedEventsToStore()
    {
        using var temp = new TempWorkspace("legacy-import");
        var path = temp.Combine("CKAN.log");
        File.WriteAllLines(
            path,
            new[]
            {
                Header("2026-01-02 03:04:05", "INFO", "CKAN.Main", "first"),
                Header("2026-01-02 03:04:06", "ERROR", "CKAN.Net", "second"),
            });

        var store = NewStore();
        var result = new LegacyLogImporter(store).Import(path);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(2));
        Assert.That(store.Count, Is.EqualTo(2));
        Assert.That(store.Snapshot().Events.Select(item => item.Kind), Is.All.EqualTo(LogSourceKind.Legacy));
    }
}
