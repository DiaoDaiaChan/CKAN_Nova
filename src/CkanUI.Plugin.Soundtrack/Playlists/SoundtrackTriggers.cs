using System.Globalization;

namespace CkanUI.Plugin.Soundtrack.Playlists;

/// <summary>
/// playWhen 里的一条条件，已经翻译成界面能直接绑定的人话。
/// </summary>
/// <remarks>
/// <see cref="Raw"/> 保留文件里的原始写法，<see cref="Tooltip"/> 用它拼出 `键 = 值`：
/// 中文标签只是帮助阅读，用户想核对 cfg 时不能被我翻译过的文字挡住。
/// </remarks>
public sealed record SoundtrackCondition(string Key, string Label, string Value, string Raw)
{
    public string Tooltip => Raw.Length == 0
        ? $"{Key}：playWhen 里没有写这个键"
        : $"{Key} = {Raw}";
}

/// <summary>
/// 流程画布上的一节：从「几首曲子」一路走到「什么条件下播」。
/// </summary>
/// <remarks>画布按场景做根节点、每条播放列表向右拉出一条链，所以链上不再重复 scene。
/// <see cref="IsLast"/> 让模板知道要不要在节点后面接箭头——最后接一个箭头会指向空气。</remarks>
public sealed record SoundtrackFlowNode(string Label, string Value, bool IsLast, string Tooltip = "");

/// <summary>
/// SoundtrackEditorForked 的 playWhen 触发器目录。
/// </summary>
/// <remarks>
/// <para>键名与取值来自 <c>SoundtrackEditorForked/Persistor.cs</c> 的 ParsePlayWhen 与写回逻辑，
/// 语义来自 <c>Playlist.cs</c> 的 Prerequisites 判定（全部条件 AND，flags 类要求“允许集包含当前值”）。
/// 目录不复制运行为，只负责把文件里的键值翻译成能读的条件；页面依旧不编辑 playWhen。</para>
/// <para>数值条件在文件里是 min/max 两个键，界面合成一条“范围”；默认值（float.MaxValue / MinValue / 0）
/// 视为没有限制，不显示，避免每个列表都挂一排 3.4E+38。</para>
/// </remarks>
public static class SoundtrackTriggers
{
    public const string SceneKey = "scene";
    public const string BodyNameKey = "bodyName";

    // 判定顺序与 Playlist.PrerequisitesMet 一致：场景之外，先看飞船，再看数值，最后看大气层与生命周期。
    private static readonly (string Key, string Label)[] ScalarOrder =
    {
        (BodyNameKey, "星球"),
        ("timeOfDay", "时段"),
        ("cameraMode", "视角"),
        ("situation", "飞行状态"),
        ("vesselType", "载具类型"),
        ("paused", "暂停"),
        ("inAtmosphere", "大气层"),
        ("vesselState", "载具生命周期"),
    };

    // 数值触发器的文件键是 min/max 成对出现的，界面只显示一条范围。
    internal static readonly (string MinKey, string MaxKey, string Label, string Unit, bool Kilo)[] Ranges =
    {
        ("minAltitude", "maxAltitude", "高度", "m", true),
        ("minVelocitySurface", "maxVelocitySurface", "表面速度", "m/s", false),
        ("minVelocityOrbital", "maxVelocityOrbital", "轨道速度", "m/s", false),
        ("minVesselDistance", "maxVesselDistance", "最近飞船距离", "m", true),
    };

