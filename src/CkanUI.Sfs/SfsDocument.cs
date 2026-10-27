namespace CkanUI.Sfs;

/// <summary>SFS 文本模型骨架（13 篇）。解析/序列化/格式化在 E1 里程碑实现。</summary>
public sealed class SfsDocument
{
    public SfsDocument(string sourcePath, string text)
    {
        SourcePath = sourcePath;
        Text = text;
    }

    public string SourcePath { get; }

    public string Text { get; }
}
