namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 打包结果：zip 路径、大小、实际行数、条目清单。
/// 条目清单要回给 UI 展示「包里有什么」——用户点「导出诊断包」后最常问的就是这个。
/// </summary>
public sealed record LogBundleResult
{
    /// <summary>zip 绝对路径。</summary>
    public string ZipPath { get; init; } = string.Empty;

    /// <summary>zip 字节数。</summary>
    public long ByteLength { get; init; }

    /// <summary>实际写入 log.tail.txt 的行数。</summary>
    public int TailLineCount { get; init; }

    /// <summary>写入 log.errors.txt 的行数。</summary>
    public int ErrorLineCount { get; init; }

    /// <summary>zip 内的条目名（不含逻辑路径）。</summary>
    public IReadOnlyList<string> Entries { get; init; } = Array.Empty<string>();

    /// <summary>zip 的 SHA-256（用户回报问题时可核对上传的文件是否完整）。</summary>
    public string? Sha256 { get; init; }
}
