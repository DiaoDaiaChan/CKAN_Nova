namespace CkanUI.Services.Abstractions.KspConfig;

/// <summary>
/// KSP settings.cfg 完整文档模型，保全文件原始排版与所有配置项。
/// </summary>
public sealed record KspConfigDocument
{
    /// <summary>settings.cfg 的物理完整路径。</summary>
    public required string FilePath { get; init; }

    /// <summary>所有解析出的键值配置项。</summary>
    public required IReadOnlyList<KspSettingItem> Settings { get; init; }

    /// <summary>原始文本行（保全行尾、注释和嵌套模块，写盘时精准替换）。</summary>
    public required IReadOnlyList<string> RawLines { get; init; }

    /// <summary>从 Squad 官方 dictionary.cfg 提取到的本地化映射字典（#autoLOC_xxxx -> 译文）。</summary>
    public required IReadOnlyDictionary<string, string> LocalizationTable { get; init; }

    /// <summary>当前提取的本地化语言标识（如 zh-cn）。</summary>
    public string Language { get; init; } = "zh-cn";

    /// <summary>是否成功加载了 Squad 官方 dictionary.cfg 字典。</summary>
    public bool HasOfficialTranslations => LocalizationTable.Count > 0;

    /// <summary>配置项总数。</summary>
    public int TotalSettingCount => Settings.Count;

    /// <summary>是否存在备份文件（settings.cfg.bak）。</summary>
    public bool HasBackup { get; init; }
}
