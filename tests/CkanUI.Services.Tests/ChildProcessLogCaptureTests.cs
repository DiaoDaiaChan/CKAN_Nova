using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-01 的子进程输出采集（14 篇 §3.2 ④）。
/// 真实 ckan.exe 的输出不进本进程 log4net，这一路断了，求解/安装阶段的日志页就是空白。
/// </summary>
[TestFixture]
sealed class ChildProcessLogCaptureTests
{
    private static LogStore NewStore() => new(null, new LogStoreOptions { Capacity = 200 });

    private static void WaitFor(Func<bool> condition, string because)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 5_000)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail("等待超时：" + because);
    }

    [TestCase(false, "starting solver", LogLevel.Info)]
    [TestCase(false, "warning: deprecated api", LogLevel.Warn)]
    [TestCase(false, "error: cannot resolve", LogLevel.Error)]
    [TestCase(true, "", LogLevel.Info)]
    [TestCase(true, "just informational", LogLevel.Warn)]
    [TestCase(true, "FATAL: out of memory", LogLevel.Error)]
    [TestCase(false, "exception thrown while installing", LogLevel.Error)]
    [TestCase(false, "download failed", LogLevel.Error)]
    [TestCase(false, "failure tolerance exceeded", LogLevel.Error)]
    public void LevelFor_ClassifiesLines(bool isError, string line, LogLevel expected) =>
        Assert.That(ChildProcessLogCapture.LevelFor(isError, line), Is.EqualTo(expected));

    [Test]
    public void LevelFor_TreatsEmptyAsInfo()
    {
        Assert.That(ChildProcessLogCapture.LevelFor(true, null), Is.EqualTo(LogLevel.Info));
        Assert.That(ChildProcessLogCapture.LevelFor(true, string.Empty), Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void Constructor_RequiresStore() =>
        Assert.Throws<ArgumentNullException>(() => new ChildProcessLogCapture(null!));

    [Test]
    public void Attach_RejectsProcessWithoutRedirection()
    {
        using var capture = new ChildProcessLogCapture(NewStore());
        using var process = new Process { StartInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false } };

        var result = capture.Attach(process);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("进程未重定向标准输出/错误，无法采集"));
        Assert.That(capture.AttachedCount, Is.EqualTo(0));
    }

    [Test]
    public void Attach_RequiresProcess() =>
        Assert.Throws<ArgumentNullException>(() => new ChildProcessLogCapture(NewStore()).Attach(null!));

    [Test]
    public void Attach_CapturesStdoutAndStderrLines()
    {
        var store = NewStore();
        using var capture = new ChildProcessLogCapture(store);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("echo hello && echo error: boom 1>&2");

        process.Start();
        var attached = capture.Attach(process, correlationId: "child-7");
        process.WaitForExit(10_000);

        Assert.That(attached.IsSuccess, Is.True, attached.Message);
        WaitFor(() => store.Count >= 2, "子进程两行输出没有都进缓冲");

        var events = store.Snapshot().Events;
        Assert.That(events.Any(logEvent => logEvent.Message.Contains("hello", StringComparison.Ordinal)), Is.True);
        Assert.That(events.Any(logEvent => logEvent.Message.Contains("boom", StringComparison.Ordinal)), Is.True);
        Assert.That(events.Any(logEvent => logEvent.Level == LogLevel.Error), Is.True, "stderr 里的 error 必须升级为错误");
        Assert.That(events.All(logEvent => logEvent.Kind == LogSourceKind.Child && logEvent.Source.StartsWith("child:", StringComparison.Ordinal)), Is.True);
        Assert.That(events.All(logEvent => logEvent.CorrelationId == "child-7"), Is.True);
    }

    [Test]
    public void Detach_IsSafeForUnknownProcessAndUpdatingCount()
    {
        using var capture = new ChildProcessLogCapture(NewStore());
        using var process = new Process { StartInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false } };

        capture.Detach(process);

        Assert.That(capture.AttachedCount, Is.EqualTo(0));
        Assert.Throws<ArgumentNullException>(() => capture.Detach(null!));
    }

    [Test]
    public void Dispose_DetachesEverything()
    {
        var capture = new ChildProcessLogCapture(NewStore());
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("echo once");
        process.Start();
        capture.Attach(process);
        Assert.That(capture.AttachedCount, Is.EqualTo(1));

        capture.Dispose();
        process.WaitForExit(10_000);

        Assert.That(capture.AttachedCount, Is.EqualTo(0));
        capture.Dispose();
    }
}
