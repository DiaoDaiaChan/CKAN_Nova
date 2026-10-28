using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logging;
using CkanUI.Services.Logs;
using CkanUI.TestSupport;

using log4net;
using log4net.Core;
using log4net.Repository.Hierarchy;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// LOG-02 的 log4net 接线（14 篇 §3.2 ① / §7）。
/// 这套测试直接玩 log4net 的全局仓库，所以必须独占运行：其他 fixture 里的
/// <c>LogManager.ResetConfiguration</c> 会把 appender 摘掉，并发跑必然是假失败。
/// </summary>
[TestFixture]
[NonParallelizable]
sealed class LogPipelineTests
{
    private static Hierarchy Repository => (Hierarchy)LogManager.GetRepository(typeof(LogPipeline).Assembly);

    private static LogStore NewStore() => new(null, new LogStoreOptions { Capacity = 1_000 });

    private static LogPipelineOptions OptionsWith(string directory, bool writeFile = false) =>
        new() { LogDirectory = directory, WriteFile = writeFile };

    private static string[] InstalledAppenderNames() =>
        Repository.Root.Appenders.Cast<log4net.Appender.IAppender>().Select(appender => appender.Name).ToArray();

    private static void WaitFor(Func<bool> condition, string because)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 3_000)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail("等待超时：" + because);
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [SetUp]
    public void SetUp() => LogManager.ResetConfiguration(typeof(LogPipeline).Assembly);

    [TearDown]
    public void TearDown()
    {
        LogPipeline.Active?.Dispose();
        LogManager.ResetConfiguration(typeof(LogPipeline).Assembly);
    }

    [Test]
    public void Install_AttachesStoreAppenderAndRaisesThresholdToInfo()
    {
        using var temp = new TempWorkspace("pipeline-install");
        var hierarchy = Repository;
        hierarchy.Threshold = Level.Error;
        using var pipeline = new LogPipeline(NewStore(), OptionsWith(temp.Path));

        var result = pipeline.Install();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(pipeline.IsInstalled, Is.True);
        Assert.That(InstalledAppenderNames(), Does.Contain("CkanUI.LogStore"));
        Assert.That(hierarchy.Threshold!.Value, Is.EqualTo(Level.Info.Value));
        Assert.That(LogPipeline.Active, Is.SameAs(pipeline));
    }

    [Test]
    public void Install_KeepsDebugThresholdThatUserSetOnPurpose()
    {
        using var temp = new TempWorkspace("pipeline-debug");
        var hierarchy = Repository;
        hierarchy.Threshold = Level.Debug;
        using var pipeline = new LogPipeline(NewStore(), OptionsWith(temp.Path));

        pipeline.Install();

        Assert.That(hierarchy.Threshold!.Value, Is.EqualTo(Level.Debug.Value), "排障时用户显式开的 DEBUG 不能被改回去");
    }

    [Test]
    public void Install_CapturesRealLog4NetWrites()
    {
        using var temp = new TempWorkspace("pipeline-capture");
        var store = NewStore();
        using var pipeline = new LogPipeline(store, OptionsWith(temp.Path));
        pipeline.Install();

        LoggingSetup.GetLogger("CkanUI.Tests.Pipeline").Info("pipeline-probe");
        WaitFor(() => store.Count > 0, "log4net 写入没有进内存缓冲");

        var logEvent = store.Snapshot().Events[0];
        Assert.That(logEvent.Message, Is.EqualTo("pipeline-probe"));
        Assert.That(logEvent.Source, Is.EqualTo("log4net:CkanUI.Tests.Pipeline"));
    }

    [Test]
    public void Install_DropsDebugLevelNoise()
    {
        using var temp = new TempWorkspace("pipeline-noise");
        var store = NewStore();
        using var pipeline = new LogPipeline(store, OptionsWith(temp.Path));
        pipeline.Install();

        LoggingSetup.GetLogger("CkanUI.Tests.Pipeline").Debug("debug-noise");
        Thread.Sleep(200);

        Assert.That(store.Count, Is.EqualTo(0), "INFO 阈值下的 DEBUG 噪音不该占缓冲");
    }

    [Test]
    public void Install_WritesRollingFileUnderGivenDirectory()
    {
        using var temp = new TempWorkspace("pipeline-file");
        var directory = temp.Combine("logs");
        using var pipeline = new LogPipeline(NewStore(), OptionsWith(directory, writeFile: true));
        pipeline.Install();

        LoggingSetup.GetLogger("CkanUI.Tests.Pipeline").Info("pipeline-file-probe");
        var expected = Path.Combine(directory, new LogPipelineOptions().ResolveFileName(DateTime.Now));
        WaitFor(() => File.Exists(expected) && ReadShared(expected).Contains("pipeline-file-probe"), "日志文件没有落盘");

        Assert.That(pipeline.FileLogPath, Is.EqualTo(expected));
        Assert.That(InstalledAppenderNames(), Does.Contain("CkanUI.RollingFile"));
        Assert.That(pipeline.Warnings, Is.Empty);
    }

    [Test]
    public void Install_DegradesToMemoryWhenDirectoryIsBlocked()
    {
        using var temp = new TempWorkspace("pipeline-blocked");
        var blocker = temp.Combine("logs");
        File.WriteAllText(blocker, "我是文件不是目录");
        var store = NewStore();
        using var pipeline = new LogPipeline(store, OptionsWith(blocker, writeFile: true));

        var result = pipeline.Install();

        Assert.That(result.IsSuccess, Is.True, "文件写不了只是降级，不算安装失败");
        Assert.That(pipeline.FileLogPath, Is.Null);
        Assert.That(pipeline.Warnings, Is.Not.Empty);
        Assert.That(pipeline.Warnings[0], Does.Contain("降级为仅内存缓冲"));

        LoggingSetup.GetLogger("CkanUI.Tests.Pipeline").Info("still-in-memory");
        WaitFor(() => store.Count > 0, "降级后内存缓冲仍应工作");
    }

    [Test]
    public void Install_IsIdempotent()
    {
        using var temp = new TempWorkspace("pipeline-idempotent");
        using var pipeline = new LogPipeline(NewStore(), OptionsWith(temp.Path, writeFile: true));

        Assert.That(pipeline.Install().IsSuccess, Is.True);
        Assert.That(pipeline.Install().IsSuccess, Is.True);

        Assert.That(InstalledAppenderNames().Count(name => name == "CkanUI.LogStore"), Is.EqualTo(1), "重复安装会让每条日志写两遍");
        Assert.That(InstalledAppenderNames().Count(name => name == "CkanUI.RollingFile"), Is.EqualTo(1));
    }

    [Test]
    public void Install_ReplacesPreviousPipeline()
    {
        using var temp = new TempWorkspace("pipeline-replace");
        using var previous = new LogPipeline(NewStore(), OptionsWith(temp.Path));
        using var current = new LogPipeline(NewStore(), OptionsWith(temp.Path));
        previous.Install();

        current.Install();

        Assert.That(previous.IsInstalled, Is.False);
        Assert.That(LogPipeline.Active, Is.SameAs(current));
        Assert.That(InstalledAppenderNames().Count(name => name == "CkanUI.LogStore"), Is.EqualTo(1));
    }

    [Test]
    public void Dispose_DetachesAppendersAndRestoresThreshold()
    {
        using var temp = new TempWorkspace("pipeline-dispose");
        var hierarchy = Repository;
        hierarchy.Threshold = Level.Warn;
        var pipeline = new LogPipeline(NewStore(), OptionsWith(temp.Path, writeFile: true));
        pipeline.Install();

        pipeline.Dispose();

        Assert.That(pipeline.IsInstalled, Is.False);
        Assert.That(hierarchy.Threshold!.Value, Is.EqualTo(Level.Warn.Value));
        Assert.That(InstalledAppenderNames(), Does.Not.Contain("CkanUI.LogStore"));
        Assert.That(InstalledAppenderNames(), Does.Not.Contain("CkanUI.RollingFile"));
        Assert.That(LogPipeline.Active, Is.Null);
        pipeline.Dispose();
    }

    [Test]
    public void Options_ResolveFallsBackToDefaultDirectory()
    {
        Assert.That(LogPipelineOptions.Resolve(null).LogDirectory, Is.EqualTo(LogPipelineOptions.DefaultLogDirectory));
        Assert.That(LogPipelineOptions.Resolve("   ").LogDirectory, Is.EqualTo(LogPipelineOptions.DefaultLogDirectory));
        Assert.That(LogPipelineOptions.Resolve(@"D:\tmp\ckan").LogDirectory, Is.EqualTo(@"D:\tmp\ckan"));
        Assert.That(LogPipelineOptions.DefaultLogDirectory, Does.EndWith(Path.Combine("CKAN", "logs")));
    }

    [Test]
    public void Options_FileNameCarriesDate()
    {
        var options = new LogPipelineOptions();

        Assert.That(options.ResolveFileName(new DateTime(2026, 3, 7)), Is.EqualTo("ckanui-20260307.log"));
        Assert.That(options.FileSizeLimitBytes, Is.EqualTo(5L * 1024 * 1024));
        Assert.That(options.MaxRollBackups, Is.EqualTo(5));
        Assert.That(LogPipelineOptions.Default.WriteFile, Is.True);
    }

    [Test]
    public void Install_MarksRepositoryConfigured()
    {
        using var temp = new TempWorkspace("pipeline-configured");
        var repo = LogManager.GetRepository(typeof(LogPipeline).Assembly);
        Assert.That(repo.Configured, Is.False, "前置条件：ResetConfiguration 之后仓库是未配置状态");
        using var pipeline = new LogPipeline(NewStore(), OptionsWith(temp.Path));

        pipeline.Install();

        Assert.That(repo.Configured, Is.True, "log4net 3.x 在未配置仓库上会静默丢弃全部日志");
    }

    [Test]
    public void Constructor_RequiresStore() =>
        Assert.Throws<ArgumentNullException>(() => new LogPipeline(null!));
}
