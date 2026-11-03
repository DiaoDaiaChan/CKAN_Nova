using System.Globalization;

namespace CkanUI.Plugin.Performance.Telemetry;

/// <summary>遥测探针状态（契约 §1 的"探针判定"三段 + §6 的兼容性）。</summary>
public enum KppTelemetryState
{
    /// <summary>没有选中 KSP 实例。</summary>
    NoInstance,

    /// <summary>没有遥测目录：Mod 未安装。</summary>
    ModMissing,

    /// <summary>
    /// 遥测目录不在，但游戏目录里找到了 KPP 本体：装的是**不带文件桥的旧版本**。
    /// <para>
    /// 与 <see cref="ModMissing"/> 分开是有原因的：Mod 没装，用户要做的是「去装」；装的是旧版，
    /// 用户要做的是「升级」。同一句话糊过去，用户会去重装一遍同一个旧版本。
    /// </para>
    /// </summary>
    ModOutdated,

    /// <summary>目录在、status 在，但从来读不到可用的 latest.json。</summary>
    NoData,

    /// <summary>能读到快照，但发布时刻超过 3 倍发布周期：游戏已暂停或已退出。</summary>
    Stale,

    /// <summary>数据新鲜且写方存活。</summary>
    Online,

    /// <summary>schema 主版本不同：不解析，只给灰条提示。</summary>
    Incompatible,

    /// <summary>读文件本身失败（权限、文件被独占、磁盘错误）。</summary>
    ReadError,
}

/// <summary>一轮轮询的结果。</summary>
/// <param name="State">探针状态。</param>
/// <param name="Snapshot">最近一份**好**的快照；解析失败的轮次不清空它，因此可能比 <paramref name="State"/> 旧。</param>
/// <param name="Status">status.json 的健康度；读不到为 <c>null</c>。</param>
/// <param name="TelemetryDirectory">本轮盯的遥测目录。</param>
/// <param name="AgeMs">快照发布时刻距今毫秒；无快照为 <c>null</c>。</param>
/// <param name="Detail">可显示的原因文案（为什么不是 Online）。</param>
/// <param name="PollCount">第几轮轮询（UI 用来判断"是不是一直没变"）。</param>
public sealed record KppTelemetryUpdate(
    KppTelemetryState State,
    KppTelemetrySnapshot? Snapshot,
    KppStatus? Status,
    string? TelemetryDirectory,
    double? AgeMs,
    string? Detail,
    long PollCount);

/// <summary>
/// latest.json / status.json 的轮询客户端（契约 §1–§3）。
/// </summary>
/// <remarks>
/// <para>所有磁盘读取都在 <see cref="System.Threading.Timer"/> 的回调线程上完成，并且每一层都有 try/catch：
/// 解析失败 = 本轮无数据，抛不到 UI 线程，也不清空上一份好数据（§2 的读方纪律）。</para>
/// <para>事件在后台线程触发，视图模型负责封送回 UI 线程（视图模型层不引用 Avalonia 的 Dispatcher）。</para>
/// </remarks>
public sealed class KppTelemetryClient : IDisposable
{
    /// <summary>默认轮询周期；KPP 默认发布周期也是 1000ms。</summary>
    public const int DefaultPollIntervalMs = 1000;

    /// <summary>陈旧判定倍数（§1：publisherAgeMs &gt; 3×publishIntervalMs 即视为陈旧）。</summary>
    public const double StaleMultiplier = 3d;

    private readonly object _gate = new();
    private readonly Timer _timer;
    private readonly TimeSpan _pollInterval;
    private string? _telemetryDir;
    private KppTelemetrySnapshot? _lastGood;
    private KppStatus? _lastStatus;
    private KppTelemetryUpdate? _latest;
    private long _pollCount;
    private int _polling;
    private bool _disposed;

