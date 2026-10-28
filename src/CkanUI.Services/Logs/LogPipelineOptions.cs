namespace CkanUI.Services.Logs;

/// <summary>
/// 日志管线参数（14 篇 §7）：日志目录、卷大小、是否落文件。
/// 目录可注入，是为了让测试写进 <c>artifacts/tmp</c> 而不是真实 <c>%LocalAppData%</c>。
/// </summary>
public sealed record LogPipelineOptions
{
    /// <summary>默认日志目录（<c>%LocalAppData%\CKAN\logs</c>）。</summary>
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKAN",
        "logs");

    /// <summary>环境变量名：覆盖日志目录（无环境变量时用默认目录）。</summary>
    public const string LogDirectoryVariable = "CKANUI_LOG_DIR";

    /// <summary>日志目录。</summary>
    public string LogDirectory { get; init; } = DefaultLogDirectory;

    /// <summary>是否写滚动文件（false 时只保留内存缓冲，供测试或只读介质场景）。</summary>
    public bool WriteFile { get; init; } = true;

    /// <summary>单文件卷大小（字节），默认 5MB。</summary>
    public long FileSizeLimitBytes { get; init; } = 5L * 1024 * 1024;

    /// <summary>卷数上限，默认 5。</summary>
    public int MaxRollBackups { get; init; } = 5;

    /// <summary>默认参数（日志目录受 <see cref="LogDirectoryVariable"/> 影响）。</summary>
    public static LogPipelineOptions Default { get; } = Resolve(Environment.GetEnvironmentVariable(LogDirectoryVariable));

    /// <summary>按可选目录覆盖构造参数（null/空白退回默认目录）。</summary>
    public static LogPipelineOptions Resolve(string? directory) =>
        new() { LogDirectory = string.IsNullOrWhiteSpace(directory) ? DefaultLogDirectory : directory };

    /// <summary>按日期算的当日文件名（<c>ckanui-YYYYMMDD.log</c>）。</summary>
    public string ResolveFileName(DateTime todayLocal) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"ckanui-{todayLocal:yyyyMMdd}.log");
}
