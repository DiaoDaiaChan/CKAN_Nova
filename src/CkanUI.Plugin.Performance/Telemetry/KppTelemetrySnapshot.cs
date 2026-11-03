using System.Globalization;
using System.Text.Json;

namespace CkanUI.Plugin.Performance.Telemetry;

// 强类型 DTO + 容错解析（契约 docs/design/performance-tab.md §3 / §4）。
//
// 三条硬纪律都体现在这里的类型上：
// 1) 可缺能力一律 double?/long?——缺字段是"不可用"，UI 必须渲染 N/A，绝不能拿 0 冒充；
// 2) GPU 段 valid=false 时把 ms 强制拉成 null（§3.1 的 NaN 纪律，驱动器拒绝的计时不许变成 0.0）；
// 3) 解析只在 try/catch 里跑并且返回 bool，解析失败只是"这一轮没数据"，不动上一份好数据。

/// <summary>KPP 写方信息。</summary>
public sealed record KppWriter(
    long? Pid,
    string? DllVersion,
    string? KspVersion,
    string? UnityVersion,
    string? Os);

/// <summary>会话信息。</summary>
public sealed record KppSession(
    string? Id,
    double? StartedUnixMs,
    double? UptimeSec,
    long? FrameCount);

/// <summary>当前上下文（场景/载具/节流）。</summary>
public sealed record KppContext(
    string? Scene,
    string? VesselName,
    long? PartCount,
    long? ModCount,
    bool? Paused,
    double? TimeScale,
    double? GameTimeSec);

/// <summary>帧指标。</summary>
public sealed record KppFrame(
    double? Fps,
    double? AvgFps,
    double? OnePercentLowFps,
    double? PointOnePercentLowFps,
    double? FrameMs,
    double? AvgFrameMs,
    double? JitterMs,
    double? Ptr,
    double? Dt,
    long? PhysSteps);

/// <summary>耗时分解（毫秒）。</summary>
public sealed record KppBreakdown(
    double? ModuleScripts,
    double? PluginScripts,
    double? Physx,
    double? RenderResidual,
    double? Camera,
    double? TufxPostFx,
    double? TufxCpu,
    double? Gc);

/// <summary>GPU 五段之一。</summary>
/// <param name="Key">depth / opaque / transparent / postFx / final。</param>
/// <param name="Ms">该段耗时；<paramref name="Valid"/> 为 false 时必为 <c>null</c>。</param>
/// <param name="Valid">计时是否被驱动接受。</param>
public sealed record KppGpuStage(string? Key, double? Ms, bool? Valid);

/// <summary>GPU 计时。</summary>
public sealed record KppGpu(
    bool? Available,
    string? StatusText,
    double? FrameSpanMs,
    double? StageTotalMs,
    double? UnaccountedMs,
    long? PublishedFrames,
    long? InvalidStageFrames,
    long? Dropped,
    long? Reordered,
    IReadOnlyList<KppGpuStage> Stages);

/// <summary>GPU 括号段（相机/事件归属）。</summary>
public sealed record KppGpuBracket(
    string? Camera,
    string? EventName,
    string? Owner,
    double? Ms,
    long? Samples);

/// <summary>垫高器（padder）状态。</summary>
public sealed record KppPadder(
    bool? Enabled,
    double? TargetMb,
    double? HeldMb,
    double? MaxMb,
    double? PhysicalMb);

/// <summary>资产探针状态。</summary>
public sealed record KppProbe(
    string? State,
    long? AssetsScanned,
    double? TexturesMb,
    double? MeshesMb,
    double? SoundsMb,
    double? DbTexturesMb,
    double? LastScanUnixMs);

/// <summary>单个分配热点。</summary>
public sealed record KppAllocator(
    string? Name,
    string? Assembly,
    double? BytesPerFrame);

/// <summary>内存段。</summary>
public sealed record KppMemory(
    double? MonoHeapMb,
    double? ReservedMb,
    double? MonoUsedMb,
    double? GcLastMs,
    double? GcAvgMs,
    long? GcCount,
    KppPadder? Padder,
    KppProbe? Probe,
    IReadOnlyList<KppAllocator> TopAllocators);

