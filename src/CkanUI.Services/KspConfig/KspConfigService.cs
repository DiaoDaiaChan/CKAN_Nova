using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.KspConfig;

namespace CkanUI.Services.KspConfig;

/// <summary>
/// KSP 原生配置（settings.cfg）读取、修改与 Squad 官方字典本地化提取服务。
/// </summary>
public sealed class KspConfigService : IKspConfigService
{
    private readonly IInstanceService? _instanceService;
    private readonly IErrorReporter? _errorReporter;

    /// <summary>
    /// 构造配置服务。
    /// </summary>
    public KspConfigService(
        IInstanceService? instanceService = null,
        IErrorReporter? errorReporter = null)
    {
        _instanceService = instanceService;
        _errorReporter = errorReporter;
    }

    /// <inheritdoc />
    public async Task<KspConfigDocument> LoadConfigAsync(string? instancePath = null, CancellationToken cancellationToken = default)
    {
        var gameDir = ResolveGameDir(instancePath);
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            throw new DirectoryNotFoundException($"无法定位 KSP 游戏目录：'{gameDir}'");
        }

        var settingsPath = Path.Combine(gameDir, "settings.cfg");
        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException($"找不到 settings.cfg 文件：'{settingsPath}'", settingsPath);
        }

        // 1. 读取 settings.cfg 原始行
        var rawLines = await File.ReadAllLinesAsync(settingsPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        // 2. 检测 settings.cfg 中配置的语言
        var language = DetectLanguage(rawLines);

        // 3. 读取官方多语言字典
        var localizationTable = await LoadOfficialLocalizationAsync(gameDir, language, cancellationToken).ConfigureAwait(false);

        // 4. 解析 settings.cfg 中的所有配置项并结合官方字典组装模型
        var settings = ParseSettings(rawLines, localizationTable);

        var backupPath = GetBackupPath(gameDir);

        return new KspConfigDocument
        {
            FilePath = settingsPath,
            Settings = settings,
            RawLines = rawLines,
            LocalizationTable = localizationTable,
            Language = language,
            HasBackup = !string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)
        };
    }

    /// <inheritdoc />
    public async Task<bool> SaveConfigAsync(
        KspConfigDocument document,
        IEnumerable<KspSettingItem> modifiedSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(modifiedSettings);

        var modifiedMap = modifiedSettings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (modifiedMap.Count == 0)
        {
            return true; // 没有需要写的改动
        }

        var settingsPath = document.FilePath;
        var backupPath = settingsPath + ".bak";

        try
        {
            // 1. 写盘前执行安全快照备份
            if (File.Exists(settingsPath))
            {
                File.Copy(settingsPath, backupPath, overwrite: true);
            }

            // 2. 逐行保真替换（保留空行、注释和缩进）
            var newLines = new List<string>(document.RawLines.Count + 10);
            var updatedKeys = new HashSet<string>(StringComparer.Ordinal);
            var depth = 0;

            foreach (var line in document.RawLines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(trimmed))
                {
                    newLines.Add(line);
                    continue;
                }

                if (trimmed.Contains('{'))
                {
                    depth += trimmed.Count(c => c == '{');
                }
                if (trimmed.Contains('}'))
                {
                    depth -= trimmed.Count(c => c == '}');
                }

                // 只处理深度为 0 的根级 key = value
                if (depth == 0 || (depth > 0 && !line.Contains('{')))
                {
                    var equalIndex = line.IndexOf('=');
                    if (equalIndex > 0)
                    {
                        var key = line[..equalIndex].Trim();
                        if (modifiedMap.TryGetValue(key, out var newValue))
                        {
                            var indent = line[..(line.IndexOf(key, StringComparison.Ordinal))];
                            newLines.Add($"{indent}{key} = {newValue}");
                            updatedKeys.Add(key);
                            continue;
                        }
                    }
                }

                newLines.Add(line);
            }

            // 3. 处理可能存在于修改项但原本文件中缺失的键
            foreach (var (missingKey, missingValue) in modifiedMap)
            {
                if (!updatedKeys.Contains(missingKey))
                {
                    newLines.Insert(0, $"{missingKey} = {missingValue}");
                }
            }

            // 4. 原子化写入
            var tempPath = settingsPath + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllLinesAsync(tempPath, newLines, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, settingsPath, overwrite: true);

            return true;
        }
        catch (Exception ex)
        {
            _errorReporter?.Report(ex, "KspConfigService", "保存 KSP 设置失败");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LoadOfficialLocalizationAsync(
        string? instancePath = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var gameDir = ResolveGameDir(instancePath);
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var dictPath = Path.Combine(gameDir, "GameData", "Squad", "Localization", "dictionary.cfg");
        if (!File.Exists(dictPath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var targetLang = string.IsNullOrWhiteSpace(language) ? "zh-cn" : language.Trim().ToLowerInvariant();
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var stream = File.OpenRead(dictPath);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            var inLocalization = false;
            var currentLang = string.Empty;
            var targetFound = false;

            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!inLocalization)
                {
                    if (string.Equals(trimmed, "Localization", StringComparison.OrdinalIgnoreCase))
                    {
                        inLocalization = true;
                    }
                    continue;
                }

                // 检查语言块
                if (trimmed.Length is >= 2 and <= 10 && !trimmed.Contains('=') && !trimmed.Contains('{') && !trimmed.Contains('}'))
                {
                    currentLang = trimmed.ToLowerInvariant();
                    if (string.Equals(currentLang, targetLang, StringComparison.OrdinalIgnoreCase))
                    {
                        targetFound = true;
                    }
                    continue;
                }

                if (trimmed == "}")
                {
                    if (!string.IsNullOrEmpty(currentLang))
                    {
                        if (string.Equals(currentLang, targetLang, StringComparison.OrdinalIgnoreCase) && dict.Count > 0)
                        {
                            // 目标语言已读完
                            break;
                        }
                        currentLang = string.Empty;
                    }
                    continue;
                }

                // 当前在目标语言块内，或者目标语言没找到但找到了任意语言块
                if ((targetFound && string.Equals(currentLang, targetLang, StringComparison.OrdinalIgnoreCase)) ||
                    (!targetFound && !string.IsNullOrEmpty(currentLang)))
                {
                    var equalIdx = trimmed.IndexOf('=');
                    if (equalIdx > 0 && trimmed.StartsWith('#'))
                    {
                        var key = trimmed[..equalIdx].Trim();
                        var val = trimmed[(equalIdx + 1)..].Trim();

                        // 格式化清理多语言文本
                        val = CleanLocalizationString(val);
                        dict[key] = val;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _errorReporter?.Report(ex, "KspConfigService", "提取 Squad 官方本地化字典异常");
        }

        return dict;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreBackupAsync(string? instancePath = null, CancellationToken cancellationToken = default)
    {
        var gameDir = ResolveGameDir(instancePath);
        if (string.IsNullOrWhiteSpace(gameDir)) return false;

        var settingsPath = Path.Combine(gameDir, "settings.cfg");
        var backupPath = GetBackupPath(gameDir);

        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return false;
        }

        try
        {
            await Task.Run(() => File.Copy(backupPath, settingsPath, overwrite: true), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _errorReporter?.Report(ex, "KspConfigService", "恢复 KSP settings.cfg 备份失败");
            return false;
        }
    }

    /// <inheritdoc />
    public string? GetBackupPath(string? instancePath = null)
    {
        var gameDir = ResolveGameDir(instancePath);
        if (string.IsNullOrWhiteSpace(gameDir)) return null;

        var defaultBak = Path.Combine(gameDir, "settings.cfg.bak");
        if (File.Exists(defaultBak))
        {
            return defaultBak;
        }

        return null;
    }

    // ---- 内部解析辅助 ------------------------------------------------------

    private string? ResolveGameDir(string? instancePath)
    {
        if (!string.IsNullOrWhiteSpace(instancePath) && Directory.Exists(instancePath))
        {
            return instancePath;
        }

        var currentInstance = _instanceService?.Current?.GameDir;
        if (!string.IsNullOrWhiteSpace(currentInstance) && Directory.Exists(currentInstance))
        {
            return currentInstance;
        }

        // 本机默认常用 KSP 路径探测
        const string fallbackDir = @"D:Kerbal Space Program_newmod";
        if (Directory.Exists(fallbackDir))
        {
            return fallbackDir;
        }

        return null;
    }

    private static string DetectLanguage(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("LANGUAGE", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split('=');
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    return parts[1].Trim().ToLowerInvariant();
                }
            }
        }

        return "zh-cn";
    }

    private static string CleanLocalizationString(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        // 处理转义换行
        var text = raw.Replace("\\n", " ").Replace("\n", " ");

        // 去除富文本标签如 <color=...> ... </color> 或 <b> ... </b>
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);

        // 去除尾随的冒号
        text = text.TrimEnd('：', ':', ' ');

        return text.Trim();
    }

    private static IReadOnlyList<KspSettingItem> ParseSettings(
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, string> locDict)
    {
        var result = new List<KspSettingItem>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var depth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.Contains('{'))
            {
                depth += trimmed.Count(c => c == '{');
            }
            if (trimmed.Contains('}'))
            {
                depth -= trimmed.Count(c => c == '}');
                continue;
            }

            // 只解析根级别配置键
            if (depth == 0)
            {
                var equalIndex = trimmed.IndexOf('=');
                if (equalIndex > 0)
                {
                    var key = trimmed[..equalIndex].Trim();
                    var val = trimmed[(equalIndex + 1)..].Trim();

                    if (!seenKeys.Add(key))
                    {
                        continue;
                    }

                    var item = BuildSettingItem(key, val, locDict);
                    result.Add(item);
                }
            }
        }

        return result;
    }

    private static KspSettingItem BuildSettingItem(
        string key,
        string value,
        IReadOnlyDictionary<string, string> locDict)
    {
        if (CuratedDescriptors.TryGetValue(key, out var descriptor))
        {
            var displayName = descriptor.DefaultName;
            if (!string.IsNullOrWhiteSpace(descriptor.LocKey) && locDict.TryGetValue(descriptor.LocKey, out var locName))
            {
                displayName = locName;
            }

            return new KspSettingItem
            {
                Key = key,
                Value = value,
                OriginalValue = value,
                LocKey = descriptor.LocKey,
                DisplayName = displayName,
                Description = descriptor.Description,
                Category = descriptor.Category,
                SettingType = descriptor.Type,
                Options = descriptor.Options,
                MinValue = descriptor.MinValue,
                MaxValue = descriptor.MaxValue,
                Step = descriptor.Step,
                Priority = descriptor.Priority
            };
        }

        // 未在精选列表中的通用项：根据值自动推断类型
        var inferredType = KspSettingType.Text;
        if (bool.TryParse(value, out _))
        {
            inferredType = KspSettingType.Boolean;
        }
        else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            inferredType = KspSettingType.Integer;
        }
        else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            inferredType = KspSettingType.Float;
        }

        return new KspSettingItem
        {
            Key = key,
            Value = value,
            OriginalValue = value,
            DisplayName = FormatFriendlyKeyName(key),
            Category = KspSettingCategory.Other,
            SettingType = inferredType
        };
    }

    private static string FormatFriendlyKeyName(string key)
    {
        // 把 SCREEN_RESOLUTION_WIDTH 转为友好显示
        return key.Replace('_', ' ');
    }

    // ---- 精选已知配置项与官方本地化映射表 ---------------------------------

    private sealed record SettingDescriptor(
        string? LocKey,
        string DefaultName,
        string? Description,
        KspSettingCategory Category,
        KspSettingType Type,
        IReadOnlyList<KspOptionEntry>? Options = null,
        double? MinValue = null,
        double? MaxValue = null,
        double? Step = null,
        int Priority = 0);

    private static readonly Dictionary<string, SettingDescriptor> CuratedDescriptors = new(StringComparer.Ordinal)
    {
        // ---- 图像与画质 ----
        ["FULLSCREEN"] = new(
            "#autoLOC_149901", "全屏显示", "是否以全屏独占模式运行",
            KspSettingCategory.Graphics, KspSettingType.Boolean, Priority: 100),
        ["SCREEN_RESOLUTION_WIDTH"] = new(
            "#autoLOC_149894", "屏幕宽度", "屏幕水平分辨率（像素）",
            KspSettingCategory.Graphics, KspSettingType.Integer, MinValue: 640, MaxValue: 7680, Step: 10, Priority: 99),
        ["SCREEN_RESOLUTION_HEIGHT"] = new(
            "#autoLOC_149894", "屏幕高度", "屏幕垂直分辨率（像素）",
            KspSettingCategory.Graphics, KspSettingType.Integer, MinValue: 480, MaxValue: 4320, Step: 10, Priority: 99),
        ["QUALITY_PRESET"] = new(
            "#autoLOC_149808", "渲染质量预设", "整体画质预设档位",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("1", "最快 (Fastest)"),
                new("2", "快速 (Fast)"),
                new("3", "普通 (Simple)"),
                new("4", "良好 (Good)"),
                new("5", "极佳 (Beautiful)"),
                new("6", "极致 (Fantastic)")
            ], Priority: 95),
        ["FRAMERATE_LIMIT"] = new(
            "#autoLOC_149922", "帧数限制", "游戏主循环最大帧率（-1 为不限制）",
            KspSettingCategory.Graphics, KspSettingType.Integer, MinValue: -1, MaxValue: 240, Step: 5, Priority: 92),
        ["ANTI_ALIASING"] = new(
            "#autoLOC_149908", "抗锯齿", "多重采样抗锯齿倍率 (MSAA)",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("0", "关闭 (Off)"),
                new("2", "2x MSAA"),
                new("4", "4x MSAA"),
                new("8", "8x MSAA")
            ], Priority: 90),
        ["TEXTURE_QUALITY"] = new(
            "#autoLOC_149820", "纹理质量", "纹理贴图采样缩放分辨率",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("0", "全分辨率 (Full Res)"),
                new("1", "1/2 缩放 (Half Res)"),
                new("2", "1/4 缩放 (Quarter Res)"),
                new("3", "1/8 缩放 (Eighth Res)")
            ], Priority: 88),
        ["SYNC_VBL"] = new(
            "#autoLOC_149915", "垂直同步", "垂直刷新同步模式",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("0", "关闭 (Off)"),
                new("1", "开启 (Every VBlank)"),
                new("2", "半刷新率 (Every Second VBlank)")
            ], Priority: 85),
        ["LIGHT_QUALITY"] = new(
            "#autoLOC_149929", "像素光层次", "场景中同时生效的逐像素光源数量",
            KspSettingCategory.Graphics, KspSettingType.Integer, MinValue: 0, MaxValue: 64, Step: 2),
        ["SHADOWS_QUALITY"] = new(
            "#autoLOC_149936", "阴影层次", "实时阴影映射质量与级联分级",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("0", "禁用阴影 (Disabled)"),
                new("1", "极低 (Very Low)"),
                new("2", "低 (Low)"),
                new("3", "中等 (Medium)"),
                new("4", "高 (High)")
            ]),
        ["AERO_FX_QUALITY"] = new(
            "#autoLOC_149847", "气动特效质量", "跨音速激波与再入等离子体光效质量",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("0", "关闭 (Off)"),
                new("1", "低 (Low)"),
                new("2", "中等 (Normal)"),
                new("3", "高 (High)")
            ]),
        ["CELESTIAL_BODIES_CAST_SHADOWS"] = new(
            null, "天体投射阴影", "行星与卫星是否投射大尺度天体阴影",
            KspSettingCategory.Graphics, KspSettingType.Boolean),
        ["HIGHLIGHT_FX"] = new(
            "#autoLOC_150109", "高亮特效", "光标悬停与部件选取时的发光高亮",
            KspSettingCategory.Graphics, KspSettingType.Boolean),
        ["INFLIGHT_HIGHLIGHT"] = new(
            "#autoLOC_150146", "飞行中启用部件高亮", "在飞行视图中支持部件悬停高亮效果",
            KspSettingCategory.Graphics, KspSettingType.Boolean),
        ["PART_HIGHLIGHTER_BRIGHTNESSFACTOR"] = new(
            "#autoLOC_150140", "部件高亮亮度系数", "部件鼠标交互发光的高亮倍率",
            KspSettingCategory.Graphics, KspSettingType.Float, MinValue: 0.1, MaxValue: 2.0, Step: 0.05),
        ["TERRAIN_SHADER_QUALITY"] = new(
            null, "地表着色器质量", "星球表面地形着色器精细度",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("0", "低 (Low)"),
                new("1", "中 (Medium)"),
                new("2", "高 (High)"),
                new("3", "超高 (Ultra)")
            ]),
        ["REFLECTION_PROBE_TEXTURE_RESOLUTION"] = new(
            null, "反射探针分辨率", "实时环境镜面反射贴图大小",
            KspSettingCategory.Graphics, KspSettingType.Preset,
            Options:
            [
                new("0", "128 像素"),
                new("1", "256 像素"),
                new("2", "512 像素"),
                new("3", "1024 像素")
            ]),

        // ---- 音效与音量 ----
        ["MASTER_VOLUME"] = new(
            "#autoLOC_146154", "主音量", "游戏总输出音频主音量控制",
            KspSettingCategory.Audio, KspSettingType.Float, MinValue: 0.0, MaxValue: 1.0, Step: 0.05, Priority: 100),
        ["MUSIC_VOLUME"] = new(
            "#autoLOC_146182", "音乐音量", "太空探索、建造与主菜单背景音乐",
            KspSettingCategory.Audio, KspSettingType.Float, MinValue: 0.0, MaxValue: 1.0, Step: 0.05, Priority: 95),
        ["SHIP_VOLUME"] = new(
            "#autoLOC_146161", "航天器音效", "引擎轰鸣、RCS喷气与机构作动声",
            KspSettingCategory.Audio, KspSettingType.Float, MinValue: 0.0, MaxValue: 1.0, Step: 0.05, Priority: 90),
        ["UI_VOLUME"] = new(
            "#autoLOC_146175", "界面音效", "按钮点击、弹窗提示与仪表报警音",
            KspSettingCategory.Audio, KspSettingType.Float, MinValue: 0.0, MaxValue: 1.0, Step: 0.05, Priority: 85),
        ["AMBIENCE_VOLUME"] = new(
            "#autoLOC_146168", "环境音效", "太空背景静音风声与地表自然环境音",
            KspSettingCategory.Audio, KspSettingType.Float, MinValue: 0.0, MaxValue: 1.0, Step: 0.05, Priority: 80),
        ["VOICE_VOLUME"] = new(
            "#autoLOC_146189", "语音音效", "坎巴拉人无线电通话与舱内语音",
            KspSettingCategory.Audio, KspSettingType.Float, MinValue: 0.0, MaxValue: 1.0, Step: 0.05, Priority: 75),

        // ---- 常规与游戏性 ----
        ["UI_SIZE"] = new(
            "#autoLOC_146519", "操作界面尺寸", "整体 UI 界面缩放倍率（修改后需重启生效）",
            KspSettingCategory.Gameplay, KspSettingType.Float, MinValue: 0.5, MaxValue: 2.0, Step: 0.05, Priority: 100),
        ["SIMULATE_IN_BACKGROUND"] = new(
            null, "后台模拟运行", "游戏窗口切换到后台失焦时保持运行不暂停",
            KspSettingCategory.Gameplay, KspSettingType.Boolean, Priority: 95),
        ["AUTOSAVE_INTERVAL"] = new(
            null, "自动保存间隔", "游戏定期自动持久化存档的周期（秒）",
            KspSettingCategory.Gameplay, KspSettingType.Integer, MinValue: 30, MaxValue: 3600, Step: 30, Priority: 90),
        ["APPLAUNCHER_SCALE"] = new(
            "#autoLOC_146525", "应用启动器尺寸", "右侧 Mod 与系统应用图标栏缩放",
            KspSettingCategory.Gameplay, KspSettingType.Float, MinValue: 0.5, MaxValue: 2.0, Step: 0.05, Priority: 85),
        ["NAVBALL_SCALE"] = new(
            "#autoLOC_146562", "导航球尺寸", "飞行座舱姿态仪导航球缩放大小",
            KspSettingCategory.Gameplay, KspSettingType.Float, MinValue: 0.5, MaxValue: 2.0, Step: 0.05, Priority: 80),
        ["NAVBALL_POS_X"] = new(
            "#autoLOC_146566", "导航球水平位置", "导航球屏幕底部的水平偏移量",
            KspSettingCategory.Gameplay, KspSettingType.Float, MinValue: -1.0, MaxValue: 1.0, Step: 0.05, Priority: 75),
        ["CAMERA_DOUBLECLICK_MOUSELOOK"] = new(
            "#autoLOC_146488", "双击鼠标四处查看", "双击鼠标切换自由视角环视",
            KspSettingCategory.Gameplay, KspSettingType.Boolean, Priority: 60),
        ["CAMERA_FX_EXTERNAL"] = new(
            "#autoLOC_146493", "摄像机晃动（外部视角）", "大动压与高载荷下的外部镜头震颤幅度",
            KspSettingCategory.Gameplay, KspSettingType.Float, MinValue: 0.0, MaxValue: 2.0, Step: 0.1, Priority: 50),
        ["CAMERA_FX_INTERNAL"] = new(
            "#autoLOC_146498", "摄像机晃动（内部视角）", "座舱第一人称 IVA 视角的物理震颤幅度",
            KspSettingCategory.Gameplay, KspSettingType.Float, MinValue: 0.0, MaxValue: 2.0, Step: 0.1, Priority: 50),
        ["VAB_USE_ANGLE_SNAP"] = new(
            null, "VAB 角度吸附", "装配大楼内默认启用离散角度对齐吸附",
            KspSettingCategory.Gameplay, KspSettingType.Boolean),
        ["AUTOSTRUT_SYMMETRY"] = new(
            null, "自动加固对称部件", "对称放置的部件同步应用自动加固设置",
            KspSettingCategory.Gameplay, KspSettingType.Boolean),
        ["CONFIRM_MESSAGE_DELETION"] = new(
            null, "确认删除消息", "在通知栏中清理删除告警消息时弹出确认提示",
            KspSettingCategory.Gameplay, KspSettingType.Boolean),
        ["EDITOR_UNDO_REDO_LIMIT"] = new(
            null, "编辑器撤销步数限制", "VAB/SPH 厂房内允许的最大 Undo/Redo 栈深度",
            KspSettingCategory.Gameplay, KspSettingType.Integer, MinValue: 10, MaxValue: 128, Step: 2),

        // ---- 高级与调试 ----
        ["ADVANCED_TWEAKABLES"] = new(
            "#autoLOC_146503", "高级部件调整 (Advanced Tweakables)", "在部件右键菜单中显示自动加固、阻尼刚度及进阶流道控制",
            KspSettingCategory.Advanced, KspSettingType.Boolean, Priority: 100),
        ["PHYSICS_FRAME_DT_LIMIT"] = new(
            null, "物理帧时间步长上限", "每一物理仿真步的最大模拟时间（太低易卡顿，太高物理不稳定）",
            KspSettingCategory.Advanced, KspSettingType.Float, MinValue: 0.02, MaxValue: 0.12, Step: 0.01, Priority: 90),
        ["VERBOSE_DEBUG_LOG"] = new(
            null, "输出详细调试日志", "在 KSP.log 中打印更详尽的底层运行时调试信息",
            KspSettingCategory.Advanced, KspSettingType.Boolean, Priority: 80),
        ["LOG_ERRORS_TO_SCREEN"] = new(
            null, "屏幕打印错误", "在游戏画面的左上角直接浮现错误控制台文本",
            KspSettingCategory.Advanced, KspSettingType.Boolean),
        ["LOG_EXCEPTIONS_TO_SCREEN"] = new(
            null, "屏幕打印异常", "在游戏画面上直接浮现未捕获的 C# 异常崩溃详情",
            KspSettingCategory.Advanced, KspSettingType.Boolean),
        ["SHOW_TRANSLATION_KEYS_ON_SCREEN"] = new(
            null, "屏幕显示本地化键名", "把所有 UI 字符串显示为 #autoLOC_xxxxx 用于排查翻译",
            KspSettingCategory.Advanced, KspSettingType.Boolean),
        ["PHYSICS_FRAME_DT_LIMIT"] = new(
            null, "物理帧时间步长上限", "每一物理仿真步的最大模拟时间（太低易卡顿，太高物理不稳定）",
            KspSettingCategory.Advanced, KspSettingType.Float, MinValue: 0.02, MaxValue: 0.12, Step: 0.01),
        ["MAX_VESSELS_BUDGET"] = new(
            null, "最大载具预算上限", "系统追踪站允许同时追踪的航天器与碎片总数",
            KspSettingCategory.Advanced, KspSettingType.Integer, MinValue: 100, MaxValue: 50000, Step: 500),
    };
}
