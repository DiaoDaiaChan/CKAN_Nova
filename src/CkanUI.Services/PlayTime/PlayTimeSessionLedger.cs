using System.Globalization;
using System.Text;
using System.Text.Json;
using log4net;

namespace CkanUI.Services.PlayTime;

/// <summary>
/// 会话账本：把「游戏跑起来的每一段时长」按本地自然日累计，落在 play-time.json 旁边的
/// <c>play-time.sessions.json</c>（12 篇 F-48）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不直接塞进 play-time.json：那个文件是 <c>PlayTimeEntry</c> 的序列化结果，
/// 而 DTO 属共享契约（不可改）。账本另存一个文件还能让「手工补录的总量」与「自动累计的时长」
/// 各自保留原始精度，读的时候再合并——去重规则就落在合并上，而不是互相污染。
/// </para>
/// <para>
/// 按「日期 → 秒」分桶而不是只留一个总数：跨零点的会话必须切成两段，
/// 否则日后做「每天玩了多久」就无从回溯；切分只影响分桶，不影响总数。
/// </para>
/// <para>
/// 写入沿用「写 .tmp → 覆盖移动」的原子替换：崩溃最多丢掉最后一次累计，不会留下半个 JSON。
/// 未闭合会话（写 begin 后进程被杀）在新会话开始时直接覆盖丢弃：拿不到真实退出时刻，
/// 宁可少记也不虚增。
/// </para>
/// </remarks>
public sealed class PlayTimeSessionLedger
{
    /// <summary>账本文件名后缀：<c>play-time.json</c> → <c>play-time.sessions.json</c>。</summary>
    public const string FileNameSuffix = ".sessions";

    private static readonly ILog Log = LogManager.GetLogger(typeof(PlayTimeSessionLedger));
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>用账本文件的绝对/相对路径构造；<paramref name="clock"/> 供测试支配「现在」。</summary>
    public PlayTimeSessionLedger(string filePath, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>账本文件路径。</summary>
    public string FilePath { get; }

    /// <summary>由 play-time.json 的路径推出账本路径：同目录，文件名插入 <c>.sessions</c>。</summary>
    public static string PathFor(string playTimePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playTimePath);
        var directory = Path.GetDirectoryName(playTimePath);
        var fileName = Path.GetFileNameWithoutExtension(playTimePath) + FileNameSuffix + Path.GetExtension(playTimePath);
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    /// <summary>正在计时的实例名；没有未闭合会话时为 null。</summary>
    public string? OpenInstanceName => ReadAccount().Open?.Instance;

    /// <summary>开始计时；账本写不进去（盘满/被占用）返回 false。</summary>
    public bool BeginSession(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return false;
        }

        lock (_gate)
        {
            var account = ReadUnlocked();
            account.Open = new OpenSession(instanceName, _clock());
            return WriteUnlocked(account);
        }
    }

    /// <summary>结束计时：按本地自然日切分后累计，返回本次记入的小时数（无会话为 0）。</summary>
    public double EndSession()
    {
        lock (_gate)
        {
            var account = ReadUnlocked();
            if (account.Open is not { } open)
            {
                return 0d;
            }

            var endedAt = _clock();
            var seconds = (endedAt - open.StartedAt).TotalSeconds;

            // 时钟回拨（夏令时/手工改表）会算出负数：按 0 记，绝不让总时长倒退。
            if (!double.IsFinite(seconds) || seconds < 0)
            {
                seconds = 0;
            }

            Accumulate(account, open.Instance, open.StartedAt, seconds);
            account.Open = null;
            return WriteUnlocked(account) ? Math.Round(seconds / 3600d, 4, MidpointRounding.AwayFromZero) : 0d;
        }
    }

    /// <summary>丢弃未闭合会话；返回是否真的丢掉了一条。</summary>
    public bool DiscardOpenSession()
    {
        lock (_gate)
        {
            var account = ReadUnlocked();
            if (account.Open is null)
            {
                return false;
            }

            account.Open = null;
            return WriteUnlocked(account);
        }
    }

    /// <summary>
    /// 清除指定实例的自动累计（该实例的按日分桶整份删掉）。
    /// 如果正在给这个实例计时，未闭合会话从「现在」重新开始——重置前的时长不再回算，之后的照常累计。
    /// </summary>
    /// <returns>该实例本来就没有记录时也返回真（重置是幂等的）；写盘失败返回假。</returns>
    public bool ResetInstance(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return false;
        }

