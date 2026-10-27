using CkanUI.Services.Logging;
using CkanUI.TestSupport;
using log4net;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 随包发布的 src/CkanUI.App/log4net.xml 必须能被 log4net 解析并落盘。
/// 真实故障背景：该文件曾用 NLog 语法 %envFolderPath{...} 写 file 值，
/// XmlConfigurator.Configure 只会静默失败（仓库保持未配置），
/// 于是 BasicConfigurator 退回控制台 appender，WinExe 无控制台 → 日志全部丢失、磁盘上什么都没有。
/// 现方案：配置里留 __CKAN_LOG_DIR__ 占位符，由 LoggingSetup 在配置前替换为真实日志目录。
/// </summary>
[TestFixture]
public sealed class ShippedLogConfigTests
{
    [Test]
    public void ShippedAppConfig_IsParseableByLog4net_AndWritesFile()
    {
        var shipped = Path.Combine(TempWorkspace.RepositoryRootPath, "src", "CkanUI.App", LoggingSetup.ConfigFileName);
        Assert.That(File.Exists(shipped), Is.True, "随包日志配置不存在: " + shipped);

        using var temp = new TempWorkspace("shipped-logcfg");
        var logDirectory = temp.Combine("appdata");

        var raw = File.ReadAllText(shipped);
        Assert.That(raw, Does.Contain(LoggingSetup.LogDirectoryToken),
            "随包配置应使用 __CKAN_LOG_DIR__ 占位符，而不是 %envFolderPath / ${ENVVAR} 之类无法跨平台解析的写法");

        var configPath = temp.Combine("log4net.xml");
        File.WriteAllText(configPath, raw);

        // log4net.xml 的 file 值是 __CKAN_LOG_DIR__/CKAN.log，占位符换成 logDirectory 本身
        // （真实运行时 logDirectory 已经是 <LocalApplicationData>/CKAN）。
        var logPath = Path.Combine(logDirectory, "CKAN.log");
        var repository = LogManager.GetRepository(typeof(LoggingSetup).Assembly);
        LogManager.ResetConfiguration(typeof(LoggingSetup).Assembly);

        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var captured = new StringWriter();
        try
        {
            // log4net 解析失败时只写内部日志（默认吞掉），捕获出来便于定位。
            log4net.Util.LogLog.InternalDebugging = true;
            Console.SetOut(captured);
            Console.SetError(captured);

            LoggingSetup.Initialize(configPath, logDirectory);

            Console.SetOut(originalOut);
            Console.SetError(originalError);
            log4net.Util.LogLog.InternalDebugging = false;

            Assert.That(repository.Configured, Is.True, "配置未生效（log4net 内部日志：" + captured + "）");
            LoggingSetup.GetLogger("CkanUI.Tests.ShippedConfig").Info("shipped-config-probe");
            Assert.That(File.Exists(logPath), Is.True,
                "日志文件未生成: " + logPath + "（log4net 内部日志：" + captured + "）");

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Assert.That(reader.ReadToEnd(), Does.Contain("shipped-config-probe"));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            log4net.Util.LogLog.InternalDebugging = false;
            LogManager.ResetConfiguration(typeof(LoggingSetup).Assembly);
            LoggingSetup.Initialize();
        }
    }
}
