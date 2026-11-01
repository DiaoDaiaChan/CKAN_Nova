using System.Text.Json;

namespace CkanUI.ModText;

/// <summary>
/// 模组分类定义。
/// </summary>
public sealed record ModCategoryDefinition(
    string Id,
    string NameZh,
    string NameEn,
    string Icon,
    string Description,
    IReadOnlyList<string> OfficialTags,
    IReadOnlyList<string> Keywords)
{
    public string DisplayTitle => $"{Icon} {NameZh}";
}

/// <summary>
/// 纯内存模组分类分析与引擎。基于官方元数据 tags 与混合 NLP 规则，将 CKAN 所有模组智能分类到 8 大核心主题。
/// </summary>
public static class ModTaxonomyEngine
{
    public const string CatVisualsAudio = "visuals_audio";
    public const string CatToolsUtilities = "tools_utilities";
    public const string CatPartsHardware = "parts_hardware";
    public const string CatPlanetsWorlds = "planets_worlds";
    public const string CatRealismPhysics = "realism_physics";
    public const string CatCareerLifeSupport = "career_life_support";
    public const string CatCombatMilitary = "combat_military";
    public const string CatCoreDependencies = "core_dependencies";

    public static readonly IReadOnlyList<ModCategoryDefinition> Categories = new List<ModCategoryDefinition>
    {
        new(
            CatVisualsAudio,
            "视觉与音效美化",
            "Visuals & Audio",
            "🎨",
            "画质升级、光影大气、地表置换、排气羽流、座舱视角、环境音效与太空服材质",
            new[] { "graphics", "sound", "suits", "first-person" },
            new[]
            {
                "texture", "scatterer", "visual", "waterfall", "plume", "parallax", "sound", "audio",
                "music", "reshade", "skybox", "lighting", "eve", "clouds", "iva", "interior",
                "shader", "recolor", "enhancement", "visuals", "scenery", "sunset", "sunflare",
                "atmosphere visual", "graphical", "graphics", "chatterer", "soundtrack", "voice",
                "ambient", "radio chatter", "suit", "spacesuit", "helmet", "cockpit view"
            }),
        new(
            CatToolsUtilities,
            "实用工具与信息辅助",
            "Tools & Utilities",
            "🛠️",
            "飞行数据HUD、自动驾驶、装配增强、弹道预估、便捷操作与系统稳定性修复",
            new[] { "convenience", "information", "control", "editor", "app" },
            new[]
            {
                "tool", "helper", "engineer", "autopilot", "hud", "calculator", "alarm", "window",
                "trajectory", "editor", "warp", "transfer", "camera", "docking aid", "shortcuts",
                "fix", "navball", "display", "telemetry", "mechjeb", "toolbar", "action group",
                "altimeter", "gauge", "hotkey", "shortcut", "quickstart", "flight indicator",
                "flight data", "delta-v", "delta v", "maneuver", "node", "gui", "menu", "search",
                "sort", "filter", "craft manager", "save manager", "backup", "keybind"
            }),
        new(
            CatPartsHardware,
            "部件扩展与制造",
            "Parts & Hardware",
            "🚀",
            "火箭发动机、油箱、货舱、结构件、无人探针核心、机械臂与航空载具组件",
            new[] { "parts", "uncrewed", "stock-inventory" },
            new[]
            {
                "part", "engine", "tank", "capsule", "wing", "booster", "solar", "rover", "propeller",
                "wheel", "payload", "fairing", "adapter", "structure", "aerospace", "fuselage",
                "station", "probe", "hardware", "plane", "aircraft", "jet", "rocket", "cockpit",
                "decoupler", "rcs thruster", "vernor", "ssto", "lander", "cargo", "freighter",
                "fuel tank", "strut", "ladder", "docking port", "gear", "chute", "parachute",
                "heat shield", "interstage", "truss", "service module", "satellite"
            }),
        new(
            CatPlanetsWorlds,
            "星系与天体扩展",
            "Planets & Worlds",
            "🪐",
            "行星包、太阳系重置、系外行星、地表地貌置换与地面静态发射设施",
            new[] { "planet-pack", "buildings" },
            new[]
            {
                "planet", "system", "moon", "star", "kopernicus", "solar system", "celestial",
                "orbit", "terrain", "surface", "launchsite", "space center", "building", "structure",
                "base", "statue", "jool", "kerbol", "rss", "opm", "gas giant", "exoplanet",
                "kerbin", "duna", "laythe", "mun", "minmus", "galaxy", "planetary", "crater", "biome"
            }),
        new(
            CatRealismPhysics,
            "真实模拟与物理重构",
            "Realism & Physics",
            "⚙️",
            "真实气动、N体引力、再入烧蚀、结构刚度、深空通信拟真与推进剂挥发",
            new[] { "physics", "resources", "comms" },
            new[]
            {
                "physics", "aerodynamics", "far", "ferram", "principia", "gravitation", "n-body",
                "heat", "reentry", "thermal", "rigidity", "joint", "antenna", "frequency", "signal",
                "resource", "propellant", "boiloff", "realism overhaul", "realism", "remotetech",
                "radiation", "orbital decay", "atmospheric drag", "lift", "aerodynamic"
            }),
        new(
            CatCareerLifeSupport,
            "生存、科研与生涯深度",
            "Career & Life Support",
            "🧬",
            "生命维持系统、辐射暴露、科技树演进、深空探测科研与经济合同机制",
            new[] { "crewed", "science", "tech-tree", "career", "agency" },
            new[]
            {
                "life support", "kerbalism", "tac", "oxygen", "food", "habitation", "radiation",
                "science", "experiment", "contract", "mission", "tech tree", "research", "career",
                "fund", "reputation", "crew", "scansat", "contract configurator", "milestone",
                "tourist", "astronaut", "salary", "payroll", "budget", "training"
            }),
        new(
            CatCombatMilitary,
            "武器防务与拓展玩法",
            "Combat & Military",
            "⚔️",
            "空战对抗、制导导弹、舰载火炮、防空雷达、装甲系统与机构旗帜",
            new[] { "combat", "flags" },
            new[]
            {
                "weapon", "missile", "bomb", "gun", "cannon", "turret", "radar", "armor",
                "bdarmory", "combat", "warfare", "military", "flag", "badge", "emblem",
                "target", "bullet", "bulletproof", "warhead", "torpedo"
            }),
        new(
            CatCoreDependencies,
            "底层核心与前置库",
            "Core & Dependencies",
            "🧱",
            "代码补丁注入、全局配置管理、底层共用库、多部件切换内核与插件框架",
            new[] { "library", "plugin", "config" },
            new[]
            {
                "library", "api", "framework", "patch", "modulemanager", "module manager", "core",
                "dependency", "loader", "b9partswitch", "harmony", "wrapper", "plugin", "backend",
                "common library", "shared library", "utilities library"
            })
    };

