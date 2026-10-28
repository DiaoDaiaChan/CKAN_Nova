using System.Text.Json;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.PlayTime;

/// <summary>
/// 以原子 JSON 文件保存实例累计游戏时长，并把自动累计的会话时长合并进来（12 篇 F-48）。
/// </summary>
/// <remarks>
/// <para>
/// 文件里存的是「手工基线」而不是页面直接显示的数字：页面上那个数字的口径是「累计总量」，
/// 所以保存时先扣掉自动累计部分（<see cref="Save"/>），读取时再加回来（<see cref="Load"/>）。
/// 这样自动累计不会被一次手工保存吃成两份，用户手工改写也仍然是「总量」语义。
/// </para>
/// <para>
/// 同时实现 <see cref="IPlayTimeTracker"/>：启动/退出流程只依赖窄接口，容器把本类注册一次
/// 即可同时满足页面读取与启动计时的需要；本类也因此是唯一的落盘写者，不会出现两个实例
/// 各自原子替换同一份文件而互相覆盖。
/// </para>
/// </remarks>
public sealed class PlayTimeService : IPlayTimeService, IPlayTimeTracker
{
    private const string DefaultFileName = "play-time.json";

    private readonly string _path;
    private readonly PlayTimeSessionLedger _sessions;
    private readonly object _gate = new();

    /// <summary>默认保存至 %LocalAppData%/CKAN/play-time.json。</summary>
    public PlayTimeService() : this(DefaultPath) { }

    /// <summary>注入路径，供隔离测试使用。</summary>
    public PlayTimeService(string path) : this(path, null) { }

    /// <summary>注入路径与时钟（时钟只影响自动累计的会话起止）。</summary>
    public PlayTimeService(string path, Func<DateTimeOffset>? clock)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _sessions = new PlayTimeSessionLedger(PlayTimeSessionLedger.PathFor(_path), clock);
    }

    /// <summary>默认保存路径。
    /// <para>独立成属性而不是散在构造链里，是为了让宿主与测试都能核对「写到哪儿」，避免各处各拼一遍。</para>
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKAN",
        DefaultFileName);

    /// <inheritdoc />
    public IReadOnlyList<PlayTimeEntry> Load()
    {
        lock (_gate)
        {
            var totals = ReadBaselines();
            foreach (var (instanceName, autoHours) in _sessions.TotalsByInstance())
            {
                totals[instanceName] = (totals.TryGetValue(instanceName, out var manualHours) ? manualHours : 0d) + autoHours;
            }

            return totals
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PlayTimeEntry(pair.Key, Math.Round(pair.Value, 2, MidpointRounding.AwayFromZero)))
                .ToList();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="hours"/> 视为「累计总量」：落盘前扣掉该实例已自动累计的小时数，
    /// 否则下次 <see cref="Load"/> 会把同一段游戏时间算两遍。
    /// 手工填的总量小于自动累计时基线被夹到 0，页面仍显示自动累计的数字（宁可不少记）。
    /// </remarks>
    public bool Save(string instanceName, double hours)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        if (!double.IsFinite(hours) || hours < 0) return false;
        lock (_gate)
        {
            var totals = ReadBaselines();
            var total = Math.Round(hours, 0, MidpointRounding.AwayFromZero);
            var autoHours = _sessions.TotalsByInstance().TryGetValue(instanceName, out var accumulated) ? accumulated : 0d;
            totals[instanceName] = Math.Max(0d, total - autoHours);

            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory)) return false;
            var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(directory);
                var values = totals
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new PlayTimeEntry(pair.Key, pair.Value));
                File.WriteAllText(temp, JsonSerializer.Serialize(values));
                File.Move(temp, _path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch (IOException) { /* 临时文件清理失败不改变原子替换结果。 */ }
                    catch (UnauthorizedAccessException) { /* 临时文件清理失败不改变原子替换结果。 */ }
                }
            }
        }
    }

    /// <inheritdoc />
    public string? ActiveInstanceName => _sessions.OpenInstanceName;

    /// <inheritdoc />
    public bool BeginSession(string instanceName) => _sessions.BeginSession(instanceName);

    /// <inheritdoc />
    public double EndSession() => _sessions.EndSession();

    /// <inheritdoc />
    public bool DiscardOpenSession() => _sessions.DiscardOpenSession();

    /// <summary>读 play-time.json 里的手工基线；文件缺失或损坏按空表处理（时长页不该因此打不开）。</summary>
    private Dictionary<string, double> ReadBaselines()
    {
        var baselines = new Dictionary<string, double>(StringComparer.Ordinal);
        if (!File.Exists(_path)) return baselines;
        try
        {
            var entries = JsonSerializer.Deserialize<List<PlayTimeEntry>>(File.ReadAllText(_path));
            if (entries is null) return baselines;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.InstanceName)) continue;
                baselines[entry.InstanceName] = entry.Hours;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        return baselines;
    }
}
