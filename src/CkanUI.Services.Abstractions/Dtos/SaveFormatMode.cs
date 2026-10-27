namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>格式化落盘方式（13 篇 §7.4 / 03 篇 §14.4：不做静默格式化）。</summary>
public enum SaveFormatMode
{
    /// <summary>只计算不写盘（check / 预览语义）。</summary>
    Preview = 0,

    /// <summary>写新文件 <c>&lt;name&gt;.formatted.sfs</c>，原文件不动（默认）。</summary>
    NewFile = 1,

    /// <summary>先写 <c>&lt;name&gt;.pre-edit-&lt;时间戳&gt;.sfs</c> 快照，再原子覆盖原文件。</summary>
    Overwrite = 2,
}
