namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 实例的默认启动命令推导（F-06「重置为默认」，对齐 Core 的 <c>Game.DefaultCommandLines</c>）。
/// </summary>
/// <remarks>
/// <para>上游语义（<c>external/CKAN/Core/Games/KerbalSpaceProgram.cs:142</c>）：Windows 下默认值是
/// <c>"{anchorFile} -single-instance"</c>，anchorFile 取 <c>InstanceAnchorFiles</c> 里第一个真实存在的文件
/// （都不存在时退回第一个候选），随后把 <c>steamLib.GameAppURLs(path)</c> 的 Steam 链接一并并进去；
/// Mac 用 <c>./KSP.app/Contents/MacOS/KSP</c>。</para>
/// <para>文件存在性由调用方以委托注入（VM 层不直接 <c>File.Exists</c> 上游目录判断，
/// 也方便单测用假探测跑遍所有分支）。纯函数、无 IO。</para>
/// </remarks>
public static class LaunchCommandDefaults
{
    /// <summary>KSP 1 的 Steam AppID。</summary>
    public const string Ksp1AppId = "220200";

    /// <summary>KSP 2 的 Steam AppID。</summary>
    public const string Ksp2AppId = "1740390";

    /// <summary>Windows 的候选锚点文件（Core 的 <c>InstanceAnchorFiles</c>，含 KSP2 变体）。</summary>
    public static readonly IReadOnlyList<string> WindowsAnchors = new[]
    {
        "KSP_x64.exe", "KSP.exe", "KSP2_x64.exe", "KSP2.exe",
    };

    /// <summary>Unix 的候选锚点文件（Core 的 <c>InstanceAnchorFiles</c>，含 KSP2 变体）。</summary>
    public static readonly IReadOnlyList<string> UnixAnchors = new[]
    {
        "KSP.x86_64", "KSP.x86", "KSP_x64.exe", "KSP.exe", "KSP2.x86_64", "KSP2_x64.exe",
    };

    /// <summary>Mac 的候选锚点文件。</summary>
    public static readonly IReadOnlyList<string> MacAnchors = new[] { "buildID64.txt", "buildID.txt" };

    /// <summary>Mac 的固定启动命令（Core 里是写死的相对路径）。</summary>
    public const string MacCommand = "./KSP.app/Contents/MacOS/KSP";

    /// <summary>向上查找 Steam 库根时最多回溯的层数（<c>&lt;库&gt;/steamapps/common/&lt;游戏&gt;</c> 只需 3 层）。</summary>
    public const int MaxSteamAncestorDepth = 8;

    /// <summary>
    /// 推导某个游戏目录的默认启动命令列表：可执行文件命令在前，Steam 链接在后（与 Core 的拼接顺序一致）。
    /// </summary>
    /// <param name="gameDirectory">实例的游戏目录。</param>
    /// <param name="isWindows">是否 Windows。</param>
    /// <param name="isMac">是否 macOS。</param>
    /// <param name="fileExists">文件存在性探测（路径 → 是否存在）。</param>
    public static IReadOnlyList<string> For(
        string gameDirectory,
        bool isWindows,
        bool isMac,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return result;
        }

        if (isMac)
        {
            result.Add(MacCommand);
        }
        else
        {
            var anchors = isWindows ? WindowsAnchors : UnixAnchors;
            var found = new List<string>();
            foreach (var anchor in anchors)
            {
                if (SafeExists(fileExists, Path.Combine(gameDirectory, anchor)))
                {
                    found.Add(anchor);
                }
            }

            // 一个锚点都不存在（目录被移动 / 还没装好）时退回第一个候选，保证列表永远非空。
            if (found.Count == 0)
            {
                found.Add(anchors[0]);
            }

            foreach (var anchor in found)
            {
                result.Add($"{anchor} {LaunchCommandLine.SingleInstanceArgument}");
            }
        }

        if (SteamCommandFor(gameDirectory, fileExists) is { } steamCommand)
        {
            result.Add(steamCommand);
        }

        return LaunchCommandLine.NormalizeList(result);
    }

    /// <summary>
    /// 判断游戏目录是否落在某个 Steam 库里，返回对应的 <c>steam://rungameid/…</c> 链接；否则 null。
    /// </summary>
    /// <remarks>
    /// 只做「向上找 <c>steamapps/appmanifest_&lt;appid&gt;.acf</c>」这一件事：不需要解析 VDF，
    /// 也不会误判非 Steam 安装（没有 manifest 就没有链接）。
    /// </remarks>
    public static string? SteamCommandFor(string gameDirectory, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        var directory = gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var depth = 0; depth < MaxSteamAncestorDepth && !string.IsNullOrEmpty(directory); depth++)
        {
            foreach (var appId in new[] { Ksp1AppId, Ksp2AppId })
            {
                var manifest = Path.Combine(directory, "steamapps", $"appmanifest_{appId}.acf");
                if (SafeExists(fileExists, manifest))
                {
                    return $"{LaunchCommandLine.SteamPrefix}rungameid/{appId}";
                }
            }

            directory = Path.GetDirectoryName(directory) ?? string.Empty;
        }

        return null;
    }

    /// <summary>当前平台的默认启动命令（宿主用真实文件系统探测）。</summary>
    public static IReadOnlyList<string> ForThisMachine(string gameDirectory) =>
        For(gameDirectory, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), File.Exists);

    private static bool SafeExists(Func<string, bool> fileExists, string path)
    {
        try
        {
            return fileExists(path);
        }
        catch (Exception)
        {
            // 探测失败按「不存在」处理：只影响默认列表，不影响用户手写的命令。
            return false;
        }
    }
}