/// <summary>热点条目（§3.3 的 E）。无值字段在 JSON 里省略，这里一律可空。</summary>
public sealed record KppHotspot(
    string? Name,
    string? Assembly,
    string? Type,
    string? Friendly,
    string? Category,
    double? Ms,
    double? PeakMs,
    double? Pct,
    long? Calls,
    double? BytesPerFrame,
    bool? IsTufx);

/// <summary>四类热点榜。</summary>
public sealed record KppHotspots(
    IReadOnlyList<KppHotspot> Modules,
    IReadOnlyList<KppHotspot> Plugins,
    IReadOnlyList<KppHotspot> Methods,
    IReadOnlyList<KppHotspot> Parts,
    IReadOnlyList<KppHotspot> Assemblies);

/// <summary>诊断的问题条目。</summary>
public sealed record KppCulprit(
    string? Source,
    string? Name,
    string? Assembly,
    double? Ms,
    double? Pct);

/// <summary>诊断的单项检查。</summary>
public sealed record KppCheck(
    string? Id,
    string? Label,
    string? State,
    string? Value,
    string? Detail);

/// <summary>诊断卡（§3.4）。</summary>
public sealed record KppDiagnostics(
    string? Title,
    string? Description,
    string? Advice,
    string Severity,
    IReadOnlyList<KppCulprit> Culprits,
    IReadOnlyList<KppCheck> Checks);

/// <summary>时间线中的一帧。</summary>
public sealed record KppTimelineFrame(
    double? T,
    double? FrameMs,
    double? Module,
    double? Plugin,
    double? Physx,
    double? Render,
    double? Gpu,
    bool? Gc,
    double? Fps);

/// <summary>时间线环形缓冲，最新在末尾。</summary>
public sealed record KppTimeline(
    double? Window,
    IReadOnlyList<KppTimelineFrame> Frames);

/// <summary>掉帧尖峰。</summary>
public sealed record KppSpike(
    long? Frame,
    double? AtUnixMs,
    double? FrameMs,
    string? Reason,
    IReadOnlyList<KppHotspot> Culprits);

/// <summary>计数器。</summary>
public sealed record KppCounters(
    long? GcCount,
    long? SpikeCount,
    long? DroppedFrames,
    long? ReportExports);

