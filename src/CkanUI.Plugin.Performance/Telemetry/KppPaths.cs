namespace CkanUI.Plugin.Performance.Telemetry;

/// <summary>
/// 遥测文件桥的目录定位（契约 <c>docs/design/performance-tab.md</c> §1）。
/// </summary>
/// <remarks>
/// 目录固定为 <c>&lt;Instance&gt;/GameData/KSPPerformanceProfiler/Telemetry/</c>：KPP 与 ckan-ui 之间没有共享内存，
/// 只有这条路径是两侧唯一必须一致的约定，因此单独成类型，任何一处拼错都要在编译期可见。
/// </remarks>
public static class KppPaths
{
    /// <summary>KPP 的 GameData 目录名（与 Mod 安装名一致，不能随手改）。</summary>
    public const string ModDirectoryName = "KSPPerformanceProfiler";

    /// <summary>遥测根目录名。</summary>
    public const string TelemetryDirectoryName = "Telemetry";

    /// <summary>命令目录名（ckan-ui 写、KPP 消费）。</summary>
    public const string CommandsDirectoryName = "commands";

    /// <summary>ack 目录名（KPP 写、ckan-ui 读后删除）。</summary>
    public const string AcksDirectoryName = "acks";

    /// <summary>整快照文件名。</summary>
    public const string LatestFileName = "latest.json";

    /// <summary>桥健康度文件名。</summary>
    public const string StatusFileName = "status.json";

    /// <summary>
    /// 由实例的游戏目录推出遥测根目录。
    /// </summary>
    /// <param name="gameDir">实例的 <c>GameDir</c>（<c>GameData</c> 的父目录）。</param>
    /// <returns>绝对路径；<paramref name="gameDir"/> 为空时返回 <c>null</c>（而不是抛异常——没选实例是常态）。</returns>
    public static string? ForGameDir(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return null;
        }

        return Path.Combine(gameDir, "GameData", ModDirectoryName, TelemetryDirectoryName);
    }

    /// <summary>拼出遥测根目录下的某个文件名。</summary>
    public static string FileIn(string telemetryDir, string fileName) => Path.Combine(telemetryDir, fileName);

    /// <summary>拼出命令目录路径。</summary>
    public static string CommandsIn(string telemetryDir) => Path.Combine(telemetryDir, CommandsDirectoryName);

    /// <summary>拼出 ack 目录路径。</summary>
    public static string AcksIn(string telemetryDir) =>
        Path.Combine(telemetryDir, CommandsDirectoryName, AcksDirectoryName);
}
