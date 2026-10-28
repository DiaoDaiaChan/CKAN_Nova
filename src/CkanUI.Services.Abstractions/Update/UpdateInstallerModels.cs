using System;
using System.Collections.Generic;

namespace CkanUI.Services.Abstractions.Update;

/// <summary>
/// 自动更新安装的阶段（12 篇：下载 → 校验 → 解包 → 备份 → 替换）。
/// </summary>
public enum UpdateInstallStage
{
    /// <summary>尚未开始。</summary>
    Idle = 0,

    /// <summary>正在下载更新包。</summary>
    Downloading = 1,

    /// <summary>正在校验更新包（大小 / SHA256）。</summary>
    Verifying = 2,

    /// <summary>正在解包到暂存目录。</summary>
    Extracting = 3,

    /// <summary>正在备份将被覆盖的旧文件。</summary>
    BackingUp = 4,

    /// <summary>正在原地替换文件。</summary>
    Replacing = 5,

    /// <summary>已就位，等待重启。</summary>
    Completed = 6,

    /// <summary>失败（已回滚或无需回滚）。</summary>
    Failed = 7,

    /// <summary>被调用方取消。</summary>
    Canceled = 8,
}

/// <summary>
/// 安装进度快照。<see cref="Percent"/> 为 0-100，未知长度时由阶段权重估算。
/// </summary>
/// <param name="Stage">当前阶段。</param>
/// <param name="Percent">整体百分比（0-100）。</param>
/// <param name="Message">给界面直接显示的中文进度文案。</param>
/// <param name="BytesReceived">已接收字节数（下载阶段有效）。</param>
/// <param name="TotalBytes">总字节数（未知时为 0）。</param>
public sealed record UpdateInstallProgress(
    UpdateInstallStage Stage,
    int Percent,
    string Message,
    long BytesReceived = 0,
    long TotalBytes = 0)
{
    /// <summary>按阶段构造一个不带字节计数的进度。</summary>
    /// <param name="stage">阶段。</param>
    /// <param name="message">中文文案。</param>
    /// <param name="percent">百分比，越界时自动收敛到 0-100。</param>
    public static UpdateInstallProgress For(UpdateInstallStage stage, string message, int percent = 0) =>
        new(stage, Math.Clamp(percent, 0, 100), message);

    /// <summary>阶段的中文名称，用于状态栏兜底显示。</summary>
    public string StageText => Stage switch
    {
        UpdateInstallStage.Idle => "未开始",
        UpdateInstallStage.Downloading => "下载更新包",
        UpdateInstallStage.Verifying => "校验更新包",
        UpdateInstallStage.Extracting => "解包更新包",
        UpdateInstallStage.BackingUp => "备份旧文件",
        UpdateInstallStage.Replacing => "替换文件",
        UpdateInstallStage.Completed => "更新已就位",
        UpdateInstallStage.Failed => "失败",
        UpdateInstallStage.Canceled => "已取消",
        _ => "未知阶段",
    };
}

/// <summary>
/// 一次自动更新安装的输入（由检查更新结果转换而来）。
/// </summary>
/// <param name="DownloadUrl">更新包下载地址（GitHub release 资产）。</param>
/// <param name="ExpectedSha256">期望的 SHA256；为空表示上游未提供校验值（此时只做大小校验并留痕）。</param>
/// <param name="ExpectedSize">期望字节数；0 表示未知。</param>
/// <param name="Version">目标版本号，用于文案。</param>
/// <param name="DisplayName">显示名（默认取 URL 末段）。</param>
public sealed record UpdateInstallPlan(
    Uri DownloadUrl,
    string? ExpectedSha256 = null,
    long ExpectedSize = 0,
    string? Version = null,
    string? DisplayName = null)
{
    /// <summary>URL 末段不可用时的兜底文件名。</summary>
    private const string DefaultFileName = "ckan-update.zip";

    /// <summary>用于界面显示的名称。</summary>
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName!;

    /// <summary>落盘文件名：优先取 URL 末段，缺失时退化为 ckan-update.zip。</summary>
    public string FileName
    {
        get
        {
            var segment = DownloadUrl is null
                ? string.Empty
                : Uri.UnescapeDataString(DownloadUrl.Segments[DownloadUrl.Segments.Length - 1])
                    .Trim()
                    .Trim('/', '\\');
            if (string.IsNullOrWhiteSpace(segment))
            {
                return DefaultFileName;
            }

            var name = segment;
            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            if (string.IsNullOrWhiteSpace(name.Trim('.', '_')))
            {
                return DefaultFileName;
            }

            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : name + ".zip";
        }
    }

    /// <summary>是否携带上游校验值。</summary>
    public bool HasChecksum => !string.IsNullOrWhiteSpace(ExpectedSha256);
}

/// <summary>
/// 安装结果。失败时 <see cref="Message"/> 是给用户看的中文原因，<see cref="Detail"/> 是给日志的技术细节。
/// </summary>
/// <param name="Stage">结束阶段。</param>
/// <param name="RestartRequired">是否需要重启程序才能生效。</param>
/// <param name="RolledBack">失败后是否已回滚到旧文件。</param>
/// <param name="Message">中文结论。</param>
/// <param name="Detail">技术细节（可为空）。</param>
/// <param name="DownloadPath">更新包落盘路径。</param>
/// <param name="ChecksumStatus">校验结论（已校验 / 上游未提供校验值 …）。</param>
/// <param name="StagingDirectory">解包暂存目录。</param>
/// <param name="BackupDirectory">旧文件备份目录。</param>
/// <param name="ProgramDirectory">被替换的程序目录。</param>
/// <param name="ReplacedFiles">已替换的相对路径。</param>
/// <param name="RestoredFiles">回滚时已还原的相对路径。</param>
/// <param name="CreatedFiles">本次新建的相对路径（回滚时会删除）。</param>
public sealed record UpdateInstallResult(
    UpdateInstallStage Stage,
    bool RestartRequired,
    bool RolledBack,
    string Message,
    string? Detail = null,
    string? DownloadPath = null,
    string? ChecksumStatus = null,
    string? StagingDirectory = null,
    string? BackupDirectory = null,
    string? ProgramDirectory = null,
    IReadOnlyList<string>? ReplacedFiles = null,
    IReadOnlyList<string>? RestoredFiles = null,
    IReadOnlyList<string>? CreatedFiles = null)
{
    /// <summary>是否安装成功（已就位）。</summary>
    public bool IsInstalled => Stage == UpdateInstallStage.Completed;

    /// <summary>已替换文件清单（保证非空）。</summary>
    public IReadOnlyList<string> Replaced => ReplacedFiles ?? Array.Empty<string>();

    /// <summary>已还原文件清单（保证非空）。</summary>
    public IReadOnlyList<string> Restored => RestoredFiles ?? Array.Empty<string>();

    /// <summary>新建文件清单（保证非空）。</summary>
    public IReadOnlyList<string> Created => CreatedFiles ?? Array.Empty<string>();
}
