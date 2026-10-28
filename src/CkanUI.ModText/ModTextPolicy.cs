namespace CkanUI.ModText;

/// <summary>
/// 元数据译文使用策略（17 篇 MTI-08 的三档开关）。
/// </summary>
/// <remarks>
/// 为什么要分档而不是"全用"：机翻覆盖率高但错译是玩家能一眼看见的地方，
/// 所以默认只展示已有的人工译文；想提高覆盖率的人自己往上抬一档，责任边界清楚。
/// </remarks>
public enum ModTextPolicy
{
    /// <summary>关闭：任何元数据译文都不用，界面一律英文原文。</summary>
    Off = 0,

    /// <summary>仅已有译文（默认）：只用人工译文，机翻条目不参与查找。</summary>
    ExistingOnly = 1,

    /// <summary>允许机翻：人工优先，人工缺失时回退到机翻译文。</summary>
    AllowMachine = 2,
}
