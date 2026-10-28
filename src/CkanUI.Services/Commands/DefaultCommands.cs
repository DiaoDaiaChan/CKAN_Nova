using System.Diagnostics.CodeAnalysis;
using CkanUI.Services.Abstractions.Commands;

namespace CkanUI.Services.Commands;

/// <summary>
/// 内置命令表（UI-40）：19 个导航页 + 10 个界面动作，命令面板开箱即用。
/// </summary>
/// <remarks>
/// <para><b>为什么放在服务层</b>：命令表是纯数据 + 纯字符串拼接，和 App 外壳无关；
/// 放在这里能让无头测试逐条断言 Id 与导航键的映射，也能让插件复用同一套约定。</para>
/// <para><b>Id 约定</b>：导航命令用 <c>nav:&lt;页面键&gt;</c>（页面键与 <c>ShellViewModel</c> 的导航键一致），
/// 界面动作用 <c>action:&lt;动词&gt;</c>。宿主只要看前缀就知道该走 <c>INavigationService.NavigateAsync</c>
/// 还是自己的动作映射表，不需要给命令面板开一个回调注册表。</para>
/// <para><b>文案</b>：导航标题复用既有的 <c>Ui_NavRail_*</c> 键（与左侧导航栏同一份译文，避免两处不一致），
/// 其余键是本任务新增的。没注入翻译委托时用中文兜底，绝不把 <c>Ui_*</c> 键名显示给用户。</para>
/// <para><b>快捷键文本</b>：只写真正会接线的键位（Ctrl+K / Ctrl+/），后退/前进一步的键位由外壳决定后再补，
/// 免得快捷键目录里出现按了没反应的行。</para>
/// </remarks>
public static class DefaultCommands
{
    /// <summary>导航命令的 Id 前缀。</summary>
    public const string NavigationPrefix = "nav:";

    /// <summary>界面动作的 Id 前缀。</summary>
    public const string ActionPrefix = "action:";

    /// <summary>导航类命令的分组键。</summary>
    public const string CategoryNavigationKey = "Ui_Cmd_Category_Navigate";

    /// <summary>界面动作类命令的分组键。</summary>
    public const string CategoryInterfaceKey = "Ui_Cmd_Category_Interface";

    /// <summary>命令面板自身的手势（面板打开时由视图处理，写在描述里以便展示）。</summary>
    public const string PaletteShortcut = "Ctrl+K";

    /// <summary>快捷键目录的手势。</summary>
    public const string ShortcutsShortcut = "Ctrl+/";

    /// <summary>内置导航页（页面键，与 ShellViewModel 的导航键一致）。</summary>
    private static readonly NavigationEntry[] NavigationEntries =
    {
        new("mods", "Ui_NavRail_ManageMods", new[] { "模组", "mods", "mod", "管理", "已安装" }),
        new("content", "Ui_NavRail_Content", new[] { "内容", "content", "预览" }),
        new("changes", "Ui_NavRail_Changes", new[] { "变更", "changes", "变更集", "待应用" }),
        new("repos", "Ui_NavRail_Repos", new[] { "仓库", "repos", "repository", "源", "元数据" }),
        new("modpacks", "Ui_NavRail_ModPacks", new[] { "整合包", "modpack", "modpacks", "导入", "导出", "备份" }),
        new("modpack-editor", "Ui_NavRail_ModPackEditor", new[] { "编辑器", "editor", "modpack", "整合包" }),
        new("instances", "Ui_NavRail_Instances", new[] { "实例", "instance", "instances", "游戏", "ksp" }),
        new("playtime", "Ui_NavRail_PlayTime", new[] { "时长", "playtime", "时间", "游戏时长" }),
        new("saves", "Ui_NavRail_Saves", new[] { "存档", "save", "saves", "游戏存档" }),
        new("unmanaged", "Ui_NavRail_Unmanaged", new[] { "未管理", "unmanaged", "残留", "文件" }),
        new("cache", "Ui_NavRail_Cache", new[] { "缓存", "cache", "下载缓存" }),
        new("downloadstats", "Ui_NavRail_DownloadStats", new[] { "统计", "stats", "下载", "流量" }),
        new("dedup", "Ui_NavRail_Dedup", new[] { "去重", "dedup", "重复", "跨实例" }),
        new("installhistory", "Ui_NavRail_InstallHistory", new[] { "历史", "history", "安装历史" }),
        new("plugins", "Ui_NavRail_Plugins", new[] { "插件", "plugin", "plugins", "扩展" }),
        new("modtext", "Ui_NavRail_ModText", new[] { "翻译", "modtext", "本地化", "汉化" }),
        new("logs", "Ui_NavRail_Logs", new[] { "日志", "log", "logs", "排障" }),
        new("settings", "Ui_NavRail_Settings", new[] { "设置", "settings", "选项", "偏好" }),
        new("modconfig", "Ui_NavRail_ModConfig", new[] { "配置", "config", "mod 配置", "参数" }),
    };

