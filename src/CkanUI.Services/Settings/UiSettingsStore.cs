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
        var loaded = TryRead() ?? new UiSettings();
        lock (_gate)
        {
            _current = loaded;
        }

        return loaded;
    }

    public OperationResult<bool> Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 进程内串行；临时文件名带 Guid，跨进程同时写也不会互相覆盖临时文件。
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            var temp = Path.Combine(directory, $"{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(directory);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                MoveWithRetry(temp, FilePath);
                _current = settings;
                return OperationResult<bool>.Ok(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Log.Warn($"保存 UI 设置失败：{FilePath}", ex);
                return OperationResult<bool>.Fail("无法保存界面设置", ex.Message, retryable: true);
            }
            finally
            {
                TryDelete(temp);
            }
        }
    }

    private UiSettings? TryRead()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllBytes(FilePath), JsonOptions);
            // 显式写成 null 的字典会让 VM 层 NRE，这里补回默认。
            return settings is null ? null : settings with { ColumnWidths = settings.ColumnWidths ?? new Dictionary<string, double>() };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn($"UI 设置文件不可读，已回退默认值：{FilePath}", ex);
            return null;
        }
    }

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
