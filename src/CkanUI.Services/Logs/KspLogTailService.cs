using System.Text;

using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 轮询式 KSP.log 跟随器（14 篇 §3.2）。设计取舍：
/// 1. 轮询而不是 <c>FileSystemWatcher</c>：Unity 写日志是持续追加 + 频繁 flush，watcher 的 Changed 会合并/丢事件，
///    而 750ms 轮询的成本只是 stat + 一次 seek，换来「绝不丢行」的确定性。
/// 2. 只读共享打开（ReadWrite|Delete）：游戏正在写、CKAN 同时在读，任何独占打开都会让玩家看到一半日志。
/// 3. 按字节偏移续读并保留半行：UTF-8 多字节字符可能被缓冲区边界劈开，直接按 StreamReader.ReadLine 会吐乱码。
/// </summary>
public sealed class KspLogTailService : IKspLogTailService
{
    /// <summary>默认轮询间隔：低于 500ms 对玩家没有感知收益，只是多烧 IO。</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(750);

    /// <summary>首次跟随只回看末尾这么多字节：老日志可能有几十 MB，全量灌进环形缓冲只会挤掉 GUI 日志。</summary>
    public const int InitialTailBytes = 256 * 1024;

    /// <summary>单次最多读这么多字节，避免用户手动把日志换成大文件时一次卡住后台线程。</summary>
    private const int MaxBytesPerPump = 4 * 1024 * 1024;

    private const int MaxLinesPerPump = 4000;

    private readonly ILogStore _store;
    private readonly Lock _gate = new();

    /// <summary>
    /// 泵互斥：定时器会立即起跳一次，用户又可能同时点「导入已有日志」，
    /// 两个泵若同时读到同一个偏移，同一行会被写进缓冲两次（日志里凭空多出一条错误，排障就废了）。
    /// </summary>
    private readonly SemaphoreSlim _pumpGate = new(1, 1);
    private readonly Timer _timer;
    private readonly TimeSpan _interval;
    private readonly List<byte> _pending = new(4096);

    private string? _instanceName;
    private string? _gameDirectory;
    private string? _path;
    private long _offset;
    private long _imported;
    private string? _lastError;
    private bool _skipFirstPartialLine;

    /// <summary>构造并开始轮询（<paramref name="pollInterval"/> 仅测试用）。</summary>
    public KspLogTailService(ILogStore store, TimeSpan? pollInterval = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _interval = pollInterval ?? DefaultPollInterval;
        _timer = new Timer(_ => _ = PumpAsync(), null, Timeout.InfiniteTimeSpan, _interval);
    }

    /// <inheritdoc />
    public string? InstanceName
    {
        get
        {
            lock (_gate)
            {
                return _instanceName;
            }
        }
    }

    /// <inheritdoc />
    public string? GameLogPath
    {
        get
        {
            lock (_gate)
            {
                return _path;
            }
        }
    }

    /// <inheritdoc />
    public bool IsWatching
    {
        get
        {
            lock (_gate)
            {
                return _path is not null;
            }
        }
    }

    /// <inheritdoc />
    public long ImportedLineCount
    {
        get
        {
            lock (_gate)
            {
                return _imported;
            }
        }
    }

    /// <inheritdoc />
    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public void Attach(string? gameDirectory, string? instanceName)
    {
        var path = ResolveLogPath(gameDirectory);
        bool changed;
        lock (_gate)
        {
            changed = !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_instanceName, instanceName, StringComparison.Ordinal)
                || !string.Equals(_gameDirectory, gameDirectory, StringComparison.OrdinalIgnoreCase);
            _instanceName = string.IsNullOrWhiteSpace(instanceName) ? null : instanceName;
            _gameDirectory = string.IsNullOrWhiteSpace(gameDirectory) ? null : gameDirectory.Trim();
            _path = path;
            _offset = 0;
            _pending.Clear();
            _imported = 0;
            _lastError = null;
            _skipFirstPartialLine = false;
        }