    /// <summary>界面动作（命令面板自身的快捷键来自这里）。</summary>
    private static readonly ActionEntry[] ActionEntries =
    {
        new("show-palette", "Ui_Cmd_Action_ShowPalette", new[] { "命令", "command", "palette", "搜索", "跳转" }, PaletteShortcut, false),
        new("show-shortcuts", "Ui_Cmd_Action_ShowShortcuts", new[] { "快捷键", "shortcut", "shortcuts", "键位" }, ShortcutsShortcut, false),
        new("go-back", "Ui_Cmd_Action_GoBack", new[] { "后退", "back", "返回" }, "", false),
        new("go-forward", "Ui_Cmd_Action_GoForward", new[] { "前进", "forward" }, "", false),
        new("open-instance-folder", "Ui_Cmd_Action_OpenInstanceFolder", new[] { "打开", "目录", "folder", "实例" }, "", true),
        new("open-log-folder", "Ui_Cmd_Action_OpenLogFolder", new[] { "日志", "log", "目录", "folder" }, "", false),
        new("export-diagnostics", "Ui_Cmd_Action_ExportDiagnostics", new[] { "诊断", "diagnostic", "导出", "export", "打包" }, "", false),
        new("check-updates", "Ui_Cmd_Action_CheckUpdates", new[] { "更新", "update", "检查", "版本" }, "", false),
        new("toggle-theme", "Ui_Cmd_Action_ToggleTheme", new[] { "主题", "theme", "深色", "浅色", "外观" }, "", false),
        new("toggle-density", "Ui_Cmd_Action_ToggleDensity", new[] { "密度", "density", "紧凑", "间距", "布局" }, "", false),
    };

