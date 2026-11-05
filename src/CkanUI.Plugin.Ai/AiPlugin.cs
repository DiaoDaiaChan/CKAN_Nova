using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Ai;

/// <summary>
/// 「AI 助手」页签插件入口（内置插件，契约 docs/design/plugins.md）。
/// </summary>
/// <remarks>
/// <para>本类只做三件事：报元数据、声明一个页签、把生命周期回调做成空实现。
/// 真正的网络、工具调用与状态全部跟着页面实例走（<see cref="AiAssistantPageViewModel"/>）——
/// 没人看的时候不该留着一个 HTTP 长连接或一个 MCP 子进程。</para>
/// <para><see cref="Initialize"/> / <see cref="Deinitialize"/> 必须能反复成对调用（宿主重载路径），
/// 这里没有要停的后台资源，因此两个方法都是幂等的空实现，而不是抛 NotSupported。</para>
/// </remarks>
public sealed class AiPlugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识（反向域名）。</summary>
    public const string PluginId = "com.ckanui.ai";

    /// <summary>页签导航键（不得与宿主内置页签重名）。</summary>
    public const string PageKey = "ai-assistant";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    private static readonly PluginPageDescriptor[] PageList =
    {
        // Glyph 取宿主的「星光」字形（Material auto_awesome）：AI 助手不是工具表也不是齿轮。
        // 独立侧栏入口落在「扩展」分组。
        new(
            PageKey,
            "AI 助手",
            typeof(AiAssistantPageViewModel).FullName ?? nameof(AiAssistantPageViewModel),
            Glyph: "Sparkle",
            Order: 10),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "AI 助手";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "ckan-ui";

    /// <inheritdoc />
    public string Description => "内置 AI 助手：交互式问答，可调用 CKAN 数据工具与网页搜索，并能帮你打开界面页面。";

    /// <inheritdoc />
    /// <remarks>空串 = 不限制宿主版本：本页只用到 <see cref="ICkanUiPlugin"/> / <see cref="IPluginPage"/> 与契约接口，
    /// 不依赖宿主的任何具体实现，硬写一个下限反而会在版本号变动时误拦。</remarks>
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public void Initialize()
    {
        // 没有后台工作要启动：HTTP 客户端与 MCP 子进程都由页面实例按需创建。
    }

    /// <inheritdoc />
    public void Deinitialize()
    {
        // 与 Initialize 对称的空实现，保证 初始化→停用→初始化 的路径可重入。
    }
}
