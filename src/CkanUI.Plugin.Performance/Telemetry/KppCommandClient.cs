using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CkanUI.Plugin.Performance.Telemetry;

/// <summary>一条命令的 ack 结果。</summary>
/// <param name="Ok">KPP 是否执行成功（<c>ok</c> 字段；超时/写失败也为 false）。</param>
/// <param name="Op">命令 op。</param>
/// <param name="Message">可读结果或失败原因（绝不静默吞掉，§5）。</param>
/// <param name="ElapsedMs">从写到拿到 ack 的耗时。</param>
public sealed record KppCommandAck(bool Ok, string Op, string Message, double ElapsedMs)
{
    /// <summary>导出报告的产物路径（仅 <c>export_report</c> 的 ack.result.path 有）。</summary>
    public string? ResultPath { get; init; }

    /// <summary>ack 原始文本（排障用；拿不到 ack 时为 null）。</summary>
    public string? RawAck { get; init; }
}

/// <summary>
/// command 文件桥的写方（契约 §5）。
/// </summary>
/// <remarks>
/// <para>写 <c>commands/&lt;seq&gt;-&lt;id&gt;.json</c>：8 位零填充序号保证"按名字排序即按发出顺序"，
/// 生成用 tmp → Move 的原子写法（§2）。</para>
/// <para>读 <c>commands/acks/&lt;id&gt;.json</c>：拿到就删（ack 是一次性凭据）；超时则删掉自己写的命令文件
/// 并返回可显示的失败——契约 §6 明确"不认识的 op 由 UI 自己超时清理"，所以清理责任在这里。</para>
/// <para>全部异常都转成 <see cref="KppCommandAck.Ok"/> = false 的返回值，不往 UI 线程抛。</para>
/// </remarks>
public sealed class KppCommandClient
{
    /// <summary>命令 schema（固定）。</summary>
    public const string CommandSchema = "kpp.command/1";

    /// <summary>ack 轮询间隔。</summary>
    public const int AckPollIntervalMs = 120;

    /// <summary>默认 ack 超时（KPP 在主线程消费命令，卡帧时也要给足余量）。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(6);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private string? _telemetryDir;
    private long _sequence;

    /// <summary>构造。</summary>
    /// <param name="telemetryDir">遥测根目录；<c>null</c> 表示未选择实例。</param>
    public KppCommandClient(string? telemetryDir)
    {
        _telemetryDir = Normalize(telemetryDir);
    }

    /// <summary>当前目标遥测目录。</summary>
    public string? TelemetryDirectory => Volatile.Read(ref _telemetryDir);

    /// <summary>换目标实例（序号重新按磁盘上已有的命令文件推算）。</summary>
    /// <param name="telemetryDir">新遥测目录。</param>
    public void Retarget(string? telemetryDir)
    {
        lock (_gate)
        {
            _telemetryDir = Normalize(telemetryDir);
            _sequence = 0;
        }
    }

    /// <summary>
    /// 发一条命令并等待 ack。
    /// </summary>
    /// <param name="op">契约 §5 的 op。</param>
    /// <param name="args">参数；无参传 <c>null</c>。</param>
    /// <param name="timeout">ack 超时；默认 <see cref="DefaultTimeout"/>。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>ack 结果；不抛异常（取消除外也返回失败 ack）。</returns>
    public async Task<KppCommandAck> SendAsync(
        string op,
        IReadOnlyDictionary<string, object?>? args = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var dir = TelemetryDirectory;
        if (dir is null)
        {
            return new KppCommandAck(false, op, "未选择 KSP 实例，无法下发命令。", 0);
        }

        var commandsDir = KppPaths.CommandsIn(dir);
        var acksDir = KppPaths.AcksIn(dir);
        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string? commandPath = null;
        var started = DateTime.UtcNow;

        try
        {
            Directory.CreateDirectory(commandsDir);
            Directory.CreateDirectory(acksDir);

            var seq = NextSequence(commandsDir);
            commandPath = Path.Combine(commandsDir, string.Create(CultureInfo.InvariantCulture, $"{seq:D8}-{id}.json"));
            WriteAtomic(commandPath, BuildPayload(op, id, args));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new KppCommandAck(false, op, "无法写入命令目录（" + Describe(ex) + "）：" + commandsDir, Elapsed(started));
        }

        var ackPath = Path.Combine(acksDir, id + ".json");
        var limit = timeout ?? DefaultTimeout;
        var deadline = started + limit;

        while (DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested)
            {
                DeleteQuietly(commandPath);
                return new KppCommandAck(false, op, "已取消（未等待到 KPP 响应）。", Elapsed(started));
            }

            var ack = TryReadAck(ackPath, op, started);
            if (ack is not null)
            {
                return ack;
            }

            try
            {
                await Task.Delay(AckPollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                DeleteQuietly(commandPath);
                return new KppCommandAck(false, op, "已取消（未等待到 KPP 响应）。", Elapsed(started));
            }
        }

        // 超时：自己写下的命令自己清，别让 KPP 下次启动时被一堆过期命令淹掉（§6）。
        DeleteQuietly(commandPath);
        return new KppCommandAck(
            false,
            op,
            string.Create(CultureInfo.InvariantCulture, $"命令超时：{limit.TotalSeconds:0.0}s 内没有收到 KPP 的 ack（游戏未运行或命令未被消费）"),
            Elapsed(started));
    }

