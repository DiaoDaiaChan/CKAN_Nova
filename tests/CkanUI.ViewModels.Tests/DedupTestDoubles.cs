using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Tests;

/// <summary>跨实例去重页测试替身：记录扫描/执行请求，可注入返回结果。</summary>
internal sealed class DedupFakeService : IInstanceDedupService
{
    public List<DedupScanRequest> Scans { get; } = new();

    public List<DedupExecuteRequest> Executes { get; } = new();

    public OperationResult<DedupScanReport>? ScanOverride { get; set; }

    public OperationResult<DedupExecuteReport>? ExecuteOverride { get; set; }

    public Task<OperationResult<DedupScanReport>> ScanAsync(DedupScanRequest request, CancellationToken ct = default)
    {
        Scans.Add(request);
        return Task.FromResult(
            ScanOverride ?? OperationResult<DedupScanReport>.Ok(DedupScanReport.Empty(DedupPlatformSupport.Supported)));
    }

    public Task<OperationResult<DedupExecuteReport>> ExecuteAsync(DedupExecuteRequest request, CancellationToken ct = default)
    {
        Executes.Add(request);
        return Task.FromResult(
            ExecuteOverride ?? OperationResult<DedupExecuteReport>.Ok(
                DedupExecuteReport.Empty(request.DryRun, DedupPlatformSupport.Supported)));
    }

    /// <summary>造一份「一组两份重复内容」的扫描报告，供页面断言表格口径。</summary>
    public static DedupScanReport DuplicateReport(DedupPlatformSupport support = DedupPlatformSupport.Supported)
    {
        var files = new[]
        {
            new DedupCandidateFile("甲", "GameData/a.cfg", "/x/甲/GameData/a.cfg", 4096, "C:", false),
            new DedupCandidateFile("乙", "GameData/a.cfg", "/x/乙/GameData/a.cfg", 4096, "C:", false),
        };
        var group = new DedupGroup("hash-1", 4096, "C:", files) { DistinctFileCount = 2 };
        return new DedupScanReport(
            new[] { "乙", "甲" },
            new[] { group },
            Array.Empty<DedupSkipInfo>(),
            12,
            2,
            1,
            1,
            0,
            0,
            2,
            support);
    }
}
