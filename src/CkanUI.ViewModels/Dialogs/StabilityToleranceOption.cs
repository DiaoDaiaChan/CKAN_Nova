namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 单模组稳定性容忍度覆盖选项（F-13 / UI-07 / UI-29）。
/// </summary>
/// <param name="Value">容差取值（null 表示继承全局默认，或 "stable" / "testing" / "development"）。</param>
/// <param name="Label">界面显示标签文案。</param>
public sealed record StabilityToleranceOption(string? Value, string Label)
{
    /// <summary>向后兼容别名：等同于 <see cref="Value"/>。</summary>
    public string? Tolerance => Value;
}
