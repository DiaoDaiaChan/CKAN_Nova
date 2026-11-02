using System.Collections.Generic;

namespace CkanUI.Tools.PerfProbe;

/// <summary>
/// 页面结构膨胀门禁：换页/首屏成本与可视树节点数成正比（实测 ≈0.33 ms/节点，
/// 与 <see cref="BenchmarkRunner.RunLeafCostMicroBenchmark"/> 的单控件基线一致），
/// 所以「每次换页多花几百毫秒」通常等于「某个页面的可视树悄悄变胖了」。
/// 这里把每个导航页的节点数钉在实测值 + 约 25% 余量，超了就返回非零退出码。
/// 依据与实测表见 docs/ops/perf-baseline.md §21。
/// </summary>
public static class PageNodeGate
{
    /// <summary>实测节点数与上限（上限 ≈ 实测 × 1.25，settings 单列说明）。</summary>
    private static readonly Dictionary<string, int> Caps = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = 210,
        ["mods"] = 190,
        ["changes"] = 120,
        ["content"] = 110,
        ["repos"] = 165,
        ["modpacks"] = 255,
        ["modpack-editor"] = 575,
        ["instances"] = 320,
        ["saves"] = 150,
        ["ships"] = 130,
        ["flags"] = 110,
        ["loadingimages"] = 105,
        ["unmanaged"] = 80,
        ["cache"] = 420,
        ["downloadstats"] = 105,
        ["playtime"] = 70,
        ["dedup"] = 195,
        ["installhistory"] = 115,
        ["plugins"] = 130,
        ["logs"] = 160,
        ["modtext"] = 420,
        ["modconfig"] = 185,
        // KSP 配置页列表改成虚拟化的 ListBox 后，实体化容器只在可见区（20 行左右）；
        // 改前未虚拟化，一次铺满 400 行 ≈14.6k 节点（见 §46）。
        ["kspconfig"] = 800,
        // 设置页是全场唯一的 O(2000) 页面：36 行 × 65 节点/行，换页成本 ≈0.9 s。
        // 三条虚拟化路线都会改视觉或改滚动行为（见 §20），所以这里只防它继续变胖。
        ["settings"] = 2500,
    };

    /// <summary>未知页面按 300 封顶，避免新页面悄悄带着上千节点进来。</summary>
    public const int DefaultCap = 300;

    /// <summary>取某个页面的节点数上限。</summary>
    public static int CapFor(string key) => Caps.TryGetValue(key, out var cap) ? cap : DefaultCap;
}
