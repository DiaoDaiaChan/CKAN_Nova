namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 启动命令解析的唯一入口：实例里保存的命令优先，其次本机推断的默认值。
/// </summary>
/// <remarks>
/// 主页卡片、实例页、顶栏常驻启动按钮都要走同一条回落链——三处各抄一遍，
/// 迟早出现「主页能启动、顶栏报没有命令」的同实例不同答案。
/// </remarks>
public static class InstanceLaunchCommandResolver
{
    /// <summary>取第一条可用启动命令；没有就返回 <c>null</c>。</summary>
    /// <param name="store">实例启动命令存储；为空时直接用本机推断的默认值。</param>
    /// <param name="gameDirectory">实例的游戏目录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public static async Task<string?> ResolveAsync(
        IInstanceCommandStore? store,
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        if (store is { } commandStore)
        {
            var saved = await commandStore.ReadAsync(gameDirectory, cancellationToken).ConfigureAwait(true);
            if (saved.IsSuccess && saved.Value is { Count: > 0 } commands)
            {
                return commands[0];
            }

            return commandStore.DefaultsFor(gameDirectory).FirstOrDefault();
        }

        return LaunchCommandDefaults.ForThisMachine(gameDirectory).FirstOrDefault();
    }
}
