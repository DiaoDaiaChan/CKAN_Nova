using System;

using Avalonia.Controls;

using log4net;

namespace CkanUI.App.Ai;

/// <summary>
/// 底栏「AI 助手」按钮背后的开合逻辑：解析插件页 → 建视图 → 关掉时连视图模型一起释放。
/// </summary>
/// <remarks>
/// <para>刻意不把这段逻辑塞进 <see cref="MainWindow"/>：解析页面与建视图都是委托，无头用例塞假页面和假控件
/// 就能验「打开 → 有内容 → 关闭 → 释放」整条链，不必真加载插件 dll，也不碰渲染（headless 下驱帧会挂死，
/// 见 docs/design/plugins.md §11.7）。</para>
/// <para><b>释放是硬要求</b>：面板关掉后视图模型还挂着就没人退订、没人停聊天流；插件被停用时
/// <c>PluginPageCache</c> 也认不到这个实例（它不是导航页），ALC 就永远回收不了。所以 <see cref="Close"/>
/// 一定 Dispose，抛异常也只记日志。</para>
/// </remarks>
public sealed class AiAssistantLauncher
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AiAssistantLauncher));

    /// <summary>AI 助手页的导航键（与 <c>src/CkanUI.Plugin.Ai/AiPlugin.cs</c> 声明的键一致）。</summary>
    public const string PageKey = "ai-assistant";

    private readonly Func<string, object?> _resolvePage;
    private readonly Func<object, Control?> _buildView;

    private object? _viewModel;
    private Control? _view;

    /// <summary>创建启动器。</summary>
    /// <param name="resolvePage">导航键 → 插件页视图模型；插件没启用时返回 <c>null</c>。</param>
    /// <param name="buildView">视图模型 → 控件；找不到视图时返回 <c>null</c>。</param>
    public AiAssistantLauncher(Func<string, object?> resolvePage, Func<object, Control?> buildView)
    {
        _resolvePage = resolvePage ?? throw new ArgumentNullException(nameof(resolvePage));
        _buildView = buildView ?? throw new ArgumentNullException(nameof(buildView));
    }

    /// <summary>面板是否已经打开（有视图即视为打开）。</summary>
    public bool IsOpen => _view is not null;

    /// <summary>面板里的视图；关闭后为 <c>null</c>。</summary>
    public Control? View => _view;

    /// <summary>面板里的视图模型；关闭后为 <c>null</c>。</summary>
    public object? ViewModel => _viewModel;

    /// <summary>
    /// 打开面板。已打开时直接返回 <c>true</c>（按钮第二次点应该收起，由调用方判断）。
    /// </summary>
    /// <param name="error">失败时给用户看的一句话；成功时为 <c>null</c>。</param>
    public bool Open(out string? error)
    {
        error = null;
        if (IsOpen)
        {
            return true;
        }

        object? page;
        try
        {
            page = _resolvePage(PageKey);
        }
        catch (Exception ex)
        {
            Log.Warn("解析 AI 助手页面失败。", ex);
            error = "AI 助手页面没能解析：" + ex.Message;
            return false;
        }

        if (page is null)
        {
            error = "AI 助手插件没有启用：到「工具 › 插件」里启用它，再点这里。";
            return false;
        }

        Control? view;
        try
        {
            view = _buildView(page);
        }
        catch (Exception ex)
        {
            Log.Warn("构建 AI 助手视图失败。", ex);
            DisposePage(page);
            error = "AI 助手的视图没能创建：" + ex.Message;
            return false;
        }

        if (view is null)
        {
            DisposePage(page);
            error = "AI 助手的视图没能创建：插件里找不到对应视图。";
            return false;
        }

        _viewModel = page;
        _view = view;
        return true;
    }

    /// <summary>关闭面板并释放页面实例（幂等）。</summary>
    public void Close()
    {
        var page = _viewModel;
        _view = null;
        _viewModel = null;
        DisposePage(page);
    }

    private static void DisposePage(object? page)
    {
        if (page is not IDisposable disposable)
        {
            return;
        }

        try
        {
            disposable.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("释放 AI 助手页面失败（已忽略）。", ex);
        }
    }
}
