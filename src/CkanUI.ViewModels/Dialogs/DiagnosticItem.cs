namespace CkanUI.ViewModels.Dialogs;

/// <summary>诊断对话框的一行键值（03 篇 §8 D-03 / UI-09：可复制的诊断信息）。</summary>
public sealed record DiagnosticItem(string Name, string Value)
{
    /// <summary>一行文本形式，供复制用（"名称: 值"）。</summary>
    public string DisplayText => Name + ": " + Value;
}