    private static readonly Dictionary<string, ModCategoryDefinition> CategoryById =
        Categories.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] FunctionalPriority =
    {
        CatCombatMilitary,
        CatVisualsAudio,
        CatPlanetsWorlds,
        CatRealismPhysics,
        CatCareerLifeSupport,
        CatToolsUtilities,
        CatPartsHardware
    };

    /// <summary>
    /// 根据分类 ID 获取分类定义。
    /// </summary>
    public static ModCategoryDefinition? GetCategory(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return CategoryById.TryGetValue(id, out var def) ? def : null;
    }

    /// <summary>
    /// 对模组元数据进行分类判定。
    /// </summary>
    public static ModCategoryDefinition Classify(
        string identifier,
        string? name,
        IReadOnlyList<string>? tags,
        string? @abstract = null,
        string? description = null)
    {
        var rawTags = tags is { Count: > 0 }
            ? new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 优先按功能性 official tags 匹配
        foreach (var catId in FunctionalPriority)
        {
            var def = CategoryById[catId];
            if (def.OfficialTags.Any(t => rawTags.Contains(t)))
            {
                return def;
            }
        }

        // 2. 文本混合分析
        var textCorpus = $"{identifier} {name} {@abstract} {description}".ToLowerInvariant();

        ModCategoryDefinition? bestCat = null;
        var bestScore = 0;

        foreach (var catId in FunctionalPriority)
        {
            var def = CategoryById[catId];
            var score = 0;
            foreach (var kw in def.Keywords)
            {
                if (textCorpus.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    score++;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestCat = def;
            }
        }

        if (bestScore >= 1 && bestCat is not null)
        {
            return bestCat;
        }

        // 3. 官方底层库标签
        var coreDef = CategoryById[CatCoreDependencies];
        if (coreDef.OfficialTags.Any(t => rawTags.Contains(t)))
        {
            return coreDef;
        }

        // 4. 全分类关键词检索
        foreach (var def in Categories)
        {
            var score = 0;
            foreach (var kw in def.Keywords)
            {
                if (textCorpus.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    score++;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestCat = def;
            }
        }

        if (bestCat is not null)
        {
            return bestCat;
        }

        // 5. 兜底判定
        var isPartLike = textCorpus.Contains("pack") || textCorpus.Contains("craft") ||
                         textCorpus.Contains("vessel") || textCorpus.Contains("mod");

        return isPartLike ? CategoryById[CatPartsHardware] : CategoryById[CatCoreDependencies];
    }
}