/// <summary>
/// latest.json 的一份强类型快照。
/// </summary>
/// <remarks>
/// 所有字段都可缺：契约 §6 允许更高次版本（<c>kpp.telemetry/1.3</c>）只写自己有的字段，
/// 缺的部分由 UI 显示 <c>N/A</c>，不做默认值填充。
/// </remarks>
public sealed record KppTelemetrySnapshot(
    string? Schema,
    int? SchemaVersion,
    long? Seq,
    double? PublishedUnixMs,
    double? PublishIntervalMs,
    KppWriter? Writer,
    KppSession? Session,
    KppContext? Context,
    KppFrame? Frame,
    KppBreakdown? Breakdown,
    KppGpu? Gpu,
    IReadOnlyList<KppGpuBracket> GpuBrackets,
    KppMemory? Memory,
    KppHotspots? Hotspots,
    KppTimeline? Timeline,
    KppDiagnostics? Diagnostics,
    IReadOnlyList<KppSpike> Spikes,
    KppCounters? Counters)
{
    /// <summary>契约家族名（<c>kpp.telemetry</c>）。</summary>
    public const string SchemaFamily = "kpp.telemetry";

    /// <summary>UI 支持的主版本。</summary>
    public const int SupportedMajorVersion = 1;

    /// <summary>主版本是否兼容：只认 <c>kpp.telemetry/1</c>。</summary>
    public bool IsCompatible =>
        SchemaVersion is not null && SchemaVersion.Value == SupportedMajorVersion;

    /// <summary>发布周期（毫秒），缺失时给契约默认值 1000。</summary>
    public double EffectivePublishIntervalMs =>
        PublishIntervalMs is > 0 ? PublishIntervalMs.Value : 1000d;

    /// <summary>
    /// 容错解析：任何异常都吞在本方法内并转成 <paramref name="error"/>，绝不抛给调用方。
    /// </summary>
    /// <param name="json">latest.json 的完整文本。</param>
    /// <param name="snapshot">解析成功的快照。</param>
    /// <param name="error">失败原因（已本地化，可直接显示）。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryParse(string? json, out KppTelemetrySnapshot? snapshot, out string? error)
    {
        snapshot = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "遥测文件为空";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "遥测文件顶层不是 JSON 对象";
                return false;
            }

            var schema = KppJson.Str(root, "schema");
            var (family, version) = SplitSchema(schema);
            if (!string.Equals(family, SchemaFamily, StringComparison.OrdinalIgnoreCase))
            {
                error = "无法识别的 schema：" + (schema ?? "(缺失)");
                return false;
            }

            snapshot = new KppTelemetrySnapshot(
                schema,
                version,
                KppJson.Int64(root, "seq"),
                KppJson.Double(root, "publishedUnixMs"),
                KppJson.Double(root, "publishIntervalMs"),
                ParseWriter(root),
                ParseSession(root),
                ParseContext(root),
                ParseFrame(root),
                ParseBreakdown(root),
                ParseGpu(root),
                ParseArray(root, "gpuBrackets", ParseGpuBracket),
                ParseMemory(root),
                ParseHotspots(root),
                ParseTimeline(root),
                ParseDiagnostics(root),
                ParseArray(root, "spikes", ParseSpike),
                ParseCounters(root));
            return true;
        }
        catch (JsonException ex)
        {
            error = "遥测文件解析失败（JSON 非法）：" + ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error = "遥测文件读取失败：" + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 拆 <c>kpp.telemetry/1</c> 为家族名与主版本号。
    /// </summary>
    /// <param name="schema">原始 schema 文本。</param>
    /// <returns>家族名（不带版本）与主版本；无法解析时版本为 <c>null</c>。</returns>
    public static (string? Family, int? Major) SplitSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return (null, null);
        }

        var text = schema.Trim();
        var slash = text.LastIndexOf('/');
        if (slash <= 0 || slash == text.Length - 1)
        {
            return (text, null);
        }

        var family = text[..slash];
        var versionText = text[(slash + 1)..];

        // "1.3" 只取主版本，次版本不参与兼容判定（§6：更高次版本要能读）。
        var dot = versionText.IndexOf('.', StringComparison.Ordinal);
        var majorText = dot > 0 ? versionText[..dot] : versionText;
        return int.TryParse(majorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
            ? (family, major)
            : (family, null);
    }

    private static KppWriter? ParseWriter(JsonElement root)
    {
        if (!KppJson.TryObject(root, "writer", out var writer))
        {
            return null;
        }

        return new KppWriter(
            KppJson.Int64(writer, "pid"),
            KppJson.Str(writer, "dllVersion"),
            KppJson.Str(writer, "kspVersion"),
            KppJson.Str(writer, "unityVersion"),
            KppJson.Str(writer, "os"));
    }

    private static KppSession? ParseSession(JsonElement root)
    {
        if (!KppJson.TryObject(root, "session", out var session))
        {
            return null;
        }

        return new KppSession(
            KppJson.Str(session, "id"),
            KppJson.Double(session, "startedUnixMs"),
            KppJson.Double(session, "uptimeSec"),
            KppJson.Int64(session, "frameCount"));
    }

    private static KppContext? ParseContext(JsonElement root)
    {
        if (!KppJson.TryObject(root, "context", out var context))
        {
            return null;
        }

        return new KppContext(
            KppJson.Str(context, "scene"),
            KppJson.Str(context, "vesselName"),
            KppJson.Int64(context, "partCount"),
            KppJson.Int64(context, "modCount"),
            KppJson.Bool(context, "paused"),
            KppJson.Double(context, "timeScale"),
            KppJson.Double(context, "gameTimeSec"));
    }

    private static KppFrame? ParseFrame(JsonElement root)
    {
        if (!KppJson.TryObject(root, "frame", out var frame))
        {
            return null;
        }

        return new KppFrame(
            KppJson.Double(frame, "fps"),
            KppJson.Double(frame, "avgFps"),
            KppJson.Double(frame, "onePercentLowFps"),
            KppJson.Double(frame, "pointOnePercentLowFps"),
            KppJson.Double(frame, "frameMs"),
            KppJson.Double(frame, "avgFrameMs"),
            KppJson.Double(frame, "jitterMs"),
            KppJson.Double(frame, "ptr"),
            KppJson.Double(frame, "dt"),
            KppJson.Int64(frame, "physSteps"));
    }

    private static KppBreakdown? ParseBreakdown(JsonElement root)
    {
        if (!KppJson.TryObject(root, "breakdownMs", out var breakdown))
        {
            return null;
        }

        return new KppBreakdown(
            KppJson.Double(breakdown, "moduleScripts"),
            KppJson.Double(breakdown, "pluginScripts"),
            KppJson.Double(breakdown, "physx"),
            KppJson.Double(breakdown, "renderResidual"),
            KppJson.Double(breakdown, "camera"),
            KppJson.Double(breakdown, "tufxPostFx"),
            KppJson.Double(breakdown, "tufxCpu"),
            KppJson.Double(breakdown, "gc"));
    }

    private static KppGpu? ParseGpu(JsonElement root)
    {
        if (!KppJson.TryObject(root, "gpu", out var gpu))
        {
            return null;
        }

        var stages = new List<KppGpuStage>();
        if (KppJson.TryArray(gpu, "stages", out var stageArray))
        {
            foreach (var item in stageArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var key = KppJson.Str(item, "key");
                var valid = KppJson.Bool(item, "valid");

                // §3.1：valid=false 时 ms 必须是 null。写方违约时这里主动纠正成 null，
                // 而不是把 0.0 一路带到界面上假装那个阶段花了 0 毫秒。
                var ms = valid == false ? null : KppJson.Double(item, "ms");
                stages.Add(new KppGpuStage(key, ms, valid));
            }
        }

        return new KppGpu(
            KppJson.Bool(gpu, "available"),
            KppJson.Str(gpu, "statusText"),
            KppJson.Double(gpu, "frameSpanMs"),
            KppJson.Double(gpu, "stageTotalMs"),
            KppJson.Double(gpu, "unaccountedMs"),
            KppJson.Int64(gpu, "publishedFrames"),
            KppJson.Int64(gpu, "invalidStageFrames"),
            KppJson.Int64(gpu, "dropped"),
            KppJson.Int64(gpu, "reordered"),
            stages);
    }

    private static KppGpuBracket ParseGpuBracket(JsonElement item) => new(
        KppJson.Str(item, "camera"),
        KppJson.Str(item, "eventName"),
        KppJson.Str(item, "owner"),
        KppJson.Double(item, "ms"),
        KppJson.Int64(item, "samples"));

    private static KppMemory? ParseMemory(JsonElement root)
    {
        if (!KppJson.TryObject(root, "memory", out var memory))
        {
            return null;
        }

        KppPadder? padder = null;
        if (KppJson.TryObject(memory, "padder", out var padderElement))
        {
            padder = new KppPadder(
                KppJson.Bool(padderElement, "enabled"),
                KppJson.Double(padderElement, "targetMb"),
                KppJson.Double(padderElement, "heldMb"),
                KppJson.Double(padderElement, "maxMb"),
                KppJson.Double(padderElement, "physicalMb"));
        }

        KppProbe? probe = null;
        if (KppJson.TryObject(memory, "probe", out var probeElement))
        {
            probe = new KppProbe(
                KppJson.Str(probeElement, "state"),
                KppJson.Int64(probeElement, "assetsScanned"),
                KppJson.Double(probeElement, "texturesMb"),
                KppJson.Double(probeElement, "meshesMb"),
                KppJson.Double(probeElement, "soundsMb"),
                KppJson.Double(probeElement, "dbTexturesMb"),
                KppJson.Double(probeElement, "lastScanUnixMs"));
        }

        return new KppMemory(
            KppJson.Double(memory, "monoHeapMb"),
            KppJson.Double(memory, "reservedMb"),
            KppJson.Double(memory, "monoUsedMb"),
            KppJson.Double(memory, "gcLastMs"),
            KppJson.Double(memory, "gcAvgMs"),
            KppJson.Int64(memory, "gcCount"),
            padder,
            probe,
            ParseArray(memory, "topAllocators", item => new KppAllocator(
                KppJson.Str(item, "name"),
                KppJson.Str(item, "assembly"),
                KppJson.Double(item, "bytesPerFrame"))));
    }

    private static KppHotspots? ParseHotspots(JsonElement root)
    {
        if (!KppJson.TryObject(root, "hotspots", out var hotspots))
        {
            return null;
        }

        return new KppHotspots(
            ParseArray(hotspots, "modules", ParseHotspot),
            ParseArray(hotspots, "plugins", ParseHotspot),
            ParseArray(hotspots, "methods", ParseHotspot),
            ParseArray(hotspots, "parts", ParseHotspot),
            ParseArray(hotspots, "assemblies", ParseHotspot));
    }

    private static KppHotspot ParseHotspot(JsonElement item) => new(
        KppJson.Str(item, "name"),
        KppJson.Str(item, "assembly"),
        KppJson.Str(item, "type"),
        KppJson.Str(item, "friendly"),
        KppJson.Str(item, "category"),
        KppJson.Double(item, "ms"),
        KppJson.Double(item, "peakMs"),
        KppJson.Double(item, "pct"),
        KppJson.Int64(item, "calls"),
        KppJson.Double(item, "bytesPerFrame"),
        KppJson.Bool(item, "isTufx"));

    private static KppTimeline? ParseTimeline(JsonElement root)
    {
        if (!KppJson.TryObject(root, "timeline", out var timeline))
        {
            return null;
        }

        return new KppTimeline(
            KppJson.Double(timeline, "window"),
            ParseArray(timeline, "frames", item => new KppTimelineFrame(
                KppJson.Double(item, "t"),
                KppJson.Double(item, "frameMs"),
                KppJson.Double(item, "module"),
                KppJson.Double(item, "plugin"),
                KppJson.Double(item, "physx"),
                KppJson.Double(item, "render"),
                KppJson.Double(item, "gpu"),
                KppJson.Bool(item, "gc"),
                KppJson.Double(item, "fps"))));
    }

    private static KppDiagnostics? ParseDiagnostics(JsonElement root)
    {
        if (!KppJson.TryObject(root, "diagnostics", out var diagnostics))
        {
            return null;
        }

        return new KppDiagnostics(
            KppJson.Str(diagnostics, "title"),
            KppJson.Str(diagnostics, "description"),
            KppJson.Str(diagnostics, "advice"),
            NormalizeSeverity(KppJson.Str(diagnostics, "severity")),
            ParseArray(diagnostics, "culprits", item => new KppCulprit(
                KppJson.Str(item, "source"),
                KppJson.Str(item, "name"),
                KppJson.Str(item, "assembly"),
                KppJson.Double(item, "ms"),
                KppJson.Double(item, "pct"))),
            ParseArray(diagnostics, "checks", item => new KppCheck(
                KppJson.Str(item, "id"),
                KppJson.Str(item, "label"),
                NormalizeCheckState(KppJson.Str(item, "state")),
                KppJson.Str(item, "value"),
                KppJson.Str(item, "detail"))));
    }

    private static KppSpike ParseSpike(JsonElement item) => new(
        KppJson.Int64(item, "frame"),
        KppJson.Double(item, "atUnixMs"),
        KppJson.Double(item, "frameMs"),
        KppJson.Str(item, "reason"),
        ParseArray(item, "culprits", ParseHotspot));

    private static KppCounters? ParseCounters(JsonElement root)
    {
        if (!KppJson.TryObject(root, "counters", out var counters))
        {
            return null;
        }

        return new KppCounters(
            KppJson.Int64(counters, "gcCount"),
            KppJson.Int64(counters, "spikeCount"),
            KppJson.Int64(counters, "droppedFrames"),
            KppJson.Int64(counters, "reportExports"));
    }

    private static string NormalizeSeverity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ok" => "ok",
        "warn" or "warning" => "warn",
        "critical" or "error" => "critical",
        _ => "unknown",
    };

    private static string NormalizeCheckState(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ok" => "ok",
        "warn" or "warning" => "warn",
        "critical" or "error" => "critical",
        _ => "unknown",
    };

    private static IReadOnlyList<T> ParseArray<T>(JsonElement parent, string name, Func<JsonElement, T> parse)
    {
        if (!KppJson.TryArray(parent, name, out var array))
        {
            return Array.Empty<T>();
        }

        var items = new List<T>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                items.Add(parse(item));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                // 单条坏数据只丢这一条：热点榜里一条畸形条目不该把整页打成"无数据"。
            }
        }

        return items;
    }
}

