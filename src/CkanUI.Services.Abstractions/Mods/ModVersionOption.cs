namespace CkanUI.Services.Abstractions.Mods;

/// <summary>
/// 一个可选安装的模组版本（F-30「全部版本」页签的数据项）。
/// </summary>
/// <remarks>
/// 纯数据、不含任何 CKAN 类型：服务层把 Core 的 <c>CkanModule</c> 投影成本记录，
/// 视图模型再转成自己的展示 DTO。所有非必填字段缺失时由展示层走「未知」文案。
/// </remarks>
/// <param name="Version">版本号原文（可能带 epoch 前缀，展示层负责美化）。</param>
/// <param name="GameCompatibility">游戏兼容范围文案（如 <c>KSP 1.12.x</c>）；无法判定时为 <c>null</c>。</param>
/// <param name="ReleaseDate">发布日期；上游没写时为 <c>null</c>。</param>
/// <param name="DownloadSizeBytes">下载体积；上游没写或为 0 时为 <c>null</c>。</param>
/// <param name="DownloadUrl">首选下载地址；没有下载列表时为 <c>null</c>。</param>
/// <param name="IsInstalled">该版本是否正是当前实例已安装的版本。</param>
/// <param name="IsCompatible">该版本是否兼容当前实例的游戏版本。</param>
/// <param name="IsUnstable">是否为非稳定版（测试版/开发版）。</param>
public sealed record ModVersionOption(
    string Version,
    string? GameCompatibility = null,
    DateTimeOffset? ReleaseDate = null,
    long? DownloadSizeBytes = null,
    string? DownloadUrl = null,
    bool IsInstalled = false,
    bool IsCompatible = true,
    bool IsUnstable = false);
