using System.Reflection;
using log4net;
using log4net.Config;
using log4net.Core;
using log4net.Repository;

namespace CkanUI.Services.Logging;

/// <summary>
/// 日志落位（01 篇 §7 / 02 篇 §2）：与 Core 共用同一个 log4net 仓库与 appender，
/// 新 UI 只通过 <see cref="UiLogger"/>（"CKAN.UI"）写入，不另开日志文件。
/// </summary>
public static class LoggingSetup
{
    /// <summary>新 UI 统一使用的 logger 名（02 篇 §2「复用」）。</summary>
    public const string UiLoggerName = "CKAN.UI";

    /// <summary>随包分发的配置文件名，与 Core 的 Logging.Initialize 查找的文件同名。</summary>
    public const string ConfigFileName = "log4net.xml";

    // 配置动作必须串行：两个线程同时看到 Configured=false 会各配一次，appender 翻倍。
    private static readonly Lock Gate = new();

    /// <summary>新 UI 的 logger，写入 Core 已配置的同一组 appender。</summary>
    public static ILog UiLogger => LogManager.GetLogger(UiLoggerName);

    /// <summary>log4net 是否已配置（无论是本类还是 Core 的 Logging.Initialize 配置的）。</summary>
    public static bool IsConfigured => Repository.Configured;

    // 默认仓库选择器下所有程序集共用同一个仓库，Core 的 Logging.Initialize 检查的也是它，
    // 所以这里配置过后 Core 那边会因 Configured=true 直接跳过，不会重复挂 appender。
    private static ILoggerRepository Repository => LogManager.GetRepository(typeof(LoggingSetup).Assembly);

    public static ILog GetLogger(string name) => LogManager.GetLogger(name);

    /// <summary>
    /// 启动最早期调用：先把 CWD 拉回程序目录（快捷方式 / ckan:// 启动时 CWD 不可控，
    /// 而 Core 的 Logging.Initialize 只从 CWD 找 log4net.xml），再按程序目录下的 log4net.xml 配置。幂等。
    /// </summary>
    public static void Initialize()
    {
        lock (Gate)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            ConfigureOnce(Path.Combine(AppContext.BaseDirectory, ConfigFileName));
        }
    }

    /// <summary>按指定配置文件初始化（测试与特殊入口用）；文件缺失时退回控制台 + WARN，与 Core 行为一致。幂等。</summary>
    public static void Initialize(string configPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);
        lock (Gate)
        {
            ConfigureOnce(configPath);
        }
    }

    private static void ConfigureOnce(string configPath)
    {
        var repo = Repository;
        if (repo.Configured)
        {
            return;
        }

        var file = new FileInfo(configPath);
        if (!file.Exists)
        {
            ConfigureFallback(repo);
            return;
        }

        try
        {
            XmlConfigurator.Configure(repo, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or TargetInvocationException)
        {
            // 配置文件坏了也不能让启动失败：退回基础配置，并把原因写进日志。
            ConfigureFallback(repo);
            UiLogger.Warn($"log4net 配置文件加载失败，已退回基础配置：{configPath}", ex);
        }

        // XmlConfigurator 解析失败时只记内部日志而不抛异常，这里兜底保证"已配置"语义成立。
        if (!repo.Configured)
        {
            ConfigureFallback(repo);
        }
    }

    private static void ConfigureFallback(ILoggerRepository repo)
    {
        BasicConfigurator.Configure(repo);
        repo.Threshold = Level.Warn;
    }
}
