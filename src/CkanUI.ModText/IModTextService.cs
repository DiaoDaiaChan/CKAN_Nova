using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.ModText;

/// <summary>
/// 翻译文件与条目项模型。
/// </summary>
public sealed record ModTextEntryItem(
    ulong Hash,
    string? Normalized,
    string Source,
    string Translation,
    string? Context,
    string? Mod,
    string? Src,
    bool IsKeep);

/// <summary>
/// 翻译包文件摘要。
/// </summary>
public sealed record ModPackFileInfo(
    string RelativePath,
    string FullPath,
    long FileSizeBytes,
    int EntryCount,
    DateTime LastModifiedUtc);

/// <summary>
/// Mod 元数据本地化管理服务接口（Plan 17 扩展）。
/// 管理语言包加载、持久化、条目增删改查、人工覆盖 (overrides.jsonl) 与导入导出。
/// </summary>
public interface IModTextService
{
    /// <summary>当前就绪的纯内存查找器。</summary>
    IModTextProvider Provider { get; }

    /// <summary>当前语言（如 "zh-cn"）。</summary>
    string CurrentLanguage { get; }

    /// <summary>当前已载入的内存总条目数。</summary>
    int TotalEntries { get; }

    /// <summary>基础存储根目录（%LocalAppData%/CKAN/ckan-ui/i18n）。</summary>
    string BaseDirectory { get; }

    /// <summary>语言包更新通知事件。</summary>
    event Action? PackChanged;

    /// <summary>加载或重载指定语言的语言包。</summary>
    Task ReloadPacksAsync(string? language = null, CancellationToken ct = default);

    /// <summary>获取可用的语言代码列表（根据 packs/ 下目录扫描）。</summary>
    IReadOnlyList<string> GetAvailableLanguages();

    /// <summary>列出当前语言下的所有语言包文件（含 packs/<lang>/*.jsonl 与 overrides.jsonl）。</summary>
    IReadOnlyList<ModPackFileInfo> GetPackFiles(string? language = null);

    /// <summary>获取当前已载入的所有词条条目（支持过滤）。</summary>
    IReadOnlyList<ModTextEntryItem> GetAllEntries(string? filter = null);

    /// <summary>保存或更新单条人工覆盖翻译（写入 packs/overrides.jsonl 并立即热更新内存）。</summary>
    Task PutOverrideAsync(string identifier, string field, string sourceText, string translation, CancellationToken ct = default);

    /// <summary>删除单条人工覆盖翻译（从 packs/overrides.jsonl 中移除并热更新内存）。</summary>
    Task DeleteOverrideAsync(string identifier, string field, string sourceText, CancellationToken ct = default);

    /// <summary>导入 JSONL 格式翻译文件到指定语言的包文件（如 "CKANMeta.jsonl"）。</summary>
    Task ImportJsonlAsync(string targetFileName, string content, string? language = null, CancellationToken ct = default);

    /// <summary>导出指定文件内容为 JSONL 文本。</summary>
    Task<string> ExportJsonlAsync(string relativePath, CancellationToken ct = default);

    /// <summary>从 UniversalI18n 仓库或 GameData 目录同步词库文件。返回同步的文件数量。</summary>
    Task<int> SyncFromUniversalI18nAsync(string? sourceDir = null, CancellationToken ct = default);
}
