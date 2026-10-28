namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 打包请求：输出路径 + 参数。
/// 输出路径由调用方（UI 保存对话框 / 测试临时目录）给出，服务不猜目录——猜目录是把测试写坏的常见方式。
/// </summary>
public sealed record LogBundleRequest
{
    /// <summary>zip 输出路径（父目录不存在时由服务创建）。</summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>参数。</summary>
    public LogBundleOptions Options { get; init; } = LogBundleOptions.Default;

    /// <summary>构造请求。</summary>
    public static LogBundleRequest For(string outputPath, int tailLines = LogBundleOptions.DefaultTailLines) => new()
    {
        OutputPath = outputPath,
        Options = LogBundleOptions.Default with { TailLines = tailLines },
    };
}
