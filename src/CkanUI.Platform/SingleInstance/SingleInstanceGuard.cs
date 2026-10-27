using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace CkanUI.Platform.SingleInstance;

/// <summary>
/// 单实例守卫 + 命令行参数转发（ARC-05，验收「双击两次只有一个窗口」）。
/// 命名互斥量判唯一性，命名管道把第二次启动的参数（含 <c>ckan://</c> URI）交给首个实例。
/// </summary>
/// <remarks>
/// 用「创建即拥有」而不是 WaitOne/ReleaseMutex：Mutex 的所有权是线程亲和的，
/// 而 Dispose 往往发生在另一个线程（UI 线程退出时），ReleaseMutex 会抛 ApplicationException。
/// 只要还有句柄存活，Mutex 对象就存在；进程退出（哪怕崩溃）时句柄由系统关闭，对象随之销毁，
/// 因此不需要处理 AbandonedMutexException，也不会残留僵尸锁。
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexPrefix = @"Local\CkanUI.SingleInstance.";
    private const string PipePrefix = "CkanUI.SingleInstance.";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _cancellation;
    private Thread? _listener;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>收到另一实例转发的参数时在后台线程触发；调用方负责封送到 UI 线程。</summary>
    public event Action<IReadOnlyList<string>>? ArgumentsReceived;

    /// <summary>
    /// 尝试成为唯一实例。成功返回守卫（正常情况下进程生命周期内不释放）；已有实例在跑则返回 null，
    /// 调用方应接着用 <see cref="TryForwardToOwner"/> 把参数交出去，然后退出。
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        var mutex = new Mutex(initiallyOwned: true, MutexPrefix + appId, out var createdNew);
        if (!createdNew)
        {
            // 已经把所有权交给别的实例，本进程不持有互斥量，立刻释放句柄
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex, PipePrefix + appId);
    }

    /// <summary>把参数转发给正在监听的唯一实例。连不上（对方还没起好管道）返回 false。</summary>
    public static bool TryForwardToOwner(string appId, IReadOnlyList<string> args, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(args);

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            try
            {
                using var client = new NamedPipeClientStream(
                    ".", PipePrefix + appId, PipeDirection.Out, PipeOptions.Asynchronous);
                client.Connect((int)Math.Max(1, remaining.TotalMilliseconds));

                using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                foreach (var arg in args)
                {
                    writer.WriteLine(arg);
                }

                writer.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                // 对方实例还在启动（Mutant 已存在但管道未就绪）：稍等再试，直到总超时耗尽
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>开始监听转发来的参数。可重复调用，只有第一次生效。</summary>
    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _listener = new Thread(() => ListenLoop(token))
        {
            IsBackground = true,
            Name = "CKAN.UI single-instance listener",
        };
        _listener.Start();
    }

    private void ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // 取消时把 WaitForConnection 打断，否则线程会一直挂到下一个实例出现
                using var registration = token.Register(DisposeQuietly(server));
                server.WaitForConnection();

                var received = new List<string>();
                using (var reader = new StreamReader(server, Encoding.UTF8))
                {
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        received.Add(line);
                    }
                }

                if (received.Count > 0)
                {
                    ArgumentsReceived?.Invoke(received);
                }
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // 对端提前断开是正常情况（转发的进程可能被用户杀掉），继续监听下一个
                Thread.Sleep(50);
            }
            catch (ObjectDisposedException) when (token.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private static Action DisposeQuietly(NamedPipeServerStream server) => () =>
    {
        try
        {
            server.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // 已经关掉了，忽略
        }
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();

        // Join 自己会死锁：只有在别的线程上 Dispose 时才等监听线程收尾
        if (_listener is not null && _listener != Thread.CurrentThread)
        {
            _listener.Join(TimeSpan.FromSeconds(1));
        }

        _cancellation?.Dispose();
        _mutex.Dispose();
    }
}
