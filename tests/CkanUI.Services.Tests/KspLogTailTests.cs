using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// KSP.log 的两层解析：行格式（KspLogLineParser）与增量跟随（KspLogTailService）。
/// 为什么拆两层测：玩家贴来的 KSP.log 五花八门（模块名带空格、续行是 Unity 堆栈、时间只有时分秒），
/// 解析错了后面整条「游戏日志 → AI 诊断」都建立在假数据上。
/// </summary>
[TestFixture]
public sealed class KspLogTailTests
{
    private static readonly DateTime DateHint = new(2024, 5, 6, 0, 0, 0, DateTimeKind.Local);

    [Test]
    public void Parser_ReadsLevelModuleAndTime()
    {
        Assert.That(KspLogLineParser.TryParse("[LOG 12:34:56.789] [KSP] Hello there", DateHint, out var info), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(info.Level, Is.EqualTo(LogLevel.Info));
            Assert.That(info.Module, Is.EqualTo("KSP"));
            Assert.That(info.Message, Is.EqualTo("Hello there"));
            Assert.That(info.IsContinuation, Is.False);
            Assert.That(info.TimeStamp, Is.EqualTo(new DateTime(2024, 5, 6, 12, 34, 56, 789)));
        });

        Assert.That(KspLogLineParser.TryParse("[WRN 01:02:03.004] [Module Manager] late", DateHint, out var warn), Is.True);
        Assert.That(warn.Level, Is.EqualTo(LogLevel.Warn));
        Assert.That(warn.Module, Is.EqualTo("Module Manager"));

        Assert.That(KspLogLineParser.TryParse("[ERR 01:02:03.004] [PartLoader] broken", DateHint, out var err), Is.True);
        Assert.That(err.Level, Is.EqualTo(LogLevel.Error));

        Assert.That(KspLogLineParser.TryParse("[EXC 01:02:03.004] NullReferenceException", DateHint, out var exc), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(exc.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(exc.Module, Is.Null);
            Assert.That(exc.Message, Is.EqualTo("NullReferenceException"));
        });
    }

    [Test]
    public void Parser_TreatsUnmatchedLinesAsContinuation()
    {
        Assert.That(
            KspLogLineParser.TryParse("  at System.String.Format (System.Object arg0)", DateHint, out var line),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(line.IsContinuation, Is.True);
            // 续行只保留正文（缩进由界面统一给），首尾空白在解析时就裁掉。
            Assert.That(line.Message, Is.EqualTo("at System.String.Format (System.Object arg0)"));
            Assert.That(line.Level, Is.EqualTo(LogLevel.Info));
        });

        Assert.That(KspLogLineParser.TryParse("   ", DateHint, out _), Is.False);
        Assert.That(KspLogLineParser.TryParse(null, DateHint, out _), Is.False);
    }

    [Test]
    public void Parser_KeepsReadingWhenTheTimeStampIsGarbage()
    {
        Assert.That(KspLogLineParser.TryParse("[LOG 99:99:99] [KSP] still text", DateHint, out var line), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(line.Message, Is.EqualTo("still text"));
            Assert.That(line.TimeStamp, Is.Null);
        });
    }

    [Test]
    public async Task Tail_ImportsTheWholeSmallFileAndTagsGameRows()
    {
        using var temp = new TempDirectory();
        var gameDir = temp.Path;
        File.WriteAllText(
            Path.Combine(gameDir, "KSP.log"),
            "[LOG 10:00:00.001] [KSP] start\n[WRN 10:00:01.002] [Module Manager] patch\n  at Boom.Ctor ()\n",
            new UTF8Encoding(false));

        var store = new LogStore(new StubClassifier(LogFamily.Other));
        using var tail = new KspLogTailService(store, TimeSpan.FromHours(1));
        tail.Attach(gameDir, "Default");

        // Attach 时定时器会立即起跳一次，可能已先一步读走这段内容；这里只断言最终缓冲，
        // 不把「这一次泵返回几行」写进断言（泵之间已用信号量互斥，快照是稳定的）。
        await tail.PumpAsync(CancellationToken.None);

        var events = store.Snapshot().Events;
        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(3));
            Assert.That(events.Select(e => e.Kind), Is.All.EqualTo(LogSourceKind.Game));
            Assert.That(events[0].Message, Is.EqualTo("start"));
            Assert.That(events[0].TimeLocal, Is.EqualTo(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 10, 0, 0, 1)));
            Assert.That(events[1].Level, Is.EqualTo(LogLevel.Warn));
            Assert.That(events[1].Tag("module"), Is.EqualTo("Module Manager"));
            Assert.That(events[2].Tag("game.cont"), Is.EqualTo("1"));
            Assert.That(tail.ImportedLineCount, Is.EqualTo(3));
            Assert.That(tail.IsWatching, Is.True);
        });
    }

    [Test]
    public async Task Tail_ReadsOnlyTheAppendedPartOnTheNextPump()
    {
        using var temp = new TempDirectory();
        var logPath = Path.Combine(temp.Path, "KSP.log");
        File.WriteAllText(logPath, "[LOG 10:00:00.001] [KSP] one\n", new UTF8Encoding(false));

        var store = new LogStore(new StubClassifier(LogFamily.Other));
        using var tail = new KspLogTailService(store, TimeSpan.FromHours(1));
        tail.Attach(temp.Path, "Default");
        await tail.PumpAsync(CancellationToken.None);

        File.AppendAllText(logPath, "[LOG 10:00:02.003] [KSP] two\n", new UTF8Encoding(false));
        await tail.PumpAsync(CancellationToken.None);

        var messages = store.Snapshot().Events.Select(e => e.Message).ToArray();
        Assert.That(messages, Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public async Task Tail_RestartsFromTheTopWhenTheFileShrinks()
    {
        using var temp = new TempDirectory();
        var logPath = Path.Combine(temp.Path, "KSP.log");
        File.WriteAllText(logPath, "[LOG 10:00:00.001] [KSP] before\n", new UTF8Encoding(false));

        var store = new LogStore(new StubClassifier(LogFamily.Other));
        using var tail = new KspLogTailService(store, TimeSpan.FromHours(1));
        tail.Attach(temp.Path, "Default");
        await tail.PumpAsync(CancellationToken.None);

        // 游戏重启会截断重写 KSP.log：长度变小必须从头读，否则永远停在旧偏移。
        File.WriteAllText(logPath, "[LOG 11:00:00.001] [KSP] after\n", new UTF8Encoding(false));
        await tail.PumpAsync(CancellationToken.None);

        var messages = store.Snapshot().Events.Select(e => e.Message).ToArray();
        Assert.That(messages, Is.EqualTo(new[] { "before", "after" }));
    }

    [Test]
    public void Tail_ReportsMissingLogWithoutThrowing()
    {
        using var temp = new TempDirectory();
        var store = new LogStore(new StubClassifier(LogFamily.Other));
        using var tail = new KspLogTailService(store, TimeSpan.FromHours(1));

        Assert.DoesNotThrow(() => tail.Attach(temp.Path, "Default"));
        Assert.That(tail.IsWatching, Is.False);
        Assert.That(tail.GameLogPath, Is.Null);
        Assert.That(KspLogTailService.ResolveLogPath(temp.Path), Is.Null);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            // 这个环境里 Path.GetTempPath() 的 ACL 拒绝写（UnauthorizedAccessException），
            // 所以临时目录放在测试程序集旁边，随 bin 一起被清理。
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, "ksp-tail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 测试清理失败不影响判定。
            }
        }
    }
}
