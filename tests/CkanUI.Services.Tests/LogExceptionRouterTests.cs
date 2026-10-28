using System;
using System.Linq;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-01 未捕获异常采集（14 篇 §3.2 ③）。
/// 为什么单列：Core 只记 245 处 logger、59 条日志语句，真正的崩溃信息全在未捕获异常里。
/// </summary>
[TestFixture]
sealed class LogExceptionRouterTests
{
    private static LogStore NewStore() => new(null, new LogStoreOptions { Capacity = 500 });

    [Test]
    public void Report_WritesExceptionEvent()
    {
        var store = NewStore();
        using var router = new LogExceptionRouter(store);

        router.Report(new InvalidOperationException("boom"), "dispatcher", "MainWindow");

        var item = store.Snapshot().Events.Single();
        Assert.That(item.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(item.Kind, Is.EqualTo(LogSourceKind.Exception));
        Assert.That(item.Message, Is.EqualTo("InvalidOperationException: boom"));
        Assert.That(item.Detail, Does.Contain("boom"));
        Assert.That(item.Tag("origin"), Is.EqualTo("dispatcher"));
        Assert.That(item.Tag("context"), Is.EqualTo("MainWindow"));
        Assert.That(router.ReportedCount, Is.EqualTo(1));
    }

    [Test]
    public void Report_NullExceptionStillWritesUiEvent()
    {
        var store = NewStore();
        using var router = new LogExceptionRouter(store);

        router.Report(null, "task");

        var item = store.Snapshot().Events.Single();
        Assert.That(item.Message, Is.EqualTo("未捕获异常（task）"));
        Assert.That(item.Kind, Is.EqualTo(LogSourceKind.Ui));
        Assert.That(item.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(item.Detail, Is.Null);
    }

    [Test]
    public void Report_OmitsBlankContextTag()
    {
        var store = NewStore();
        using var router = new LogExceptionRouter(store);

        router.Report(new Exception("x"), "origin", "  ");

        Assert.That(store.Snapshot().Events.Single().Tag("context"), Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Report_RequiresOrigin(string origin)
    {
        using var router = new LogExceptionRouter(NewStore());

        Assert.Throws<ArgumentException>(() => router.Report(new Exception("x"), origin));
    }

    [Test]
    public void ReportMessage_WritesRequestedLevel()
    {
        var store = NewStore();
        using var router = new LogExceptionRouter(store);

        router.ReportMessage("navigation", LogLevel.Warn, "导航失败", "detail text");

        var item = store.Snapshot().Events.Single();
        Assert.That(item.Kind, Is.EqualTo(LogSourceKind.Ui));
        Assert.That(item.Level, Is.EqualTo(LogLevel.Warn));
        Assert.That(item.Message, Is.EqualTo("导航失败"));
        Assert.That(item.Detail, Is.EqualTo("detail text"));
        Assert.That(item.Tag("origin"), Is.EqualTo("navigation"));
    }

    [Test]
    public void Attach_IsIdempotentAndDetachIsSafe()
    {
        using var router = new LogExceptionRouter(NewStore());

        Assert.That(router.IsAttached, Is.False);
        router.Attach();
        router.Attach();
        Assert.That(router.IsAttached, Is.True);

        router.Detach();
        router.Detach();
        Assert.That(router.IsAttached, Is.False);
    }

    [Test]
    public void Dispose_Detaches()
    {
        var router = new LogExceptionRouter(NewStore());
        router.Attach();

        router.Dispose();

        Assert.That(router.IsAttached, Is.False);
    }

    [Test]
    public void Report_CountsEveryCall()
    {
        var store = NewStore();
        using var router = new LogExceptionRouter(store);

        router.Report(new Exception("a"), "x");
        router.ReportMessage("y", LogLevel.Info, "b");

        Assert.That(router.ReportedCount, Is.EqualTo(2));
        Assert.That(store.Count, Is.EqualTo(2));
    }

    [Test]
    public void Constructor_RequiresStore()
    {
        Assert.Throws<ArgumentNullException>(() => new LogExceptionRouter(null!));
    }
}
