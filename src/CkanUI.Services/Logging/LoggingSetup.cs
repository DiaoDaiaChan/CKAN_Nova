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

    /// <summary>
    /// 随包 log4net.xml 里的目录占位符，配置前替换为 <c>CKANPathUtils.AppDataPath</c>。
    /// 为什么不用 log4net 自带的 `${ENVVAR}` 展开：它在 Windows 上依赖 LOCALAPPDATA 环境变量，
    /// 非 Windows 环境变量名不同、且解析失败时 XmlConfigurator 只写内部日志而不抛异常，
    /// 结果是仓库保持"未配置"、日志静默丢失（实测 WinExe 下磁盘上什么都没有）。
    /// 由代码注入路径可以跨平台且可测试。
    /// </summary>
    public const string LogDirectoryToken = "__CKAN_LOG_DIR__";

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
            ConfigureOnce(Path.Combine(AppContext.BaseDirectory, ConfigFileName), LogDirectory);
        }
    }

    /// <summary>按指定配置文件初始化（测试与特殊入口用）；文件缺失时退回控制台 + WARN，与 Core 行为一致。幂等。</summary>
    public static void Initialize(string configPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);
        lock (Gate)
        {
            ConfigureOnce(configPath, LogDirectory);
        }
    }

    /// <summary>
    /// 按指定配置文件初始化，并显式指定日志目录（覆盖 <see cref="LogDirectoryToken"/>）。
    /// 测试用它把日志写进临时目录，避免污染真实 AppData。幂等。
    /// </summary>
    public static void Initialize(string configPath, string logDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);
        ArgumentException.ThrowIfNullOrEmpty(logDirectory);
        lock (Gate)
        {
            ConfigureOnce(configPath, logDirectory);
        }
    }

    // 默认日志目录：与 Core 完全一致，落在 <LocalApplicationData>/CKAN 下。
    private static string LogDirectory => CKAN.IO.CKANPathUtils.AppDataPath;

    private static void ConfigureOnce(string configPath, string logDirectory)
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
            ConfigureFromFile(repo, file, logDirectory);
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

    /// <summary>
    /// 读取配置文件文本，把 <see cref="LogDirectoryToken"/> 换成真实日志目录后再交给 log4net。
    /// 含占位符时走内存解析，否则保持原有的按文件配置行为。
    /// </summary>
    private static void ConfigureFromFile(ILoggerRepository repo, FileInfo file, string directory)
    {
        var text = File.ReadAllText(file.FullName);
        if (!text.Contains(LogDirectoryToken, StringComparison.Ordinal))
        {
            XmlConfigurator.Configure(repo, file);
            return;
        }

        Directory.CreateDirectory(directory);
        var expanded = text.Replace(LogDirectoryToken, directory.Replace(@"\", "/"), StringComparison.Ordinal);

        var document = new System.Xml.XmlDocument();
        document.LoadXml(expanded);
        if (document.DocumentElement is not { } root)
        {
            throw new System.Xml.XmlException("log4net 配置缺少根元素");
        }

        XmlConfigurator.Configure(repo, root);
    }

    /// <summary>
    /// 配置文件不可用时的兜底：优先用代码挂一个与 log4net.xml 等价的滚动文件 appender
    /// （路径取自 Core 的 CKANPathUtils.AppDataPath，因此非 Windows 也能落盘）；
    /// 连文件都建不出来才退回控制台 + WARN。
    /// </summary>
    private static void ConfigureFallback(ILoggerRepository repo)
    {
        try
        {
            var directory = CKAN.IO.CKANPathUtils.AppDataPath;
            Directory.CreateDirectory(directory);
            var appender = new log4net.Appender.RollingFileAppender
            {
                Name = "CkanFile",
                File = Path.Combine(directory, "CKAN.log"),
                AppendToFile = true,
                RollingStyle = log4net.Appender.RollingFileAppender.RollingMode.Size,
                MaximumFileSize = "1MB",
                MaxSizeRollBackups = 30,
                StaticLogFileName = true,
                LockingModel = new log4net.Appender.FileAppender.MinimalLock(),
                Layout = new log4net.Layout.PatternLayout(
                    "%date{yyyy-MM-dd HH:mm:ss} [%thread] %level %logger - %message%newline%exception"),
            };
            appender.ActivateOptions();
            BasicConfigurator.Configure(repo, appender);
            repo.Threshold = Level.Info;
        }
        catch (Exception)
        {
            BasicConfigurator.Configure(repo);
            repo.Threshold = Level.Warn;
        }
    }
}
