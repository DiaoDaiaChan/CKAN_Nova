using System;
using System.Linq;

using CKAN;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-01 IUser 通道适配（14 篇 §3.2 ②）。
/// Core 的 RaiseMessage/RaiseError 是用户真正看到的信息流，log4net 里几乎没有对应记录，
/// 所以这一层必须自己入流，且不能淹没环形缓冲（进度只在整十百分点落一条）。
/// </summary>
[TestFixture]
sealed class LoggingUserTests
{
    private static LogStore NewStore() => new(null, new LogStoreOptions { Capacity = 500 });

    [Test]
    public void RaiseMessage_WritesInfoEvent()
    {
        var store = NewStore();
        var inner = new RecordingUser();
        var user = new LoggingUser(inner, store);

        user.RaiseMessage("磁盘写入完成");

        var item = store.Snapshot().Events.Single();
        Assert.That(item.Level, Is.EqualTo(LogLevel.Info));
        Assert.That(item.Kind, Is.EqualTo(LogSourceKind.User));
        Assert.That(item.Source, Is.EqualTo("user"));
        Assert.That(item.Message, Is.EqualTo("磁盘写入完成"));
        Assert.That(inner.Messages, Is.EqualTo(new[] { "磁盘写入完成" }));
    }

    [Test]
    public void RaiseMessage_FormatsArguments()
    {
        var store = NewStore();
        var user = new LoggingUser(new RecordingUser(), store);

        user.RaiseMessage("Mod {0} 安装失败", "RealChute");

        Assert.That(store.Snapshot().Events.Single().Message, Is.EqualTo("Mod RealChute 安装失败"));
    }

    [Test]
    public void RaiseMessage_WithoutArgumentsKeepsText()
    {
        var store = NewStore();
        var user = new LoggingUser(new RecordingUser(), store);

        user.RaiseMessage("100% 完成");

        Assert.That(store.Snapshot().Events.Single().Message, Is.EqualTo("100% 完成"));
    }

    [Test]
    public void RaiseError_WritesErrorEventAndForwards()
    {
        var store = NewStore();
        var inner = new RecordingUser();
        var user = new LoggingUser(inner, store);

        user.RaiseError("安装失败：{0}", "锁被占用");

        var item = store.Snapshot().Events.Single();
        Assert.That(item.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(item.Message, Is.EqualTo("安装失败：锁被占用"));
        Assert.That(inner.Errors, Is.EqualTo(new[] { "安装失败：锁被占用" }));
    }

    [Test]
    public void RaiseProgress_WritesOnlyOnBucketChange()
    {
        var store = NewStore();
        var inner = new RecordingUser();
        var user = new LoggingUser(inner, store);

        user.RaiseProgress("下载中", 5);
        user.RaiseProgress("下载中", 6);
        user.RaiseProgress("下载中", 12);
        user.RaiseProgress("下载中", 100);

        var events = store.Snapshot().Events;
        Assert.That(events, Has.Count.EqualTo(3), "同一整十区间内的进度不应重复落盘");
        Assert.That(events[0].Message, Is.EqualTo("5% 下载中"));
        Assert.That(events[1].Message, Is.EqualTo("12% 下载中"));
        Assert.That(events[2].Message, Is.EqualTo("100% 下载中"));
        Assert.That(events.Select(item => item.Level), Is.All.EqualTo(LogLevel.User));
        Assert.That(inner.Progress, Has.Count.EqualTo(4), "转发不能省");
    }

    [Test]
    public void RaiseProgress_WritesAgainWhenBucketRepeatsAfterJump()
    {
        var store = NewStore();
        var user = new LoggingUser(new RecordingUser(), store);

        user.RaiseProgress("下载中", 10);
        user.RaiseProgress("下载中", 20);

        Assert.That(store.Count, Is.EqualTo(2));
    }

    [Test]
    public void RaiseProgress_ByteRateCounterIsForwardedWithoutLogging()
    {
        var store = NewStore();
        var inner = new RecordingUser();
        var user = new LoggingUser(inner, store);

        user.RaiseProgress(new ByteRateCounter());

        Assert.That(store.Count, Is.Zero, "传输层进度已由百分比通道记录，这里不重复入流");
        Assert.That(inner.ProgressRateCalls, Is.EqualTo(1));
    }

    [Test]
    public void DialogCallsAreForwarded()
    {
        var inner = new RecordingUser();
        var user = new LoggingUser(inner, NewStore());

        Assert.That(user.RaiseYesNoDialog("继续？"), Is.True);
        Assert.That(user.RaiseSelectionDialog("选一个", "a", "b"), Is.EqualTo(2));
        Assert.That(inner.YesNoCalls, Is.EqualTo(1));
        Assert.That(inner.SelectionCalls, Is.EqualTo(1));
    }

    [Test]
    public void HeadlessAndInnerAreExposed()
    {
        var inner = new RecordingUser();
        var user = new LoggingUser(inner, NewStore());

        Assert.That(user.Headless, Is.EqualTo(inner.Headless));
        Assert.That(user.Inner, Is.SameAs(inner));
    }

    [Test]
    public void Constructor_RequiresInnerAndStore()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggingUser(null!, NewStore()));
        Assert.Throws<ArgumentNullException>(() => new LoggingUser(new RecordingUser(), null!));
    }
}
