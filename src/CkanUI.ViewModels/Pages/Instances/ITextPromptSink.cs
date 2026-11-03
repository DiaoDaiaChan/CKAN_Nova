namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 页面视图模型的「单行文本输入由宿主注入」契约。
/// </summary>
/// <remarks>
/// <para><c>IInstanceTextPrompt</c> 的实现住在 App 层（要弹 <c>TextInputDialogView</c>，需要 Avalonia 控件），
/// 插件页面又不能反向引用 App。所以页面只声明「我需要这个能力」，由外壳在建好视图之后注入——与
/// <c>IPageThumbnailWarmup</c> 同一套路：外壳不认识具体页面类型，只认接口。</para>
/// <para>没注入时页面必须按「当前环境无法输入文本」降级，不能静默失败。</para>
/// </remarks>
public interface ITextPromptSink
{
    /// <summary>宿主的文本输入实现；未注入时为 <c>null</c>。</summary>
    IInstanceTextPrompt? TextPrompt { get; set; }
}