/// <summary>
/// status.json —— 桥健康度（契约 §4）。
/// </summary>
/// <remarks>独立于 latest.json：它让 UI 能区分"Mod 没装"、"Mod 在线但写不出数据"和"游戏已退出"。</remarks>
public sealed record KppStatus(
    string? Schema,
    bool? WriterAlive,
    long? Pid,
    double? UptimeSec,
    double? PublishIntervalMs,
    double? LastPublishUnixMs,
    double? LastPublishMs,
    long? WriteFailures,
    long? CommandsSeen,
    long? CommandsApplied,
    long? CommandsFailed,
    string? BridgeVersion,
    string? TelemetryDirectory)
{
    /// <summary>主版本是否兼容（与 latest.json 同一套口径）。</summary>
    public bool IsCompatible =>
        KppTelemetrySnapshot.SplitSchema(Schema).Major == KppTelemetrySnapshot.SupportedMajorVersion;

    /// <summary>容错解析 status.json；失败只返回 false + 原因。</summary>
    /// <param name="json">status.json 文本。</param>
    /// <param name="status">解析结果。</param>
    /// <param name="error">失败原因。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryParse(string? json, out KppStatus? status, out string? error)
    {
        status = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "status.json 为空";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "status.json 顶层不是 JSON 对象";
                return false;
            }

            status = new KppStatus(
                KppJson.Str(root, "schema"),
                KppJson.Bool(root, "writerAlive"),
                KppJson.Int64(root, "pid"),
                KppJson.Double(root, "uptimeSec"),
                KppJson.Double(root, "publishIntervalMs"),
                KppJson.Double(root, "lastPublishUnixMs"),
                KppJson.Double(root, "lastPublishMs"),
                KppJson.Int64(root, "writeFailures"),
                KppJson.Int64(root, "commandsSeen"),
                KppJson.Int64(root, "commandsApplied"),
                KppJson.Int64(root, "commandsFailed"),
                KppJson.Str(root, "bridgeVersion"),
                KppJson.Str(root, "telemetryDirectory"));
            return true;
        }
        catch (JsonException ex)
        {
            error = "status.json 解析失败（JSON 非法）：" + ex.Message;
            return false;
        }
    }
}

/// <summary>
/// JSON 读字段的统一口径：类型不符、缺字段、null 一律返回 <c>null</c>（= 该能力不可用）。
/// </summary>
internal static class KppJson
{
    public static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    public static bool TryArray(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Array;
    }

    public static string? Str(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static double? Double(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDouble(out var number) ? number : null;

            // 写方偶尔把数字写成字符串（Mono 侧的 InvariantCulture 拼接），这里容忍一次；
            // 仍然解析失败就是不可用，不给 0。
            case JsonValueKind.String:
                return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;

            default:
                return null;
        }
    }

    public static long? Int64(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var integer))
                {
                    return integer;
                }

                return value.TryGetDouble(out var number) && number is >= long.MinValue and <= long.MaxValue
                    ? (long)number
                    : null;

            case JsonValueKind.String:
                return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;

            default:
                return null;
        }
    }

    public static bool? Bool(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetDouble(out var number) ? Math.Abs(number) > double.Epsilon : null,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : null,
            _ => null,
        };
    }
}
