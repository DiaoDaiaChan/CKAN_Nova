using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// SpaceDock 模组的 ZIP 下载与 GameData 安装。
/// </summary>
/// <remarks>
/// 这条链路完全绕过 CKAN：不写注册表、不做依赖解析、不参与变更集。
/// 因此安装后的文件在 CKAN 眼里属于「未管理文件」，这是预期行为，页面会在确认框里明说。
/// </remarks>
public interface ISpaceDockInstaller
{
    /// <summary>下载版本 ZIP 到目标目录，返回保存后的绝对路径。</summary>
    Task<OperationResult<string>> DownloadAsync(
        SpaceDockVersionInfo version,
        string targetDirectory,
        string suggestedFileName,
        CancellationToken ct = default);

    /// <summary>
    /// 只读检查 ZIP 的目录布局，供安装确认框展示；不写磁盘、不修改压缩包。
    /// </summary>
    Task<OperationResult<SpaceDockInstallPlan>> InspectAsync(
        string archivePath,
        string modName,
        CancellationToken ct = default);

    /// <summary>
    /// 按安装适配器识别出的布局解压到游戏目录。
    /// GameData 内容进 GameData，Ships / Missions 等内容进游戏根目录。
    /// 返回实际安装的模组根目录。
    /// </summary>
    Task<OperationResult<string>> InstallAsync(
        string archivePath,
        string gameDirectory,
        string modName,
        CancellationToken ct = default);
}