    /// <summary>清空命令目录里本程序留下的整份命令（断开/切换实例时用；失败静默）。</summary>
    /// <param name="maxAge">只清理早于该时长的命令文件。</param>
    public void PruneStaleCommands(TimeSpan maxAge)
    {
        var dir = TelemetryDirectory;
        if (dir is null)
        {
            return;
        }

        var commandsDir = KppPaths.CommandsIn(dir);
        try
        {
            if (!Directory.Exists(commandsDir))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(commandsDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 单个文件删不掉不影响其它清理。
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 清理是尽力而为：失败不弹给用户。
        }
    }

    private string BuildPayload(string op, string id, IReadOnlyDictionary<string, object?>? args)
    {
        var builder = new StringBuilder(256);
        builder.Append('{');
        AppendName(builder, "schema");
        AppendQuoted(builder, CommandSchema);
        builder.Append(',');
        AppendName(builder, "id");
        AppendQuoted(builder, id);
        builder.Append(',');
        AppendName(builder, "op");
        AppendQuoted(builder, op);
        builder.Append(',');
        AppendName(builder, "issuedUnixMs");
        builder.Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        builder.Append(',');
        AppendName(builder, "args");
        builder.Append('{');
        var first = true;
        if (args is not null)
        {
            foreach (var pair in args)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                AppendName(builder, pair.Key);
                AppendScalar(builder, pair.Value);
            }
        }

        builder.Append("}}");
        return builder.ToString();
    }

    private static void AppendName(StringBuilder builder, string name)
    {
        AppendQuoted(builder, name);
        builder.Append(':');
    }

    private static void AppendQuoted(StringBuilder builder, string? value)
    {
        builder.Append('"');
        builder.Append(value?
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) ?? string.Empty);
        builder.Append('"');
    }

    private static void AppendScalar(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case bool flag:
                builder.Append(flag ? "true" : "false");
                break;
            case int or long or double or float or decimal:
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append('"').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('"');
                break;
        }
    }

    private long NextSequence(string commandsDir)
    {
        lock (_gate)
        {
            if (_sequence == 0)
            {
                // 序号从磁盘上已有的最大值 +1 起：重启 ckan-ui 后仍然按名字排序即按发出顺序。
                _sequence = ProbeMaxSequence(commandsDir);
            }

            _sequence++;
            return _sequence;
        }
    }

    private static long ProbeMaxSequence(string commandsDir)
    {
        long max = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(commandsDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var dash = name.IndexOf('-', StringComparison.Ordinal);
                var prefix = dash > 0 ? name[..dash] : name;
                if (long.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) && seq > max)
                {
                    max = seq;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 读不到就从头开始编号：命名重复由 KPP 侧的 id 去重兜底，不在这里失败。
        }

        return max;
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Utf8NoBom);

        if (File.Exists(path))
        {
            File.Replace(tmp, path, destinationBackupFileName: null);
        }
        else
        {
            try
            {
                File.Move(tmp, path);
            }
            catch (IOException)
            {
                // §2 的兜底路径：Move 失败（目标已被别的进程创建）就删目标再 Move。
                DeleteQuietly(path);
                File.Move(tmp, path);
            }
        }
    }

    private KppCommandAck? TryReadAck(string ackPath, string op, DateTime started)
    {
        try
        {
            if (!File.Exists(ackPath))
            {
                return null;
            }

            var raw = File.ReadAllText(ackPath);
            DeleteQuietly(ackPath);

            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new KppCommandAck(false, op, "KPP 的 ack 不是 JSON 对象。", Elapsed(started)) { RawAck = raw };
            }

            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? string.Empty
                : ok ? "已执行" : "KPP 未给出失败原因";

            string? resultPath = null;
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String)
            {
                resultPath = pathElement.GetString();
            }

            return new KppCommandAck(ok, op, message, Elapsed(started)) { ResultPath = resultPath, RawAck = raw };
        }
        catch (JsonException)
        {
            // ack 读到一半（写方刚建好文件）：当作还没到，下一轮再读。
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉就算了：不能因为清理失败让一次命令操作变成异常。
        }
    }

    private static double Elapsed(DateTime started) =>
        Math.Max(0d, (DateTime.UtcNow - started).TotalMilliseconds);

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "权限不足",
        System.Security.SecurityException => "安全策略拒绝",
        NotSupportedException => "路径格式不支持",
        _ => "磁盘或路径错误",
    };

    private static string? Normalize(string? telemetryDir) =>
        string.IsNullOrWhiteSpace(telemetryDir) ? null : telemetryDir.Trim();
}
