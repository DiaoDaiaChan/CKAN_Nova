using CkanUI.Services.Logging;
using CkanUI.TestSupport;
using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class LoggingSetupTests
{
    [Test]
    public void GetLogger_ReturnsNamedLogger()
    {
        var log = LoggingSetup.GetLogger("CkanUI.Tests");
        Assert.That(log.Logger.Name, Is.EqualTo("CkanUI.Tests"));
    }

    [Test]
    public void Initialize_IsIdempotentAndConfiguresRepository()
    {
        Assert.DoesNotThrow(() => LoggingSetup.Initialize());
        Assert.DoesNotThrow(() => LoggingSetup.Initialize());
        Assert.That(LoggingSetup.IsConfigured, Is.True);
    }

    [Test]
    public void Initialize_WithConfigPath_WritesLogToConfiguredFile()
    {
        using var temp = new TempWorkspace("logging");
        var logPath = temp.Combine("configured.log");
        var configPath = temp.Combine(LoggingSetup.ConfigFileName);
        File.WriteAllText(configPath, $"""
            <log4net><appender name="File" type="log4net.Appender.FileAppender"><file value="{System.Security.SecurityElement.Escape(logPath)}" /><appendToFile value="false" /><layout type="log4net.Layout.PatternLayout"><conversionPattern value="%message%newline" /></layout></appender><root><level value="ALL" /><appender-ref ref="File" /></root></log4net>
            """);
        // log4net 的 Repository.Configured 是粘性标志：同一进程里前面测试已配过 fallback，
        // ConfigureOnce 会直接 return，自定义配置文件永远不生效。这里先显式重置仓库，
        // 断言后恢复，避免测试顺序依赖（CI 上 dotnet test 的 fixture 顺序同样不确定）。
        var repository = LogManager.GetRepository(typeof(LoggingSetup).Assembly);
        LogManager.ResetConfiguration(typeof(LoggingSetup).Assembly);
        try
        {
            LoggingSetup.Initialize(configPath);
            Assert.That(repository.Configured, Is.True);
            LoggingSetup.GetLogger("CkanUI.Tests.File").Info("settings-log-probe");
            Assert.That(File.Exists(logPath), Is.True);

            // FileAppender 以 FileShare.Read 独占打开日志文件，直接 File.ReadAllText 会 IOException。
            // 这里用 FileShare.ReadWrite 共享读，既验证落盘又不依赖 appender 何时 Close。
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Assert.That(reader.ReadToEnd(), Does.Contain("settings-log-probe"));
        }
        finally
        {
            LogManager.ResetConfiguration(typeof(LoggingSetup).Assembly);
            LoggingSetup.Initialize();
        }
    }
}
