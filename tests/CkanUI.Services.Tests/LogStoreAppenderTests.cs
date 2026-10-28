using System;
using System.Collections.Generic;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;

using log4net;
using log4net.Core;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-01 的 log4net → 内部事件映射（14 篇 §3.1 / §3.2 ①）。
/// 这一层是「245 处 Core logger 零改动进日志页」的关键，映射错一个字段，日志页就少一类信息。
/// </summary>
[TestFixture]
sealed class LogStoreAppenderTests
{
    private static LoggingEvent Event(
        string message,
        Level level,
        string loggerName = "CKAN.ModuleInstaller",
        Exception? exception = null) =>
        new(
            typeof(LogStoreAppenderTests),
            LogManager.GetRepository(typeof(LogStoreAppender).Assembly),
            loggerName,
            level,
            message,
            exception);

    private static LogStore NewStore() => new(null, new LogStoreOptions { Capacity = 500 });

    private static LogStoreAppender NewAppender(ILogStore store)
    {
        var appender = new LogStoreAppender(store);
        appender.ActivateOptions();
        return appender;
    }

    [TestCase(30_000, LogLevel.Debug)]
    [TestCase(40_000, LogLevel.Info)]
    [TestCase(60_000, LogLevel.Warn)]
    [TestCase(70_000, LogLevel.Error)]
    [TestCase(110_000, LogLevel.Fatal)]
    [TestCase(5_000, LogLevel.Trace)]
    [TestCase(120_000, LogLevel.Fatal)]
    public void MapLevel_MapsLog4NetLevels(int value, LogLevel expected) =>
        Assert.That(LogStoreAppender.MapLevel(new Level(value, "x")), Is.EqualTo(expected));

    [Test]
    public void MapLevel_TreatsNullAsInfo() => Assert.That(LogStoreAppender.MapLevel(null), Is.EqualTo(LogLevel.Info));

    [Test]
    public void DoAppend_WritesEventWithSourceAndKind()
    {
        var store = NewStore();
        NewAppender(store).DoAppend(Event("开始安装 RealChute", Level.Info));

        var logEvent = store.Snapshot().Events[0];
        Assert.That(logEvent.Message, Is.EqualTo("开始安装 RealChute"));
        Assert.That(logEvent.Level, Is.EqualTo(LogLevel.Info));
        Assert.That(logEvent.Kind, Is.EqualTo(LogSourceKind.Log4Net));
        Assert.That(logEvent.Source, Is.EqualTo("log4net:CKAN.ModuleInstaller"));
    }

    [Test]
    public void DoAppend_CapturesExceptionDetail()
    {
        var store = NewStore();
        var exception = new InvalidOperationException("求解器炸了");

        NewAppender(store).DoAppend(Event("安装失败", Level.Error, exception: exception));

        var logEvent = store.Snapshot().Events[0];
        Assert.That(logEvent.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(logEvent.Detail, Does.Contain("InvalidOperationException"));
        Assert.That(logEvent.Detail, Does.Contain("求解器炸了"));
    }

    [Test]
    public void DoAppend_ReadsStructuredTagsFromProperties()
    {
        var store = NewStore();
        var loggingEvent = Event("下载失败", Level.Error);
        loggingEvent.Properties["mod"] = "RealChute";
        loggingEvent.Properties["instance"] = "默认实例";
        loggingEvent.Properties["noise"] = "不该被采集";

        NewAppender(store).DoAppend(loggingEvent);

        var logEvent = store.Snapshot().Events[0];
        Assert.That(logEvent.Tag("mod"), Is.EqualTo("RealChute"));
        Assert.That(logEvent.Tag("instance"), Is.EqualTo("默认实例"));
        Assert.That(logEvent.Tags?.ContainsKey("noise"), Is.Not.EqualTo(true));
    }

    [Test]
    public void DoAppend_UsesCorrelationIdProperty()
    {
        var store = NewStore();
        var loggingEvent = Event("一次安装事务", Level.Info);
        loggingEvent.Properties["correlationId"] = "install-42";

        NewAppender(store).DoAppend(loggingEvent);

        Assert.That(store.Snapshot().Events[0].CorrelationId, Is.EqualTo("install-42"));
    }

    [Test]
    public void DoAppend_LeavesTagsNullWhenNoStructuredProperty()
    {
        var store = NewStore();
        NewAppender(store).DoAppend(Event("普通日志", Level.Info));

        Assert.That(store.Snapshot().Events[0].Tags, Is.Null);
    }

    [Test]
    public void Threshold_IsInfoSoDebugNoiseDoesNotFlood()
    {
        var appender = new LogStoreAppender(NewStore());

        Assert.That(appender.Threshold, Is.Not.Null);
        Assert.That(appender.Threshold!.Value, Is.EqualTo(Level.Info.Value));
        Assert.That(appender.Name, Is.EqualTo("CkanUI.LogStore"));
    }

    [Test]
    public void Constructor_RequiresStore() =>
        Assert.Throws<ArgumentNullException>(() => new LogStoreAppender(null!));

    [Test]
    public void ToEvent_HandlesMissingLoggerName()
    {
        var loggingEvent = Event("没有源", Level.Warn, loggerName: string.Empty);

        Assert.That(LogStoreAppender.ToEvent(loggingEvent).Source, Is.EqualTo(LogSources.Encode(LogSourceKind.Log4Net, string.Empty)));
    }

    [Test]
    public void ToEvent_RequiresEvent() =>
        Assert.Throws<ArgumentNullException>(() => LogStoreAppender.ToEvent(null!));
}
