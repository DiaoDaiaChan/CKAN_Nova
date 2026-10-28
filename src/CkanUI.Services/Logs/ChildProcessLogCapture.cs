using System.Diagnostics;
using System.Globalization;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 子进程输出采集（14 篇 §3.2 ④）：按行把 <c>ckan.exe</c> 的 stdout/stderr 变成事件。
/// 为什么要采集：求解/安装策略可能跑在子进程里，那部分输出不进本进程的 log4net，没有这一路日志页就是空白。
/// </summary>
public sealed class ChildProcessLogCapture : IDisposable
{
    private readonly ILogStore _store;
    private readonly List<(Process Process, DataReceivedEventHandler Output, DataReceivedEventHandler Error)> _attached = [];
    private bool _disposed;

    /// <summary>构造采集器。</summary>
    public ChildProcessLogCapture(ILogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>已挂钩的进程数。</summary>
    public int AttachedCount => _attached.Count;

    /// <summary>挂钩一个已启动且重定向了输出的进程。</summary>
    public OperationResult Attach(Process process, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!process.StartInfo.RedirectStandardOutput && !process.StartInfo.RedirectStandardError)
        {
            return OperationResult.Fail("进程未重定向标准输出/错误，无法采集");
        }

        var source = LogSources.Encode(LogSourceKind.Child, process.Id.ToString(CultureInfo.InvariantCulture));
        DataReceivedEventHandler output = (_, e) => WriteLine(source, e.Data, isError: false, correlationId);
        DataReceivedEventHandler error = (_, e) => WriteLine(source, e.Data, isError: true, correlationId);

        if (process.StartInfo.RedirectStandardOutput)
        {
            process.OutputDataReceived += output;
            process.BeginOutputReadLine();
        }

        if (process.StartInfo.RedirectStandardError)
        {
            process.ErrorDataReceived += error;
            process.BeginErrorReadLine();
        }

        _attached.Add((process, output, error));
        return OperationResult.Ok();
    }

    /// <summary>摘钩（进程退出后调用，避免 DataReceived 回调持有已释放对象）。</summary>
    public void Detach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        for (var i = _attached.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_attached[i].Process, process))
            {
                continue;
            }

            var entry = _attached[i];
            try
            {
                entry.Process.OutputDataReceived -= entry.Output;
                entry.Process.ErrorDataReceived -= entry.Error;
                entry.Process.CancelOutputRead();
                entry.Process.CancelErrorRead();
            }
            catch (Exception)
            {
                // 进程已退出/已释放时摘钩会抛 InvalidOperationException，忽略即可。
            }

            _attached.RemoveAt(i);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var i = _attached.Count - 1; i >= 0; i--)
        {
            Detach(_attached[i].Process);
        }
    }

    /// <summary>按行判定级别：stderr 默认警告，含错误关键词升级为错误（真实 ckan.exe 会把非致命告警也写到 stderr）。</summary>
    public static LogLevel LevelFor(bool isError, string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return LogLevel.Info;
        }

        if (ContainsAny(line, "error", "exception", "fatal", "failed", "failure"))
        {
            return LogLevel.Error;
        }

        if (isError || ContainsAny(line, "warn", "deprecated"))
        {
            return LogLevel.Warn;
        }

        return LogLevel.Info;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void WriteLine(string source, string? line, bool isError, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(line) || _disposed)
        {
            return;
        }

        try
        {
            _store.Append(LogEvent.Create(
                LogSourceKind.Child,
                LevelFor(isError, line),
                source,
                line,
                null,
                null,
                correlationId));
        }
        catch (Exception)
        {
            // 子进程管道回调里抛异常会终止读取线程，必须吞掉。
            System.Diagnostics.Debug.WriteLine("ChildProcessLogCapture 写入失败");
        }
    }
}
