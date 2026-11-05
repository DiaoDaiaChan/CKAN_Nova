namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>SpaceDock 压缩包条目的安装目标根目录。</summary>
public enum SpaceDockInstallTarget
{
    /// <summary>KSP 的 GameData 目录。</summary>
    GameData,

    /// <summary>KSP 游戏根目录（Ships、Missions 等）。</summary>
    GameRoot,
}

/// <summary>SpaceDock 压缩包识别出的目录布局。</summary>
public enum SpaceDockInstallStrategy
{
    /// <summary>压缩包里有 GameData，按 GameData 下的内容安装。</summary>
    GameDataTree,

    /// <summary>没有 GameData，但只有一个顶层模组文件夹。</summary>
    SingleFolder,

    /// <summary>没有 GameData，也没有统一顶层文件夹，整体收进 GameData/模组名。</summary>
    FlatPayload,

    /// <summary>压缩包提供的是 Ships / Missions 等游戏根目录内容。</summary>
    GameRootTree,

    /// <summary>同时包含 GameData 与游戏根目录内容。</summary>
    MixedRoots,
}

/// <summary>
/// 安装前的布局确认信息：只描述压缩包会被放到哪里，不包含文件路径明细。
/// </summary>
/// <param name="Strategy">识别出的布局。</param>
/// <param name="InstalledRoot">GameData 下的安装根目录；纯游戏根目录布局为空。</param>
/// <param name="Targets">实际会写入的根目录集合。</param>
/// <param name="FileCount">计划安装的文件数。</param>
public sealed record SpaceDockInstallPlan(
    SpaceDockInstallStrategy Strategy,
    string InstalledRoot,
    IReadOnlyList<SpaceDockInstallTarget> Targets,
    int FileCount);
