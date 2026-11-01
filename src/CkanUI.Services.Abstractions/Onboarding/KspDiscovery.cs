namespace CkanUI.Services.Abstractions;

/// <summary>KSP 安装目录的候选来源（首次启动指引的「自动扫描」结果）。</summary>
/// <remarks>
/// 来源不是装饰：界面要按来源分组显示（Steam 库 / 注册表 / 常见路径 / 手动选择），
/// 用户看到「Steam 库里的」比看到一串盘符更愿意点下去。
/// </remarks>
public enum KspCandidateSource
{
    /// <summary>Steam 库（libraryfolders.vdf 解析出的 steamapps/common）。</summary>
    Steam,

    /// <summary>系统注册表（Steam App 220200 / GOG 安装记录）。</summary>
    Registry,

    /// <summary>常见安装位置（Program Files、各盘根目录、Games 子目录、SteamLibrary 子目录）。</summary>
    CommonPath,

    /// <summary>用户手动选择。</summary>
    Manual,
}

/// <summary>扫描到的一个 KSP 安装目录。</summary>
/// <param name="GameDir">游戏根目录（含 GameData 与 KSP 可执行文件）。</param>
/// <param name="GameVersion">从 readme.txt / buildID.txt 读到的版本；读不到为 <c>null</c>。</param>
/// <param name="Source">候选来源。</param>
public sealed record KspCandidate(string GameDir, string? GameVersion, KspCandidateSource Source)
{
    /// <summary>该目录是否已能确认版本（界面据此决定「版本待确认」提示）。</summary>
    public bool HasVersion => !string.IsNullOrWhiteSpace(GameVersion);
}

/// <summary>
/// KSP 安装目录扫描（首次启动指引第 2 步）。只做只读探测：找到即返回，绝不写盘。
/// </summary>
public interface IKspInstallScanner
{
    /// <summary>按 Steam 库 → 注册表 → 常见路径的顺序扫描，去重后返回。</summary>
    Task<OperationResult<IReadOnlyList<KspCandidate>>> ScanAsync(CancellationToken ct = default);

    /// <summary>目录是否是 KSP 安装根（可执行文件 + GameData 都在）。</summary>
    bool IsKspDirectory(string path);

    /// <summary>读游戏版本（readme.txt / buildID.txt）；读不到返回 <c>null</c>。</summary>
    string? ReadGameVersion(string gameDir);
}
