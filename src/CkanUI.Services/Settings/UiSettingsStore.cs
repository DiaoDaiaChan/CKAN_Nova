using System.Text.Json;
using CKAN.IO;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Logging;
using log4net;

namespace CkanUI.Services.Settings;

/// <summary>
/// IUiSettingsStore 实现（01 篇 §7）：UI 自有设置存 %LocalAppData%/CKAN/ckan-ui.json，与 Core 的 config.json 分文件，
/// 避免和 CLI / 旧 GUI 争写同一个文件。写入走「同目录临时文件 + File.Move(overwrite)」，崩溃时不会留下半截 JSON。
/// </summary>
public sealed class UiSettingsStore : IUiSettingsStore
{
    /// <summary>默认文件名。</summary>
    public const string FileName = "ckan-ui.json";

    private const int MoveAttempts = 5;

    private static readonly ILog Log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 窗口坐标可能被平台回报成 NaN；不允许时整份序列化失败，比丢一个值更糟。
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly Lock _gate = new();
    private UiSettings _current = new();

    // 读取缓存：启动期 7 个外观宿主各 Load 一次、设置页每次进入还要 GetAllAsync 一次，
    // 每次都 File.ReadAllBytes + 反序列化同一份 ckan-ui.json 是纯重复 IO。按「大小 + 写入时间」失效。
    private UiSettings? _cachedRead;
    private long _cachedLength = -1;
    private DateTime _cachedWriteUtc;
    private DateTime _lastFailureLogUtc = DateTime.MinValue;

    /// <summary>使用默认路径（Core 的 AppDataPath，即 %LocalAppData%/CKAN）。</summary>
    public UiSettingsStore()
        : this(Path.Combine(CKANPathUtils.AppDataPath, FileName))
    {
    }

    /// <summary>指定文件路径（测试用）。</summary>
    public UiSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    /// <inheritdoc />
    public event Action<UiSettings>? Changed;

    public UiSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public UiSettings Load()
    {
        var loaded = ReadWithCache() ?? new UiSettings();
        lock (_gate)
        {
            _current = loaded;
        }

        return loaded;
    }

    public OperationResult<bool> Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        OperationResult<bool> result;

        // 进程内串行；临时文件名带 Guid，跨进程同时写也不会互相覆盖临时文件。
        lock (_gate)
        {
            result = WriteCore(settings);
        }

        // 变更通知在锁外触发：订阅方（主题宿主）会同步回读 Current，持锁回调容易把锁序搞乱。
        if (result.IsSuccess)
        {
            RaiseChanged(settings);
        }

        return result;
    }

    /// <summary>
    /// 真正落盘：临时文件 + 替换。瞬时占用（杀软扫描刚建出来的临时文件、另一进程正好在替换目标）
    /// 会以 UnauthorizedAccessException 冒出来，重试一次即可；仍然失败才报错。
    /// </summary>
    private OperationResult<bool> WriteCore(UiSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        Exception? failure = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var temp = Path.Combine(directory, $"{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(directory);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                MoveWithRetry(temp, FilePath);
                _current = settings;
                RememberWritten(settings);
                return OperationResult<bool>.Ok(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failure = ex;
                if (attempt == 0 && ex is UnauthorizedAccessException)
                {
                    Thread.Sleep(60);
                    continue;
                }

                break;
            }
            finally
            {
                TryDelete(temp);
            }
        }

        LogSaveFailure(failure!);
        return OperationResult<bool>.Fail("无法保存界面设置", failure!.Message, retryable: true);
    }

    /// <summary>同一类失败 30 秒内只留一条：连续拖动每格写一次时，日志抽屉不能被同一条错误刷满。</summary>
    private void LogSaveFailure(Exception ex)
    {
        var now = DateTime.UtcNow;
        if (now - _lastFailureLogUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastFailureLogUtc = now;
        Log.Warn($"保存 UI 设置失败：{FilePath}", ex);
    }

    /// <summary>写完立刻把缓存指向刚写入的内容，避免下一次 Load 再解析一遍。</summary>
    /// <param name="settings">已落盘的设置。</param>
    private void RememberWritten(UiSettings settings)
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists)
            {
                _cachedRead = null;
                _cachedLength = -1;
                return;
            }

            _cachedRead = settings;
            _cachedLength = info.Length;
            _cachedWriteUtc = info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cachedRead = null;
            _cachedLength = -1;
        }
    }

    private void RaiseChanged(UiSettings settings)
    {
        var handler = Changed;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(settings);
        }
        catch (Exception ex)
        {
            // 05 篇 §7：事件处理不抛，一个订阅方失败不能影响落盘结果。
            Log.Warn("UI 设置变更订阅方抛出异常。", ex);
        }
    }

    /// <summary>带失效判断的读取：文件没变就复用上次解析结果。</summary>
    /// <returns>解析出的设置；文件不存在或不可读时为 null。</returns>
    private UiSettings? ReadWithCache()
    {
        FileInfo info;
        try
        {
            info = new FileInfo(FilePath);
            if (!info.Exists)
            {
                _cachedRead = null;
                _cachedLength = -1;
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TryRead();
        }

        if (_cachedRead is not null && info.Length == _cachedLength && info.LastWriteTimeUtc == _cachedWriteUtc)
        {
            return _cachedRead;
        }

        var read = TryRead();
        if (read is not null)
        {
            _cachedRead = read;
            _cachedLength = info.Length;
            _cachedWriteUtc = info.LastWriteTimeUtc;
        }

        return read;
    }

    private UiSettings? TryRead()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<UiSettings>(StripUtf8Bom(File.ReadAllBytes(FilePath)), JsonOptions);
            // 显式写成 null 的字典会让 VM 层 NRE，这里补回默认。
            return settings is null ? null : settings with { ColumnWidths = settings.ColumnWidths ?? new Dictionary<string, double>() };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn($"UI 设置文件不可读，已回退默认值：{FilePath}", ex);
            return null;
        }
    }

    /// <summary>去掉 UTF-8 BOM：JsonSerializer 按字节解析时会把 0xEF 当成非法起始字符，整个文件回落默认值。</summary>
    private static ReadOnlySpan<byte> StripUtf8Bom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes;

    // Windows 上目标文件正被读取（杀毒、另一个读者）时替换会短暂失败，退避重试几次。
    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MoveAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"清理临时文件失败：{path}", ex);
        }
    }
}
