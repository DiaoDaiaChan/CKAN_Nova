using CkanUI.Services.Abstractions;

using CkanUI.Services.Abstractions.Logs;

using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository;
using log4net.Repository.Hierarchy;

namespace CkanUI.Services.Logs;

/// <summary>
/// 日志管线（14 篇 §3.2 ① / §7）：往 Core 已在用的同一个 log4net 仓库挂两件事——
/// 内存 appender（喂日志页）与自建滚动文件 appender（落 <c>%LocalAppData%\CKAN\logs</c>）。
/// </summary>
/// <remarks>
/// 三条纪律：
/// 1. 不依赖 <c>log4net.xml</c>、不依赖当前工作目录：配置全在代码里，Core 一行不改，打包漏文件也不会丢日志。
/// 2. 只把阈值抬到 INFO，绝不把用户显式设的 DEBUG/ALL 降下来（那是他在排障）。
/// 3. 单例安装：同进程只允许一套 appender，重复安装会让每条日志写两遍（内存缓冲直接减半）。
/// </remarks>
public sealed class LogPipeline : IDisposable
{
    private static readonly Lock InstallGate = new();

    private static LogPipeline? _active;

    private readonly ILogStore _store;
    private readonly LogPipelineOptions _options;
    private readonly List<IAppender> _attached = [];
    private readonly List<string> _warnings = [];

    private Hierarchy? _hierarchy;
    private Level? _previousThreshold;
    private bool _disposed;

    /// <summary>构造管线（日志目录可覆盖，测试用它指到临时目录）。</summary>
    public LogPipeline(ILogStore store, LogPipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _options = options ?? LogPipelineOptions.Default;
    }

    /// <summary>本进程当前生效的管线（未安装时为 null）。</summary>
    public static LogPipeline? Active => Volatile.Read(ref _active);

    /// <summary>实际落盘的日志文件路径（未写文件时为 null）。</summary>
    public string? FileLogPath { get; private set; }

    /// <summary>安装过程中的非致命问题（文件 appender 建不出来等），会在 UI/诊断包里展示。</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>是否已安装。</summary>
    public bool IsInstalled { get; private set; }

    /// <summary>
    /// 安装 appender。只有「仓库不是 Hierarchy（无法挂 appender）」才算失败；
    /// 文件写不了只是降级（内存缓冲照常工作），原因进 <see cref="Warnings"/>。
    /// </summary>
    public OperationResult Install()
    {
        lock (InstallGate)
        {
            if (IsInstalled)
            {
                return OperationResult.Ok();
            }

            if (_active is { } previous && !ReferenceEquals(previous, this))
            {
                previous.Dispose();
            }

            if (LogManager.GetRepository(typeof(LogPipeline).Assembly) is not Hierarchy hierarchy)
            {
                return OperationResult.Fail("log4net 仓库不是 Hierarchy，无法挂载 appender");
            }

            _hierarchy = hierarchy;
            _previousThreshold = hierarchy.Threshold;

            // 只在阈值比 INFO 更「安静」时抬到 INFO：Core 默认 WARN，我们要看到 INFO；
            // 但用户/开发者显式设成 DEBUG 时不能改回去，否则排障时反而丢信息。
            var current = hierarchy.Threshold;
            if (current is null || current.Value > Level.Info.Value)
            {
                hierarchy.Threshold = Level.Info;
            }

            var storeAppender = new LogStoreAppender(_store) { Name = "CkanUI.LogStore" };
            storeAppender.ActivateOptions();
            hierarchy.Root.AddAppender(storeAppender);
            _attached.Add(storeAppender);

            if (_options.WriteFile)
            {
                AttachFileAppender(hierarchy);
            }

            // log4net 3.x 在仓库没标记「已配置」时会把整条日志链短路（LoggerImpl.IsEnabledFor 恒为 false），
            // 现象是 appender 挂上了、阈值也对，却一条日志都收不到。走到这里 appender 与阈值都已设好，
            // 这就是「已配置」的事实，必须置位；否则找不到 log4net.xml 的启动路径会把日志全丢。
            if (!hierarchy.Configured)
            {
                hierarchy.Configured = true;
            }

            IsInstalled = true;
            Volatile.Write(ref _active, this);
            return OperationResult.Ok();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (InstallGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_hierarchy is { } hierarchy)
            {
                foreach (var appender in _attached)
                {
                    try
                    {
                        hierarchy.Root.RemoveAppender(appender);
                        appender.Close();
                    }
                    catch (Exception)
                    {
                        // 拆 appender 失败只可能是仓库已被重置，不影响退出流程。
                    }
                }

                if (_previousThreshold is { } threshold)
                {
                    hierarchy.Threshold = threshold;
                }
            }

            _attached.Clear();
            IsInstalled = false;
            if (ReferenceEquals(Volatile.Read(ref _active), this))
            {
                Volatile.Write(ref _active, null);
            }
        }
    }

    private void AttachFileAppender(Hierarchy hierarchy)
    {
        try
        {
            Directory.CreateDirectory(_options.LogDirectory);
            var path = Path.Combine(_options.LogDirectory, _options.ResolveFileName(DateTime.Now));
            var appender = new RollingFileAppender
            {
                Name = "CkanUI.RollingFile",
                File = path,
                AppendToFile = true,
                StaticLogFileName = true,
                RollingStyle = RollingFileAppender.RollingMode.Size,
                MaximumFileSize = _options.FileSizeLimitBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                MaxSizeRollBackups = _options.MaxRollBackups,
                LockingModel = new FileAppender.MinimalLock(),
                Threshold = Level.Info,
                Layout = new PatternLayout("%date{yyyy-MM-dd HH:mm:ss} [%thread] %-5level %logger - %message%newline%exception"),
            };
            appender.ActivateOptions();
            hierarchy.Root.AddAppender(appender);
            _attached.Add(appender);
            FileLogPath = path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 只读介质 / 权限不足：日志页仍然要用，降级为仅内存并记录原因。
            _warnings.Add($"日志文件不可写，已降级为仅内存缓冲：{exception.Message}");
        }
    }
}