        if (path is not null)
        {
            // 从末尾窗口起步：只把「最近发生了什么」灌进来，用户在日志页能立刻看到刚发生的错误。
            try
            {
                var length = new FileInfo(path).Length;
                var start = Math.Max(0, length - InitialTailBytes);
                lock (_gate)
                {
                    _offset = start;
                    _skipFirstPartialLine = start > 0;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetError(ex.Message);
            }
        }

        // 没有实例目录就别空转；有目录但 KSP.log 还没生成时仍要保持轮询，游戏一写日志就能自动接上。
        if (_gameDirectory is null)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _timer.Change(TimeSpan.Zero, _interval);
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Detach()
    {
        bool wasWatching;
        lock (_gate)
        {
            wasWatching = _path is not null;
            _path = null;
            _instanceName = null;
            _gameDirectory = null;
            _offset = 0;
            _pending.Clear();
            _skipFirstPartialLine = false;
        }

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (wasWatching)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public async Task<int> ImportFromStartAsync(CancellationToken cancellationToken = default)
    {
        string? path;
        lock (_gate)
        {
            path = _path;
            _offset = 0;
            _pending.Clear();
            _skipFirstPartialLine = false;
        }

        if (path is null)
        {
            return 0;
        }

        // 一直读到文件末尾（单次预算 4MB，大日志会分几轮），中途没有进展就停，避免空转。
        var total = 0;
        while (total < 20000)
        {
            var written = await PumpAsync(cancellationToken).ConfigureAwait(false);
            if (written <= 0)
            {
                break;
            }

            total += written;
        }

        return total;
    }

    /// <inheritdoc />
    public async Task<int> PumpAsync(CancellationToken cancellationToken = default)
    {
        await _pumpGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PumpCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pumpGate.Release();
        }
    }

    private async Task<int> PumpCoreAsync(CancellationToken cancellationToken)
    {
        string? path;
        long offset;
        string? instance;
        string? gameDirectory;
        lock (_gate)
        {
            path = _path;
            offset = _offset;
            instance = _instanceName;
            gameDirectory = _gameDirectory;
        }

        if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(gameDirectory))
        {
            // 游戏刚启动、KSP.log 才被创建：这里补一次定位，用户不用切实例来「刷新」跟随目标。
            path = ResolveLogPath(gameDirectory);
            if (path is not null)
            {
                lock (_gate)
                {
                    _path = path;
                    _offset = 0;
                    _pending.Clear();
                    _skipFirstPartialLine = false;
                }
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            return 0;
        }

        int appended;
        try
        {
            appended = await ReadIncrementAsync(path, instance, cancellationToken).ConfigureAwait(false);
            if (appended > 0 || LastError is not null)
            {
                ClearError();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            SetError(ex.Message);
            return 0;
        }

        if (appended > 0)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        return appended;
    }

    private async Task<int> ReadIncrementAsync(string path, string? instance, CancellationToken cancellationToken)
    {
        long offset;
        lock (_gate)
        {
            offset = _offset;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var length = stream.Length;
        if (length < offset)
        {
            // 日志被清空/轮转（游戏重启会覆盖）：重新从头读，否则新日志永远追不上。
            lock (_gate)
            {
                offset = 0;
                _offset = 0;
                _pending.Clear();
                _skipFirstPartialLine = false;
            }
        }

        if (length == offset)
        {
            return 0;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var budget = (int)Math.Min(MaxBytesPerPump, length - offset);
        var buffer = new byte[budget];
        var read = 0;
        while (read < budget)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, budget - read), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        lock (_gate)
        {
            _offset = offset + read;
        }

        if (read == 0)
        {
            return 0;
        }

        string complete;
        bool skipFirst;
        lock (_gate)
        {
            _pending.AddRange(buffer.AsSpan(0, read).ToArray());
            var lastNewline = _pending.LastIndexOf((byte)'\n');
            if (lastNewline < 0)
            {
                return 0;
            }

            var bytes = _pending.GetRange(0, lastNewline + 1).ToArray();
            _pending.RemoveRange(0, lastNewline + 1);
            complete = Encoding.UTF8.GetString(bytes);
            skipFirst = _skipFirstPartialLine;
            _skipFirstPartialLine = false;
        }

        var lines = complete.Split('\n');
        var drafts = new List<LogEvent>(Math.Min(lines.Length, MaxLinesPerPump));
        var now = DateTime.Now;
        foreach (var raw in lines)
        {
            if (drafts.Count >= MaxLinesPerPump)
            {
                break;
            }

            if (skipFirst)
            {
                // 从 256KB 处起步时第一行很可能是半截，丢掉；否则会把一句被截断的话当成日志。
                skipFirst = false;
                continue;
            }

            if (!KspLogLineParser.TryParse(raw, now, out var line))
            {
                continue;
            }

            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(instance))
            {
                tags["instance"] = instance;
            }

            if (!string.IsNullOrEmpty(line.Module))
            {
                tags["module"] = line.Module;
            }

            if (line.IsContinuation)
            {
                tags["game.cont"] = "1";
            }

            drafts.Add(LogEvent.Create(
                LogSourceKind.Game,
                line.Level,
                LogSources.Encode(LogSourceKind.Game, instance),
                line.Message,
                tags: tags) with
            {
                TimeLocal = line.TimeStamp ?? now,
            });
        }

        if (drafts.Count == 0)
        {
            return 0;
        }

        var written = _store.AppendRange(drafts);
        lock (_gate)
        {
            _imported += written;
        }

        return written;
    }

    /// <summary>拼出实例的 KSP.log 路径；目录不存在或文件缺失都返回 null（UI 据此提示「尚未生成」）。</summary>
    public static string? ResolveLogPath(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        try
        {
            var dir = gameDirectory.Trim();
            if (!Directory.Exists(dir))
            {
                return null;
            }

            var candidate = Path.Combine(dir, "KSP.log");
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void SetError(string message)
    {
        var raise = false;
        lock (_gate)
        {
            if (!string.Equals(_lastError, message, StringComparison.Ordinal))
            {
                _lastError = message;
                raise = true;
            }
        }

        if (raise)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClearError()
    {
        var raise = false;
        lock (_gate)
        {
            if (_lastError is not null)
            {
                _lastError = null;
                raise = true;
            }
        }

        if (raise)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Dispose();
    }
}
