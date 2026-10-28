namespace CkanUI.Services.Abstractions.Commands;

/// <summary>
/// 标题里的一段原文区间（用于命令面板的高亮）。
/// </summary>
/// <remarks>
/// 服务层只回字符下标，不碰任何排版类型：视图把它翻成 <c>Run</c>/加粗样式，
/// 无头测试则直接比较下标，双方都不需要引用对方的库。
/// </remarks>
/// <param name="Start">起始下标（0 基，落在标题字符串内）。</param>
/// <param name="Length">长度（至少 1）。</param>
public readonly record struct TextMatchRange(int Start, int Length)
{
    /// <summary>结束下标（不含）。</summary>
    public int End => Start + Length;
}