    internal static readonly Dictionary<string, string> SceneNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Loading"] = "载入中",
        ["LoadingBuffer"] = "载入缓冲",
        ["MainMenu"] = "主菜单",
        ["Settings"] = "设置",
        ["Credits"] = "制作名单",
        ["SpaceCentre"] = "航天中心",
        ["VAB"] = "载具组装厂",
        ["SPH"] = "航天器机库",
        ["TrackingStation"] = "跟踪站",
        ["Flight"] = "飞行中",
        ["PSystem"] = "行星系统",
        ["AstronautComplex"] = "宇航员中心",
        ["KSPedia"] = "KSPedia",
        ["MissionControl"] = "任务控制中心",
        ["RnDComplex"] = "研发中心",
        ["AdminFacility"] = "行政大楼",
        ["Any"] = "任意场景",
    };

    internal static readonly Dictionary<string, string> TimeOfDayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NightAM"] = "凌晨",
        ["TwilightAM"] = "黎明",
        ["DayAM"] = "上午",
        ["DayPM"] = "下午",
        ["TwilightPM"] = "黄昏",
        ["NightPM"] = "夜晚",
        ["Any"] = "任意时段",
    };

    internal static readonly Dictionary<string, string> CameraModeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flight"] = "飞行",
        ["Map"] = "地图",
        ["External"] = "外部",
        ["IVA"] = "舱内",
        ["Internal"] = "内部",
        ["Any"] = "任意视角",
    };

    internal static readonly Dictionary<string, string> SituationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PRELAUNCH"] = "发射前",
        ["LANDED"] = "已着陆",
        ["SPLASHED"] = "已溅落",
        ["FLYING"] = "飞行中",
        ["SUB_ORBITAL"] = "亚轨道",
        ["ORBITING"] = "环绕中",
        ["ESCAPING"] = "逃逸中",
        ["DOCKED"] = "已对接",
    };

    internal static readonly Dictionary<string, string> VesselTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Debris"] = "残骸",
        ["SpaceObject"] = "太空物体",
        ["Unknown"] = "未知",
        ["Probe"] = "探测器",
        ["Relay"] = "中继卫星",
        ["Rover"] = "巡视器",
        ["Lander"] = "着陆器",
        ["Ship"] = "飞船",
        ["Plane"] = "飞机",
        ["Station"] = "空间站",
        ["Base"] = "基地",
        ["EVA"] = "舱外活动",
        ["Flag"] = "旗帜",
        ["DeployedScienceController"] = "部署科学控制器",
        ["DeployedSciencePart"] = "部署科学部件",
        ["DroppedPart"] = "掉落的部件",
        ["DeployedGroundPart"] = "地面部署部件",
        ["Any"] = "任意类型",
    };

    internal static readonly Dictionary<string, string> VesselStateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Active"] = "活动中",
        ["Inactive"] = "未激活",
        ["Dead"] = "已损毁",
        ["Any"] = "任意状态",
    };

    /// <summary>
    /// 把一份播放列表的 playWhen 翻译成有序条件。未知键按原样列在最后：界面看不懂，也不能假装它不存在。
    /// </summary>
    /// <param name="playlist">播放列表。</param>
    /// <param name="includeScene">是否把 scene 也作为一条条件（节点树把 scene 当分组标题，行内不再重复）。</param>
    public static IReadOnlyList<SoundtrackCondition> Describe(SoundtrackPlaylist playlist, bool includeScene = true)
    {
        if (playlist is null)
        {
            return Array.Empty<SoundtrackCondition>();
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var pair in playlist.PlayWhen)
        {
            if (!values.ContainsKey(pair.Key))
            {
                order.Add(pair.Key);
            }

            // Upsert：同一个键写两遍时后者生效，与 Soundtrack 的读取结果一致。
            values[pair.Key] = pair.Value;
        }

        var conditions = new List<SoundtrackCondition>();
        if (includeScene)
        {
            conditions.Add(values.TryGetValue(SceneKey, out var scene)
                ? new SoundtrackCondition(SceneKey, "场景", DescribeScene(scene), scene)
                : new SoundtrackCondition(SceneKey, "场景", "任何场景", string.Empty));
        }

        foreach (var (key, label) in ScalarOrder)
        {
            if (values.TryGetValue(key, out var value))
            {
                conditions.Add(new SoundtrackCondition(key, label, DescribeScalar(key, value), value));
            }
        }

        foreach (var range in Ranges)
        {
            var hasMin = TryParse(values, range.MinKey, out var min);
            var hasMax = TryParse(values, range.MaxKey, out var max);
            if (TryFormatRange(hasMin, min, hasMax, max, range.Unit, range.Kilo, out var text, out var raw))
            {
                conditions.Add(new SoundtrackCondition(range.MinKey, range.Label, text, raw));
            }
        }

        foreach (var key in order)
        {
            if (!IsKnown(key))
            {
                conditions.Add(new SoundtrackCondition(key, key, values[key], values[key]));
            }
        }

        return conditions;
    }

    /// <summary>把 scene 值拆成枚举名列表（`VAB | SPH`）。</summary>
    public static IReadOnlyList<string> SplitFlags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(new[] { '|', '&', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// playWhen 里的 bodyName，去空格后返回；没写或写了空串都返回空串（表示任意星球）。
    /// 同一个键写两遍时取最后一条，和 <see cref="Describe"/> 的 Upsert 读取一致。
    /// </summary>
    public static string BodyNameOf(SoundtrackPlaylist playlist)
    {
        if (playlist is null)
        {
            return string.Empty;
        }

        var value = string.Empty;
        foreach (var pair in playlist.PlayWhen)
        {
            if (pair.Key.Equals(BodyNameKey, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
            }
        }

        return value.Trim();
    }

    /// <summary>scene 的中文名；不认识的枚举名原样返回，宁可显示 `KSPedia2` 也不吞掉。</summary>
    public static string SceneName(string scene) => SceneNames.TryGetValue(scene, out var name) ? name : scene;

    /// <summary>playWhen 的 `键 = 值` 原文，一行一个，给详情区做核对用。</summary>
    public static string DescribeRaw(SoundtrackPlaylist playlist)
        => playlist.PlayWhen.Count == 0
            ? string.Empty
            : string.Join(" · ", playlist.PlayWhen.Select(pair => pair.Key + " = " + pair.Value));

    private static string DescribeScene(string value)
    {
        var flags = SplitFlags(value);
        return flags.Count == 0
            ? "任何场景"
            : string.Join("、", flags.Select(SceneName));
    }

    private static string DescribeScalar(string key, string value)
    {
        var text = value.Trim();
        return key.ToLowerInvariant() switch
        {
            "timeofday" => JoinFlags(text, TimeOfDayNames),
            "cameramode" => JoinFlags(text, CameraModeNames),
            "situation" => JoinFlags(text, SituationNames),
            "vesseltype" => JoinFlags(text, VesselTypeNames),
            "vesselstate" => JoinFlags(text, VesselStateNames),
            "paused" => text.Equals("True", StringComparison.OrdinalIgnoreCase) ? "已暂停" : "未暂停",
            "inatmosphere" => text.Equals("True", StringComparison.OrdinalIgnoreCase) ? "大气层内" : "大气层外",
            "bodyname" => text.Length == 0 ? "任何星球" : text,
            _ => text,
        };
    }

    private static string JoinFlags(string value, IReadOnlyDictionary<string, string> names)
    {
        var flags = SplitFlags(value);
        if (flags.Count == 0)
        {
            return value;
        }

        return string.Join("、", flags.Select(flag => names.TryGetValue(flag, out var name) ? name : flag));
    }

    private static bool TryParse(IReadOnlyDictionary<string, string> values, string key, out float value)
    {
        value = 0f;
        return values.TryGetValue(key, out var raw)
            && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryFormatRange(
        bool hasMin,
        float min,
        bool hasMax,
        float max,
        string unit,
        bool kilo,
        out string text,
        out string raw)
    {
        text = string.Empty;
        raw = string.Empty;

        // 模型默认值就是“没有限制”：它们出现在文件里也不该变成一排条件。
        var lower = hasMin && min > float.MinValue && min != 0f ? min : (float?)null;
        var upper = hasMax && max < float.MaxValue ? max : (float?)null;
        if (lower is null && upper is null)
        {
            return false;
        }

        var minText = lower is null ? string.Empty : FormatNumber(lower.Value, unit, kilo);
        var maxText = upper is null ? string.Empty : FormatNumber(upper.Value, unit, kilo);
        text = lower is null
            ? "≤ " + maxText
            : upper is null
                ? "≥ " + minText
                : minText + " – " + maxText;
        raw = string.Join(" · ", new[]
        {
            lower is null ? null : "min=" + lower.Value.ToString(CultureInfo.InvariantCulture),
            upper is null ? null : "max=" + upper.Value.ToString(CultureInfo.InvariantCulture),
        }.Where(part => part is not null));
        return true;
    }

    private static string FormatNumber(float value, string unit, bool kilo)
    {
        if (kilo && Math.Abs(value) >= 1000f)
        {
            return (value / 1000f).ToString("0.##", CultureInfo.InvariantCulture) + " km";
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
    }

    private static bool IsKnown(string key)
    {
        if (key.Equals(SceneKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ScalarOrder.Any(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return Ranges.Any(range =>
            range.MinKey.Equals(key, StringComparison.OrdinalIgnoreCase)
            || range.MaxKey.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>界面编辑条件时，一个键该怎么填。</summary>
public enum SoundtrackTriggerKind
{
    /// <summary>枚举多选，如 VAB | SPH。</summary>
    Flags,

    /// <summary>True / False。</summary>
    Boolean,

    /// <summary>自由文本，如天体名。</summary>
    Text,

    /// <summary>数字，写成 float 的 invariant 形式。</summary>
    Number,
}

/// <summary>一个可选枚举值：文件里的写法 + 中文名。</summary>
public sealed record SoundtrackTriggerValue(string Key, string Label);

/// <summary>
/// 一个可编辑的 playWhen 键：标签、控件类型、可选值与默认值。
/// </summary>
/// <remarks>目录只描述“怎么填”，不负责写盘；真正的写回发生在选中列表的 Model.PlayWhen 上。</remarks>
public sealed record SoundtrackTriggerField(
    string Key,
    string Label,
    SoundtrackTriggerKind Kind,
    IReadOnlyList<SoundtrackTriggerValue> Options,
    string DefaultValue,
    string Hint)
{
    public bool HasOptions => Options.Count > 0;

    public bool IsFlags => Kind == SoundtrackTriggerKind.Flags;

    /// <summary>取值提示：枚举键列出全部枚举名，数值键给出单位，文本键给一句话。</summary>
    public string ValueHint => Options.Count > 0
        ? "可选：" + string.Join("、", Options.Select(option => option.Key)) + (IsFlags ? "（多选用 | 分隔）" : string.Empty)
        : Hint;
}

/// <summary>
/// playWhen 的可编辑键目录，与 <see cref="SoundtrackTriggers"/> 的 17 个键一一对应。
/// </summary>
/// <remarks>
/// <para>Soundtrack 读值时用 <c>Enums.Parse</c>，只按 <c>|</c> 与 <c>&amp;</c> 切分；写逗号会让整份文件解析失败并回落到原版播放列表，
/// 所以这里统一把多选值规范化成 A | B。</para>
/// <para>min* / max* 在文件里是成对的键，界面拆成两行编辑；保存前会检查 min ≤ max。</para>
/// </remarks>
public static class SoundtrackTriggerCatalog
{
    private static readonly SoundtrackTriggerValue[] SceneOptions = BuildOptions(SoundtrackTriggers.SceneNames,
        "Loading", "LoadingBuffer", "MainMenu", "Settings", "Credits", "SpaceCentre", "VAB", "SPH",
        "TrackingStation", "Flight", "PSystem", "AstronautComplex", "KSPedia", "MissionControl", "RnDComplex",
        "AdminFacility", "Any");

    private static readonly SoundtrackTriggerValue[] TimeOfDayOptions = BuildOptions(SoundtrackTriggers.TimeOfDayNames,
        "NightAM", "TwilightAM", "DayAM", "DayPM", "TwilightPM", "NightPM", "Any");

    private static readonly SoundtrackTriggerValue[] CameraModeOptions = BuildOptions(SoundtrackTriggers.CameraModeNames,
        "Flight", "Map", "External", "IVA", "Internal", "Any");

    private static readonly SoundtrackTriggerValue[] SituationOptions = BuildOptions(SoundtrackTriggers.SituationNames,
        "PRELAUNCH", "LANDED", "SPLASHED", "FLYING", "SUB_ORBITAL", "ORBITING", "ESCAPING", "DOCKED");

    private static readonly SoundtrackTriggerValue[] VesselTypeOptions = BuildOptions(SoundtrackTriggers.VesselTypeNames,
        "Debris", "SpaceObject", "Unknown", "Probe", "Relay", "Rover", "Lander", "Ship", "Plane", "Station",
        "Base", "EVA", "Flag", "DeployedScienceController", "DeployedSciencePart", "DroppedPart",
        "DeployedGroundPart", "Any");

    private static readonly SoundtrackTriggerValue[] VesselStateOptions = BuildOptions(SoundtrackTriggers.VesselStateNames,
        "Active", "Inactive", "Dead", "Any");

    private static readonly SoundtrackTriggerValue[] BooleanOptions =
    {
        new("True", "是"),
        new("False", "否"),
    };

    /// <summary>常见天体名，作为 bodyName 选择器的建议值；文档里出现过的名字会再合并进来。</summary>
    public static IReadOnlyList<string> BodyNames { get; } = new[]
    {
        "Sun", "Moho", "Eve", "Kerbin", "Mun", "Minmus", "Duna", "Ike", "Dres",
        "Jool", "Laythe", "Vall", "Tylo", "Bop", "Pol", "Eeloo",
    };

    public static IReadOnlyList<SoundtrackTriggerField> Fields { get; } = new[]
    {
        new SoundtrackTriggerField(SoundtrackTriggers.SceneKey, "场景", SoundtrackTriggerKind.Flags, SceneOptions, "Flight",
            "KSP 场景；多个场景用 | 分隔。"),
        new SoundtrackTriggerField(SoundtrackTriggers.BodyNameKey, "星球", SoundtrackTriggerKind.Text,
            Array.Empty<SoundtrackTriggerValue>(), "Kerbin", "天体英文名，如 Kerbin；留空表示任何星球。"),
        new SoundtrackTriggerField("timeOfDay", "时段", SoundtrackTriggerKind.Flags, TimeOfDayOptions, "DayAM",
            "按 KSC 当地时间判断，飞船在别的天体也一样。"),
        new SoundtrackTriggerField("cameraMode", "视角", SoundtrackTriggerKind.Flags, CameraModeOptions, "Flight",
            "飞行中的镜头模式。"),
        new SoundtrackTriggerField("situation", "飞行状态", SoundtrackTriggerKind.Flags, SituationOptions, "ORBITING",
            "飞船当前的 KSP 状态。"),
        new SoundtrackTriggerField("vesselType", "载具类型", SoundtrackTriggerKind.Flags, VesselTypeOptions, "Ship",
            "按载具分类过滤。"),
        new SoundtrackTriggerField("paused", "暂停", SoundtrackTriggerKind.Boolean, BooleanOptions, "False",
            "只在游戏暂停或只在运行时播放。"),
        new SoundtrackTriggerField("inAtmosphere", "大气层", SoundtrackTriggerKind.Boolean, BooleanOptions, "True",
            "大气密度大于 0 才算大气层内。"),
        new SoundtrackTriggerField("vesselState", "载具生命周期", SoundtrackTriggerKind.Flags, VesselStateOptions, "Active",
            "载具是否还活着。"),
        new SoundtrackTriggerField("minAltitude", "高度下限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "1000", "单位米；只写一边就只限一边。"),
        new SoundtrackTriggerField("maxAltitude", "高度上限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "70000", "单位米。"),
        new SoundtrackTriggerField("minVelocitySurface", "表面速度下限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "0", "单位 m/s。"),
        new SoundtrackTriggerField("maxVelocitySurface", "表面速度上限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "100", "单位 m/s。"),
        new SoundtrackTriggerField("minVelocityOrbital", "轨道速度下限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "2000", "单位 m/s。"),
        new SoundtrackTriggerField("maxVelocityOrbital", "轨道速度上限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "3000", "单位 m/s。"),
        new SoundtrackTriggerField("minVesselDistance", "最近飞船距离下限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "0",
            "单位米；判定是开区间，端点不触发。注意 4.7.1.8 把它读成 minAltitude，写了在游戏里不生效。"),
        new SoundtrackTriggerField("maxVesselDistance", "最近飞船距离上限", SoundtrackTriggerKind.Number,
            Array.Empty<SoundtrackTriggerValue>(), "1000", "单位米。"),
    };

    private static readonly Dictionary<string, SoundtrackTriggerField> ByKey =
        Fields.ToDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>按文件里的键找编辑描述；不认识的键返回 null（页面会把它当只读原样保留）。</summary>
    public static SoundtrackTriggerField? Find(string key)
        => ByKey.TryGetValue(key, out var field) ? field : null;

    /// <summary>把用户填的值规范化；失败时给出能直接贴进错误提示的原因。</summary>
    public static bool TryNormalize(SoundtrackTriggerField? field, string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (field is null)
        {
            error = "页面不认识这个键，无法校验取值。";
            return false;
        }

        var value = (raw ?? string.Empty).Trim();
        switch (field.Kind)
        {
            case SoundtrackTriggerKind.Flags:
            {
                var tokens = SoundtrackTriggers.SplitFlags(value);
                if (tokens.Count == 0)
                {
                    error = field.Label + "至少要选一个值。";
                    return false;
                }

                var canonical = new List<string>(tokens.Count);
                foreach (var token in tokens)
                {
                    var option = field.Options.FirstOrDefault(candidate =>
                        candidate.Key.Equals(token, StringComparison.OrdinalIgnoreCase));
                    if (option is null)
                    {
                        error = field.Label + "的取值「" + token + "」不是 Soundtrack 认识的枚举。可选："
                                + string.Join("、", field.Options.Select(candidate => candidate.Key));
                        return false;
                    }

                    if (!canonical.Contains(option.Key, StringComparer.Ordinal))
                    {
                        canonical.Add(option.Key);
                    }
                }

                normalized = string.Join(" | ", canonical);
                return true;
            }

            case SoundtrackTriggerKind.Boolean:
            {
                if (bool.TryParse(value, out var boolean))
                {
                    normalized = boolean ? "True" : "False";
                    return true;
                }

                error = field.Label + "只能填 True 或 False。";
                return false;
            }

            case SoundtrackTriggerKind.Number:
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number)
                    && float.IsFinite(number))
                {
                    normalized = number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }

                error = field.Label + "要填数字（小数点用 .）。";
                return false;
            }

            default:
                normalized = value;
                return true;
        }
    }

    private static SoundtrackTriggerValue[] BuildOptions(
        IReadOnlyDictionary<string, string> names,
        params string[] keys)
        => keys.Select(key => new SoundtrackTriggerValue(key, names.TryGetValue(key, out var label) ? label : key))
            .ToArray();
}

/// <summary>
/// 以 scene 为根把播放列表分组：节点树的一级节点就是“什么场景下会响”。
/// </summary>
public static class SoundtrackScenes
{
    /// <summary>没有写 scene 的播放列表落到这个分组键上。</summary>
    public const string UnrestrictedKey = "";

    private static readonly string[] CanonicalOrder =
    {
        "MainMenu", "SpaceCentre", "VAB", "SPH", "TrackingStation", "MissionControl",
        "AstronautComplex", "RnDComplex", "AdminFacility", "Flight", "PSystem",
        "Credits", "Settings", "KSPedia", "Loading", "LoadingBuffer", "Any",
    };

    /// <summary>播放列表的 scene 枚举名，按文件里的顺序。</summary>
    public static IReadOnlyList<string> KeysOf(SoundtrackPlaylist playlist)
    {
        var pair = playlist.PlayWhen.FirstOrDefault(entry =>
            entry.Key.Equals(SoundtrackTriggers.SceneKey, StringComparison.OrdinalIgnoreCase));
        return pair.Equals(default(KeyValuePair<string, string>))
            ? Array.Empty<string>()
            : SoundtrackTriggers.SplitFlags(pair.Value);
    }

    /// <summary>分组键：同一组 scene 的播放列表归到一起（`SPH|VAB` 与 `VAB|SPH` 视为同一组）。</summary>
    public static string GroupKey(SoundtrackPlaylist playlist)
    {
        var keys = KeysOf(playlist);
        return keys.Count == 0
            ? UnrestrictedKey
            : string.Join("|", keys.OrderBy(OrderOf));
    }

    /// <summary>一级节点的标题，如“载具组装厂、航天器机库”。</summary>
    public static string GroupTitle(string groupKey)
    {
        if (groupKey.Length == 0)
        {
            return "未限定场景";
        }

        return string.Join("、", groupKey.Split('|').Select(SoundtrackTriggers.SceneName));
    }

    /// <summary>一级节点的副标题：保留 cfg 里的原始枚举名，方便和文件对照。</summary>
    public static string GroupSubtitle(string groupKey)
        => groupKey.Length == 0 ? "playWhen 未写 scene" : groupKey.Replace("|", " | ");

    /// <summary>分组排序：未限定的排最前，其余按 KSP 的场景顺序；不认识的枚举名排在最后。</summary>
    public static int GroupOrder(string groupKey)
    {
        if (groupKey.Length == 0)
        {
            return -1;
        }

        return groupKey.Split('|').Min(OrderOf);
    }

    private static int OrderOf(string scene)
    {
        var index = Array.FindIndex(CanonicalOrder, candidate => candidate.Equals(scene, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 1000 : index;
    }
}