    /// <summary>构造。</summary>
    /// <param name="telemetryDir">遥测根目录；<c>null</c> 表示还没选实例。</param>
    /// <param name="pollInterval">轮询周期，默认 1 秒。</param>
    public KppTelemetryClient(string? telemetryDir, TimeSpan? pollInterval = null)
    {
        _telemetryDir = Normalize(telemetryDir);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(DefaultPollIntervalMs);
        _timer = new Timer(static state => ((KppTelemetryClient)state!).Poll(), this, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>每轮轮询后触发（后台线程）。订阅者异常不会打断轮询。</summary>
    public event Action<KppTelemetryUpdate>? Updated;

    /// <summary>当前盯的遥测目录。</summary>
    public string? TelemetryDirectory => Volatile.Read(ref _telemetryDir);

    /// <summary>最近一轮结果。</summary>
    public KppTelemetryUpdate? Latest => Volatile.Read(ref _latest);

    /// <summary>开始轮询。</summary>
    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Change(0, (long)_pollInterval.TotalMilliseconds);
    }

    /// <summary>停止轮询（幂等）。</summary>
    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 换目标实例：清空上一实例的好数据，立即重探一次。
    /// </summary>
    /// <param name="telemetryDir">新遥测目录；<c>null</c> = 未选择实例。</param>
    public void Retarget(string? telemetryDir)
    {
        var normalized = Normalize(telemetryDir);
        lock (_gate)
        {
            if (string.Equals(_telemetryDir, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 换实例就是换游戏：上一实例的快照与健康度都不再代表任何东西，必须清掉。
            _telemetryDir = normalized;
            _lastGood = null;
            _lastStatus = null;
            _latest = null;
            _pollCount = 0;
        }

        Poll();
    }

    /// <summary>立即轮询一次（同步、后台安全；用于"刷新"按钮）。</summary>
    public void Poll()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var update = PollOnce();
            Volatile.Write(ref _latest, update);
            Raise(update);
        }
        catch (Exception)
        {
            // 轮询线程上的任何意外都必须在这里止住：这里再往上就是进程级未处理异常。
            RaiseSafe();
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    /// <summary>释放定时器。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
    }

    /// <summary>
    /// 遥测目录不在时，看看旁边是不是躺着 KPP 本体（旧版没有 Telemetry 子目录）。
    /// </summary>
    /// <remarks>
    /// 判据用 KPP 自己的目录特征（<c>Plugins/</c> 或 <c>*.version</c>），不用「父目录存在」——
    /// 后者会把 <c>%TEMP%</c> 这类本来就存在的目录误判成「装过 Mod」，把「没装」说成「版本旧」。
    /// </remarks>
    private static string? FindInstalledModDirectory(string telemetryDir)
    {
        string? modDir;
        try
        {
            modDir = Path.GetDirectoryName(telemetryDir.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(modDir) || !Directory.Exists(modDir))
        {
            return null;
        }

        try
        {
            if (Directory.Exists(Path.Combine(modDir, "Plugins")))
            {
                return modDir;
            }

            return Directory.GetFiles(modDir, "*.version").Length > 0 ? modDir : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private KppTelemetryUpdate PollOnce()
    {
        var count = Interlocked.Increment(ref _pollCount);
        var dir = TelemetryDirectory;
        if (dir is null)
        {
            return new KppTelemetryUpdate(KppTelemetryState.NoInstance, _lastGood, null, null, null, "未选择 KSP 实例", count);
        }

        try
        {
            if (!Directory.Exists(dir))
            {
                var modDir = FindInstalledModDirectory(dir);
                if (modDir is not null)
                {
                    return new KppTelemetryUpdate(
                        KppTelemetryState.ModOutdated,
                        null,
                        null,
                        dir,
                        null,
                        "找到 KPP（" + modDir + "），但没有 Telemetry 子目录：这一份不带文件桥，升级到 1.2.0+ 并重启游戏后才会写出遥测。",
                        count);
                }

                return new KppTelemetryUpdate(
                    KppTelemetryState.ModMissing,
                    null,
                    null,
                    dir,
                    null,
                    "未找到遥测目录：" + dir,
                    count);
            }

            var status = ReadStatus(dir);
            if (status is not null)
            {
                _lastStatus = status;
            }

            if (TryReadLatest(dir, out var snapshot, out var parseError))
            {
                if (snapshot is not null)
                {
                    if (!snapshot.IsCompatible)
                    {
                        return new KppTelemetryUpdate(
                            KppTelemetryState.Incompatible,
                            _lastGood,
                            _lastStatus,
                            dir,
                            null,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"遥测契约不兼容：需要 kpp.telemetry/{KppTelemetrySnapshot.SupportedMajorVersion}，实例给的是 {snapshot.Schema ?? "(缺失)"}"),
                            count);
                    }

                    _lastGood = snapshot;
                }
            }
            else if (_lastGood is null)
            {
                // 一次都没读到过、而且 status 也读不到：目录在但两侧都没写出东西。
                var detail = parseError ?? "latest.json 尚不存在";
                return new KppTelemetryUpdate(KppTelemetryState.NoData, null, _lastStatus, dir, null, detail, count);
            }

            var best = _lastGood;
            if (best is null)
            {
                var noDataDetail = BuildNoDataDetail(_lastStatus, parseError);
                return new KppTelemetryUpdate(KppTelemetryState.NoData, null, _lastStatus, dir, null, noDataDetail, count);
            }

            var age = ComputeAgeMs(best);
            var interval = best.EffectivePublishIntervalMs;
            var stale = age is not null && age.Value > StaleMultiplier * interval;

            if (_lastStatus?.WriterAlive == false)
            {
                return new KppTelemetryUpdate(
                    KppTelemetryState.Stale,
                    best,
                    _lastStatus,
                    dir,
                    age,
                    "KPP 写方已停止（游戏已退出或 Mod 被卸载）",
                    count);
            }

            if (stale)
            {
                return new KppTelemetryUpdate(
                    KppTelemetryState.Stale,
                    best,
                    _lastStatus,
                    dir,
                    age,
                    string.Create(CultureInfo.InvariantCulture, $"数据已陈旧：{(age!.Value / 1000d):0.0}s 未更新（发布周期 {(interval / 1000d):0.0}s）"),
                    count);
            }

            return new KppTelemetryUpdate(KppTelemetryState.Online, best, _lastStatus, dir, age, null, count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new KppTelemetryUpdate(
                KppTelemetryState.ReadError,
                _lastGood,
                _lastStatus,
                dir,
                null,
                "读取遥测失败：" + ex.Message,
                count);
        }
    }

    private static string BuildNoDataDetail(KppStatus? status, string? parseError)
    {
        if (status?.WriteFailures is > 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"KPP 在线但写入失败 {status.WriteFailures} 次（磁盘满 / 权限 / 路径错）");
        }

        return parseError ?? "latest.json 尚不存在（进游戏并加载存档后才有数据）";
    }

    private static double? ComputeAgeMs(KppTelemetrySnapshot snapshot)
    {
        if (snapshot.PublishedUnixMs is not { } published || published <= 0)
        {
            return null;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Math.Max(0d, nowMs - published);
    }

    private KppStatus? ReadStatus(string dir)
    {
        var path = KppPaths.FileIn(dir, KppPaths.StatusFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = ReadAllText(path);
        return KppStatus.TryParse(text, out var status, out _) ? status : _lastStatus;
    }

    private bool TryReadLatest(string dir, out KppTelemetrySnapshot? snapshot, out string? parseError)
    {
        snapshot = null;
        parseError = null;

        var path = KppPaths.FileIn(dir, KppPaths.LatestFileName);
        if (!File.Exists(path))
        {
            parseError = "latest.json 尚不存在";
            return false;
        }

        var text = ReadAllText(path);
        if (KppTelemetrySnapshot.TryParse(text, out var parsed, out var error))
        {
            snapshot = parsed;
            return true;
        }

        parseError = error;
        return false;
    }

    // 读方必须容忍读到一半（§2）：写方是 tmp → Move，理论上不会看到半个文件，
    // 但 Windows 的文件替换与外部工具仍可能让这一瞬间读到空文件；解析失败由上层当作"本轮无数据"。
    private static string? ReadAllText(string path) => File.ReadAllText(path);

    private void Raise(KppTelemetryUpdate update)
    {
        var handlers = Updated;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<KppTelemetryUpdate>)handler)(update);
            }
            catch (Exception)
            {
                // 一个订阅者炸了不能影响其它订阅者，更不能把轮询线程带走。
            }
        }
    }

    private void RaiseSafe()
    {
        try
        {
            var count = Interlocked.Read(ref _pollCount);
            Raise(new KppTelemetryUpdate(
                KppTelemetryState.ReadError,
                _lastGood,
                _lastStatus,
                TelemetryDirectory,
                null,
                "轮询遥测失败",
                count));
        }
        catch (Exception)
        {
            // 连上报都失败就只能放弃这一轮。
        }
    }

    private static string? Normalize(string? telemetryDir) =>
        string.IsNullOrWhiteSpace(telemetryDir) ? null : telemetryDir.Trim();
}
