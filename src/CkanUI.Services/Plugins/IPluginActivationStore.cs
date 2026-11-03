using System.Text.Json.Serialization;

namespace CkanUI.Services.Plugins;

/// <summary>
/// 插件激活状态的窄持久化接口（12 篇 F-17）。
/// </summary>
/// <remarks>
/// <para>抽成接口的唯一目的是可替换与可测：<see cref="PluginService"/> 只关心「这个 Id 上次是不是被用户
/// 显式激活过」，具体落到哪个文件、用什么格式由实现决定（测试里可以指到仓库内的临时目录）。</para>
/// <para>口径（与 <c>docs/design/plugins.md</c> 的安全口径一致）：<b>读失败一律当空表、写失败只记日志</b>。
/// 激活状态丢了顶多是插件下次回到 Dormant——默认不启用本来就是安全默认值；绝不能因为一个 JSON 文件坏了
/// 就让插件页打不开，或者让「激活」按钮报错。</para>
/// </remarks>
public interface IPluginActivationStore
{
    /// <summary>
    /// 读全表（键 = 插件 Id，忽略大小写）。文件不存在、内容损坏、无权读取时返回空表，<b>不抛异常</b>。
    /// </summary>
    IReadOnlyDictionary<string, PluginActivationEntry> Load();

    /// <summary>
    /// 这个插件上次是不是被用户显式激活过（<see cref="PluginActivationEntry.Activated"/> 为 <c>true</c>）。
    /// </summary>
    /// <param name="pluginId">插件 Id；空串返回 <c>false</c>。</param>
    bool IsActivated(string pluginId);

    /// <summary>
    /// 写回单个插件的激活状态（其余条目原样保留）。落盘失败只记日志，<b>不抛异常</b>，
    /// 也不让调用方的「激活 / 停用」跟着失败——本次会话的状态已经在内存里生效了。
    /// </summary>
    /// <param name="pluginId">插件 Id；空串直接忽略。</param>
    /// <param name="activated">用户是否显式激活过。</param>
    void Save(string pluginId, bool activated);
}

/// <summary>
/// 一个插件的激活状态（<c>plugin-activation.json</c> 里的一条值）。
/// </summary>
/// <remarks>用 init 属性而不是位置记录：<c>System.Text.Json</c> 反序列化走无参构造 + init setter，
/// 不依赖构造参数名匹配，改字段顺序也不会读成错值。</remarks>
public sealed record PluginActivationEntry
{
    /// <summary>用户是否显式激活过（<c>false</c> = 用户停用过，或从未激活）。</summary>
    [JsonPropertyName("activated")]
    public bool Activated { get; init; }

    /// <summary>最后一次被激活的 UTC 时间；从未激活过为 <c>null</c>（停用不会清掉它，便于排障）。</summary>
    [JsonPropertyName("lastActivatedUtc")]
    public DateTimeOffset? LastActivatedUtc { get; init; }
}
