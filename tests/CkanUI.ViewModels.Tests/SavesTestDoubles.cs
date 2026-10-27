using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Saves;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 存档页测试替身集合（07 篇：headless 可测）。
/// </summary>
/// <remarks>
/// 实例 / 对话框 / 错误上报的替身复用 <see cref="InstancesFakeService"/>、
/// <see cref="InstancesFakeDialogs"/>、<see cref="InstancesFakeErrorReporter"/>；
/// 这里只补存档服务、外壳启动器与 DTO 工厂。所有替身都返回已完成的任务，
/// 好让"选中即校验"的异步路径在测试里同步跑完。
/// </remarks>
internal sealed class SavesFakeSaveFileService : ISaveFileService
{
    public OperationResult<IReadOnlyList<SaveGameInfo>> ListResult { get; set; } =
        OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(Array.Empty<SaveGameInfo>());

    /// <summary>按文件路径编排的校验结果（缺省 = 干净）。</summary>
    public Dictionary<string, OperationResult<SaveValidationReport>> ValidateResults { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>FormatAsync 的返回值（null = 按 NewFile 语义造一个成功结果）。</summary>
    public OperationResult<SaveFormatOutcome>? FormatResult { get; set; }

    public List<SaveFormatRequest> FormatRequests { get; } = new();

    public List<string> ValidateRequests { get; } = new();

    /// <inheritdoc />
    public string GetSavesDirectory(string gameDir) => Path.Combine(gameDir, "saves");

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<SaveGameInfo>>> ListSavesAsync(string gameDir, CancellationToken ct = default) =>
        Task.FromResult(ListResult);

    /// <inheritdoc />
    public Task<OperationResult<SaveValidationReport>> ValidateAsync(string filePath, CancellationToken ct = default)
    {
        ValidateRequests.Add(filePath);
        return Task.FromResult(
            ValidateResults.TryGetValue(filePath, out var result)
                ? result
                : OperationResult<SaveValidationReport>.Ok(SavesFixtures.CleanReport(filePath)));
    }

    /// <inheritdoc />
    public Task<OperationResult<SaveFormatOutcome>> FormatAsync(SaveFormatRequest request, CancellationToken ct = default)
    {
        FormatRequests.Add(request);
        if (FormatResult is { } configured)
        {
            return Task.FromResult(configured);
        }

        return Task.FromResult(OperationResult<SaveFormatOutcome>.Ok(SavesFixtures.Outcome(request)));
    }
}

/// <summary>打开目录的外壳启动器替身：记录路径，返回值可编排。</summary>
internal sealed class SavesFakeShellLauncher : ISavesShellLauncher
{
    public List<string> Paths { get; } = new();

    /// <summary>返回值（null = 成功）。</summary>
    public OperationResult? Result { get; set; }

    /// <inheritdoc />
    public Task<OperationResult> OpenFolderAsync(string path)
    {
        Paths.Add(path);
        return Task.FromResult(Result ?? OperationResult.Ok());
    }
}

/// <summary>存档页测试的 DTO 工厂。</summary>
internal static class SavesFixtures
{
    /// <summary>造一个实例。</summary>
    public static InstanceInfo Instance(string name = "主力", string gameDir = @"C:\Games\KSP") =>
        new(Name: name, GameDir: gameDir, GameVersion: "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 12);

    /// <summary>造一行存档（默认主存档、非备份）。</summary>
    public static SaveGameInfo Save(
        string slot = "persistent",
        bool isBackup = false,
        string title = "远航号",
        string mode = "CAREER",
        string? ut = "5005129.6190400701",
        int? vessels = 3,
        string? funds = "98765.43",
        int snapshots = 2)
    {
        var name = isBackup ? slot + ".sfs.bak" : slot + ".sfs";
        return new SaveGameInfo(
            SlotName: slot,
            FilePath: Path.Combine(@"C:\Games\KSP\saves", name),
            SizeBytes: 4096,
            LastWriteTime: new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero),
            Title: title,
            Mode: mode,
            GameVersion: "1.12.5",
            Scene: "space",
            UniversalTime: ut,
            VesselCount: vessels,
            Funds: funds,
            SnapshotCount: snapshots,
            IsBackup: isBackup,
            HasLoadMeta: true);
    }

    /// <summary>干净报告（0 条诊断）。</summary>
    public static SaveValidationReport CleanReport(string filePath) =>
        Report(filePath, Array.Empty<SaveDiagnosticInfo>());

    /// <summary>一条错误 + 一条警告的报告。</summary>
    public static SaveValidationReport ProblemReport(string filePath) =>
        Report(
            filePath,
            new[]
            {
                new SaveDiagnosticInfo(
                    SaveDiagnosticSeverity.Error,
                    "V-01",
                    12,
                    3,
                    "大括号不配对：文件结束时仍有 1 个未闭合的节点。",
                    "\t{"),
                new SaveDiagnosticInfo(
                    SaveDiagnosticSeverity.Warning,
                    "V-06",
                    7,
                    1,
                    "缩进与深度不一致。",
                    null),
            });

    /// <summary>按请求造一个成功的格式化结果（NewFile 写出副本）。</summary>
    public static SaveFormatOutcome Outcome(SaveFormatRequest request) =>
        new(
            SourcePath: request.FilePath,
            Mode: request.Mode,
            Refused: false,
            RefusalReason: null,
            WroteFile: true,
            WrittenPath: Path.Combine(
                Path.GetDirectoryName(request.FilePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(request.FilePath) + ".formatted.sfs"),
            BackupPath: null,
            Changed: true,
            SemanticsPreserved: true,
            BytesBefore: 100,
            BytesAfter: 120,
            Stats: new SaveFormatStats(3, 1, 1, 0),
            Preview: null);

    /// <summary>被拒绝的格式化结果（原文件未改动）。</summary>
    public static SaveFormatOutcome RefusedOutcome(SaveFormatRequest request) =>
        new(
            SourcePath: request.FilePath,
            Mode: request.Mode,
            Refused: true,
            RefusalReason: "存档有解析错误（结构不完整），已拒绝格式化，原文件未改动。",
            WroteFile: false,
            WrittenPath: null,
            BackupPath: null,
            Changed: false,
            SemanticsPreserved: false,
            BytesBefore: 100,
            BytesAfter: 100,
            Stats: new SaveFormatStats(0, 0, 0, 0),
            Preview: "共 1 条诊断，首条：V-01 错误 第 12 行第 3 列: …");

    private static SaveValidationReport Report(string filePath, IReadOnlyList<SaveDiagnosticInfo> diagnostics) =>
        new(
            FilePath: filePath,
            SizeBytes: 4096,
            LastWriteTime: new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero),
            HasBom: false,
            DominantNewLine: "\r\n",
            LineCount: 14,
            NodeCount: 2,
            PairCount: 9,
            MaxDepth: 2,
            IsTextLossy: false,
            Diagnostics: diagnostics);
}