        lock (_gate)
        {
            var account = ReadUnlocked();
            var changed = account.Instances.Remove(instanceName);
            if (account.Open is { } open && string.Equals(open.Instance, instanceName, StringComparison.Ordinal))
            {
                account.Open = new OpenSession(open.Instance, _clock());
                changed = true;
            }

            // 没有可改的内容就不写盘：避免为一次空重置把整个账本重排一遍（也少一次写坏的机会）。
            return !changed || WriteUnlocked(account);
        }
    }

    /// <summary>各实例已累计的自动时长（小时）。</summary>
    public IReadOnlyDictionary<string, double> TotalsByInstance()
    {
        lock (_gate)
        {
            var account = ReadUnlocked();
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (instanceName, buckets) in account.Instances)
            {
                var seconds = 0d;
                foreach (var value in buckets.Values)
                {
                    seconds += value;
                }

                if (seconds > 0)
                {
                    totals[instanceName] = seconds / 3600d;
                }
            }

            return totals;
        }
    }

    /// <summary>指定实例按自然日分桶的累计小时数（供核对跨天切分）。</summary>
    public IReadOnlyDictionary<DateOnly, double> SlicesByDate(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        lock (_gate)
        {
            var account = ReadUnlocked();
            var slices = new SortedDictionary<DateOnly, double>();
            if (!account.Instances.TryGetValue(instanceName, out var buckets))
            {
                return slices;
            }

            foreach (var (key, seconds) in buckets)
            {
                if (DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    slices[date] = seconds / 3600d;
                }
            }

            return slices;
        }
    }

    /// <summary>
    /// 把一次会话的秒数按自然日切开累加。日期边界取「会话开始时刻的时区」：
    /// 会话是用户本机时钟上的连续一段时间，用固定偏移才能与系统时区变更解耦（也让测试可确定）。
    /// </summary>
    private static void Accumulate(Account account, string instanceName, DateTimeOffset startedAt, double seconds)
    {
        if (!account.Instances.TryGetValue(instanceName, out var buckets))
        {
            buckets = new Dictionary<string, double>(StringComparer.Ordinal);
            account.Instances[instanceName] = buckets;
        }

        var remaining = seconds;
        var cursor = startedAt;
        while (remaining > 0)
        {
            var dayEnd = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, 0, 0, 0, cursor.Offset).AddDays(1);
            var slice = Math.Min(remaining, (dayEnd - cursor).TotalSeconds);
            if (!(slice > 0))
            {
                // 病态输入（偏移导致算出非正长度）兜底：剩余全部并入当天，避免死循环。
                slice = remaining;
            }

            var key = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            buckets[key] = (buckets.TryGetValue(key, out var existing) ? existing : 0d) + slice;
            remaining -= slice;
            cursor = cursor.AddSeconds(slice);
        }
    }

    private Account ReadAccount()
    {
        lock (_gate)
        {
            return ReadUnlocked();
        }
    }

    private Account ReadUnlocked()
    {
        var account = new Account();
        if (!File.Exists(FilePath))
        {
            return account;
        }

        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return account;
            }

            if (root.TryGetProperty("open", out var open) && open.ValueKind == JsonValueKind.Object)
            {
                var instance = open.TryGetProperty("instance", out var instanceElement) && instanceElement.ValueKind == JsonValueKind.String
                    ? instanceElement.GetString()
                    : null;
                var startedAt = open.TryGetProperty("startedAt", out var startedElement)
                    && startedElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(startedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                        ? parsed
                        : (DateTimeOffset?)null;
                if (!string.IsNullOrWhiteSpace(instance) && startedAt is { } started)
                {
                    account.Open = new OpenSession(instance!, started);
                }
            }

            if (root.TryGetProperty("instances", out var instances) && instances.ValueKind == JsonValueKind.Object)
            {
                foreach (var instance in instances.EnumerateObject())
                {
                    if (instance.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var buckets = new Dictionary<string, double>(StringComparer.Ordinal);
                    foreach (var bucket in instance.Value.EnumerateObject())
                    {
                        if (bucket.Value.ValueKind == JsonValueKind.Number
                            && bucket.Value.TryGetDouble(out var seconds)
                            && double.IsFinite(seconds)
                            && seconds > 0)
                        {
                            buckets[bucket.Name] = seconds;
                        }
                    }

                    if (buckets.Count > 0)
                    {
                        account.Instances[instance.Name] = buckets;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 账本坏了就当没有：自动累计是附赠信息，不该阻断时长页。
            Log.Warn($"读取游戏时长会话账本失败：{FilePath}", ex);
            return new Account();
        }

        return account;
    }

    private bool WriteUnlocked(Account account)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        var temp = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                if (account.Open is { } open)
                {
                    writer.WriteStartObject("open");
                    writer.WriteString("instance", open.Instance);
                    writer.WriteString("startedAt", open.StartedAt.ToString("O", CultureInfo.InvariantCulture));
                    writer.WriteEndObject();
                }

                writer.WriteStartObject("instances");
                foreach (var (instanceName, buckets) in account.Instances.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartObject(instanceName);
                    foreach (var (key, seconds) in buckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        writer.WriteNumber(key, Math.Round(seconds, 3, MidpointRounding.AwayFromZero));
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"写入游戏时长会话账本失败：{FilePath}", ex);
            return false;
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // 临时文件删不掉不影响正确性（下次会被覆盖）。
                }
                catch (UnauthorizedAccessException)
                {
                    // 同上：清理失败不改变原子替换已经成功的事实。
                }
            }
        }
    }

    /// <summary>账本内存模型：未闭合会话 + 每实例的「日期 → 秒」分桶。</summary>
    private sealed class Account
    {
        public OpenSession? Open { get; set; }

        public Dictionary<string, Dictionary<string, double>> Instances { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>一次未闭合会话（已开始、还没结算）。</summary>
    private sealed record OpenSession(string Instance, DateTimeOffset StartedAt);
}
