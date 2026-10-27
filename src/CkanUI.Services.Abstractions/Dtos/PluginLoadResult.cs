namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 加载/重载/新增插件的结构化结果（12 篇 F-17）。
/// </summary>
/// <remarks>
/// <para>与 <c>OperationResult</c> 并列而不是复用它，是因为这里必须同时带上
/// 「失败分类」与「插件快照」：失败时界面要能定位是哪一行（<see cref="Plugin"/> 可空），
/// 成功时界面要能直接选中新插件。</para>
/// <para><see cref="Message"/> 一律是中文用户可读文案；<see cref="Detail"/> 放异常文本，只进日志与折叠区。</para>
/// </remarks>
/// <param name="IsSuccess">是否成功。</param>
/// <param name="Failure">失败分类（成功时为 <see cref="PluginLoadFailure.None"/>）。</param>
/// <param name="Plugin">涉及的插件快照；加载阶段就失败（文件不存在/坏 PE）时为 <c>null</c>。</param>
/// <param name="Message">中文用户可读消息。</param>
/// <param name="Detail">技术细节（异常文本），可能为 <c>null</c>。</param>
public sealed record PluginLoadResult(
    bool IsSuccess,
    PluginLoadFailure Failure,
    PluginInfo? Plugin,
    string? Message = null,
    string? Detail = null)
{
    /// <summary>成功结果。</summary>
    public static PluginLoadResult Ok(PluginInfo plugin, string? message = null) =>
        new(true, PluginLoadFailure.None, plugin, message, null);

    /// <summary>失败结果。</summary>
    public static PluginLoadResult Fail(
        PluginLoadFailure failure,
        string message,
        string? detail = null,
        PluginInfo? plugin = null) =>
        new(false, failure, plugin, message, detail);
}
