namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 实例启动命令列表的持久化契约（F-06）。
/// </summary>
/// <remarks>
/// 上游把启动命令存在**每个实例**的 <c>&lt;GameDir&gt;/CKAN/GUIConfig.json</c> 里
/// （<c>external/CKAN/GUI/Model/GUIConfiguration.cs</c> 的 <c>List&lt;string&gt; CommandLines</c>），
/// 不是全局设置，因此 <c>ISettingsService</c>（Core config.json）与 <c>IUiSettingsStore</c> 都不合适；
/// 这里按实例级文件落盘，与 <c>StabilityToleranceFile</c> 同一套做法。
/// </remarks>
public interface IInstanceCommandStore
{
    /// <summary>某个游戏目录的默认启动命令（列表为空或未保存过时使用）。</summary>
    /// <param name="gameDirectory">实例的游戏目录。</param>
    IReadOnlyList<string> DefaultsFor(string gameDirectory);

    /// <summary>
    /// 读取已保存的启动命令；<c>Value</c> 为 null 表示该实例还没有保存过（调用方应使用默认值）。
    /// </summary>
    /// <param name="gameDirectory">实例的游戏目录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<CkanUI.Services.Abstractions.OperationResult<IReadOnlyList<string>?>> ReadAsync(
        string gameDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>写入启动命令列表（空列表表示「全部删除」，读取时回落到默认值）。</summary>
    /// <param name="gameDirectory">实例的游戏目录。</param>
    /// <param name="commands">要保存的命令列表（按顺序）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<CkanUI.Services.Abstractions.OperationResult> WriteAsync(
        string gameDirectory,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken = default);
}
