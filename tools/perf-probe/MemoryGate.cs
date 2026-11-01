using System;
using System.Collections.Generic;
using System.Text;

namespace CkanUI.Tools.PerfProbe;

/// <summary>
/// 内存与空转的回归门禁：把 <see cref="MemoryProbe"/> 的观测值对着固定阈值判定。
/// </summary>
/// <remarks>
/// 阈值不是「目标值」，是「明显退化就报红」的护栏：每条都留了 1.3-1.6 倍余量给机器噪声，
/// 取值与实测依据见 docs/ops/perf-baseline.md §16。这里只守已经优化到位、又被容易改坏的指标：
/// 滚动路径的每帧分配、首屏后的驻留、空转时的光栅成本、线程数。
/// </remarks>
public static class MemoryGate
{
    /// <summary>
    /// 滚动每帧分配上限：b4f0a19 起行模板被 f0d8f81 的视觉改版加重，实测 97,674 B/帧（旧模板 67,418），留 1.33 倍余量。
    /// 行模板再改重就要重新标这个阈值——它是退化护栏，不是达标线。
    /// </summary>
    public const double MaxAllocatedPerFrameBytes = 130_000;

    /// <summary>3,000 模组首屏后工作集上限：实测 161-162 MB，留 1.6 倍余量。</summary>
    public const long MaxWorkingSetInteractiveBytes = 260L * 1024 * 1024;

    /// <summary>整轮画像的峰值工作集上限：实测 172 MB。</summary>
    public const long MaxPeakWorkingSetBytes = 300L * 1024 * 1024;

    /// <summary>光栅档空转（每帧 Skia 渲染 1280×800）单核百分比上限：实测 7.0-16.3%，留余量给噪声。</summary>
    public const double MaxIdleCpuWithRasterPercent = 22.0;

    /// <summary>线程数上限：正常 8-9 个，32 核机器上线程池会按需撑到这里就算异常。</summary>
    public const int MaxThreads = 24;

    /// <summary>逐项判定，返回 (是否全过, 可打印的表格行)。</summary>
    public static (bool Passed, IReadOnlyList<string> Rows) Evaluate(MemoryProbe.Result r)
    {
        var rows = new List<string>();
        var passed = true;

        passed &= Check(rows, "滚动每帧分配", $"{r.AllocatedPerFrameBytes:F0} B/帧", $"<= {MaxAllocatedPerFrameBytes:F0} B/帧",
            r.AllocatedPerFrameBytes <= MaxAllocatedPerFrameBytes);
        passed &= Check(rows, "首屏后工作集", Mb(r.WorkingSetInteractiveBytes), $"<= {Mb(MaxWorkingSetInteractiveBytes)}",
            r.WorkingSetInteractiveBytes <= MaxWorkingSetInteractiveBytes);
        passed &= Check(rows, "峰值工作集", Mb(r.PeakWorkingSetBytes), $"<= {Mb(MaxPeakWorkingSetBytes)}",
            r.PeakWorkingSetBytes <= MaxPeakWorkingSetBytes);
        passed &= Check(rows, "空转 CPU（光栅档）", $"{r.IdleCpuWithRasterPercent:F2}% 单核", $"<= {MaxIdleCpuWithRasterPercent:F0}% 单核",
            r.IdleCpuWithRasterPercent <= MaxIdleCpuWithRasterPercent);
        passed &= Check(rows, "线程数（首屏后）", r.ThreadsInteractive.ToString(), $"<= {MaxThreads}",
            r.ThreadsInteractive <= MaxThreads);

        return (passed, rows);
    }

    /// <summary>把判定结果渲染成对齐的文本表（stdout 用）。</summary>
    public static string ToMarkdown(IReadOnlyList<string> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| 指标 | 实测 | 门禁 | 判定 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var row in rows)
        {
            sb.AppendLine(row);
        }

        return sb.ToString();
    }

    private static bool Check(List<string> rows, string name, string actual, string limit, bool ok)
    {
        rows.Add($"| {name} | {actual} | {limit} | {(ok ? "PASS" : "FAIL")} |");
        return ok;
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} MB";
}
