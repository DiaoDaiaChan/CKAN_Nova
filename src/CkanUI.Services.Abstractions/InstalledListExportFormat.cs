namespace CkanUI.Services.Abstractions;

/// <summary>
/// 已安装模组列表导出格式（F-45 / UI-21）。
/// </summary>
public enum InstalledListExportFormat
{
    /// <summary>纯文本列表（每行一个模组名称与版本号）。</summary>
    PlainText,

    /// <summary>Markdown 表格报告（Mod 名称、标识符、已装版本、作者、主页/下载链接与描述）。</summary>
    Markdown,

    /// <summary>BBCode 代码（适用于 KSP 官方论坛或 Reddit 的 [table] 格式）。</summary>
    BbCode,

    /// <summary>逗号分隔值（CSV）。</summary>
    Csv,

    /// <summary>制表符分隔值（TSV，供 Excel 快速粘贴）。</summary>
    Tsv
}