    /// <summary>拼一条导航命令的 Id（宿主与插件都按这个约定生成）。</summary>
    /// <param name="pageKey">导航页键（如 <c>mods</c>）。</param>
    public static string NavigationId(string pageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageKey);
        return NavigationPrefix + pageKey;
    }

    /// <summary>拼一条界面动作命令的 Id。</summary>
    /// <param name="verb">动作动词（如 <c>toggle-theme</c>）。</param>
    public static string ActionId(string verb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        return ActionPrefix + verb;
    }

    /// <summary>从命令 Id 反解导航页键；不是导航命令时返回 false。</summary>
    /// <param name="commandId">命令 Id。</param>
    /// <param name="pageKey">解出的页面键（非导航命令时为 null）。</param>
    public static bool TryGetNavigationPageKey(string? commandId, [NotNullWhen(true)] out string? pageKey)
    {
        if (commandId is not null
            && commandId.Length > NavigationPrefix.Length
            && commandId.StartsWith(NavigationPrefix, StringComparison.Ordinal))
        {
            pageKey = commandId[NavigationPrefix.Length..];
            return true;
        }

        pageKey = null;
        return false;
    }

    /// <summary>生成内置导航命令（按左侧导航栏顺序）；翻译委托为空时用中文兜底。</summary>
    /// <param name="translate">取译文委托（视图模型层不引用本地化层，见架构铁律 2）。</param>
    public static IReadOnlyList<CommandDescriptor> CreateNavigation(Func<string, string>? translate = null)
    {
        var text = Resolve(translate);
        var category = text(CategoryNavigationKey);
        var commands = new List<CommandDescriptor>(NavigationEntries.Length);

        foreach (var entry in NavigationEntries)
        {
            commands.Add(new CommandDescriptor(NavigationId(entry.PageKey), text(entry.TitleKey))
            {
                Category = category,
                Keywords = entry.Keywords,
            });
        }

        return commands;
    }

    /// <summary>生成内置界面动作命令。</summary>
    /// <param name="translate">取译文委托；为空时用中文兜底。</param>
    public static IReadOnlyList<CommandDescriptor> CreateActions(Func<string, string>? translate = null)
    {
        var text = Resolve(translate);
        var category = text(CategoryInterfaceKey);
        var commands = new List<CommandDescriptor>(ActionEntries.Length);

        foreach (var entry in ActionEntries)
        {
            commands.Add(new CommandDescriptor(ActionId(entry.Verb), text(entry.TitleKey))
            {
                Category = category,
                Keywords = entry.Keywords,
                ShortcutText = entry.Shortcut,
                RequiresSelectedInstance = entry.RequiresSelectedInstance,
            });
        }

        return commands;
    }

    /// <summary>生成全部内置命令（导航 + 界面动作），顺序即目录注册顺序。</summary>
    /// <param name="translate">取译文委托；为空时用中文兜底。</param>
    public static IReadOnlyList<CommandDescriptor> CreateAll(Func<string, string>? translate = null)
    {
        var navigation = CreateNavigation(translate);
        var actions = CreateActions(translate);
        var commands = new List<CommandDescriptor>(navigation.Count + actions.Count);
        commands.AddRange(navigation);
        commands.AddRange(actions);
        return commands;
    }

    /// <summary>没有本地化注入时用中文兜底（键名与 LocalizationKeys 保持一致）。</summary>
    private static Func<string, string> Resolve(Func<string, string>? translate) =>
        translate ?? (key => FallbackTexts.TryGetValue(key, out var text) ? text : key);

    /// <summary>导航页条目：页面键 + 标题键 + 检索用的同义词（同义词不翻译，中英混排只为好搜）。</summary>
    private sealed record NavigationEntry(string PageKey, string TitleKey, string[] Keywords);

    /// <summary>界面动作条目：动词 + 标题键 + 同义词 + 快捷键文本 + 是否要求已选中实例。</summary>
    private sealed record ActionEntry(string Verb, string TitleKey, string[] Keywords, string Shortcut, bool RequiresSelectedInstance);

    /// <summary>中文兜底文案（与 i18n 包的键一一对应）。</summary>
    private static readonly Dictionary<string, string> FallbackTexts = new(StringComparer.Ordinal)
    {
        [CategoryNavigationKey] = "导航",
        [CategoryInterfaceKey] = "界面",
        ["Ui_NavRail_ManageMods"] = "管理模组",
        ["Ui_NavRail_Content"] = "内容预览",
        ["Ui_NavRail_Changes"] = "变更集",
        ["Ui_NavRail_Repos"] = "仓库",
        ["Ui_NavRail_ModPacks"] = "导入导出",
        ["Ui_NavRail_ModPackEditor"] = "modpack 编辑器",
        ["Ui_NavRail_Instances"] = "游戏实例",
        ["Ui_NavRail_PlayTime"] = "游戏时长",
        ["Ui_NavRail_Saves"] = "存档",
        ["Ui_NavRail_Unmanaged"] = "未管理文件",
        ["Ui_NavRail_Cache"] = "下载缓存",
        ["Ui_NavRail_DownloadStats"] = "下载统计",
        ["Ui_NavRail_Dedup"] = "跨实例去重",
        ["Ui_NavRail_InstallHistory"] = "安装历史",
        ["Ui_NavRail_Plugins"] = "插件",
        ["Ui_NavRail_ModText"] = "模组翻译",
        ["Ui_NavRail_Logs"] = "日志",
        ["Ui_NavRail_Settings"] = "设置",
        ["Ui_NavRail_ModConfig"] = "Mod 配置",
        ["Ui_Cmd_Action_ShowPalette"] = "命令面板",
        ["Ui_Cmd_Action_ShowShortcuts"] = "快捷键目录",
        ["Ui_Cmd_Action_GoBack"] = "后退",
        ["Ui_Cmd_Action_GoForward"] = "前进",
        ["Ui_Cmd_Action_OpenInstanceFolder"] = "打开游戏实例目录",
        ["Ui_Cmd_Action_OpenLogFolder"] = "打开日志目录",
        ["Ui_Cmd_Action_ExportDiagnostics"] = "导出诊断包",
        ["Ui_Cmd_Action_CheckUpdates"] = "检查更新",
        ["Ui_Cmd_Action_ToggleTheme"] = "切换主题",
        ["Ui_Cmd_Action_ToggleDensity"] = "切换密度",
    };
}
