using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Navigation;

/// <summary>
/// 工具条上的一个插件按钮（12 篇 F-17 工具项）。
/// </summary>
/// <remarks>
/// <para>按钮不构造插件视图模型：点下去时经 <see cref="IPluginService.InvokeToolItemAsync"/> 回调插件本人。
/// 执行结果（成功一句、失败一句）落在 <see cref="LastMessage"/> 上，由页面的状态栏/提示条显示——
/// 插件抛异常也只影响这一次点击，不会把页面带崩。</para>
/// <para>重入保护：<see cref="IsBusy"/> 为真时再次点击直接返回，避免用户连点触发两次写操作。</para>
/// </remarks>
public sealed partial class PluginToolButtonViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<OperationResult>> _execute;

    /// <summary>由工具项条目与回调构造。</summary>
    /// <param name="entry">工具项条目（来自 <see cref="PluginToolCatalog"/>）。</param>
    /// <param name="execute">回调；返回成功/失败结果。</param>
    public PluginToolButtonViewModel(PluginToolItemEntry entry, Func<CancellationToken, Task<OperationResult>> execute)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        PluginId = entry.PluginId;
        PluginName = entry.PluginName;
        Key = entry.Key;
        Title = entry.Title;
        ToolTip = entry.ToolTip;
    }

    /// <summary>插件标识。</summary>
    public string PluginId { get; }

    /// <summary>插件显示名。</summary>
    public string PluginName { get; }

    /// <summary>工具项键（插件内唯一）。</summary>
    public string Key { get; }

    /// <summary>按钮文字。</summary>
    public string Title { get; }

    /// <summary>插件给的悬停提示（可能为 <c>null</c>）。</summary>
    public string? ToolTip { get; }

    /// <summary>
    /// 实际显示的悬停提示：先看最近一次执行结果（成功/失败都算），没有结果时用插件给的提示，
    /// 再没有就用「插件名 · 键」——让用户知道这个按钮是谁装的。
    /// </summary>
    public string ToolTipText =>
        !string.IsNullOrWhiteSpace(LastMessage)
            ? LastMessage!
            : string.IsNullOrWhiteSpace(ToolTip) ? PluginName + " · " + Key : ToolTip!;

    /// <summary>正在执行（重入保护 + 按钮置灰）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>上一次执行的结果文案（成功/失败各一句；<c>null</c> 表示还没点过）。</summary>
    [ObservableProperty]
    private string? _lastMessage;

    partial void OnLastMessageChanged(string? value) => OnPropertyChanged(nameof(ToolTipText));

    /// <summary>点击：执行工具项并把结果写进 <see cref="LastMessage"/>。</summary>
    [RelayCommand]
    private async Task ExecuteAsync(CancellationToken ct)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _execute(ct).ConfigureAwait(true);
            LastMessage = result.IsSuccess
                ? "已执行「" + Title + "」。"
                : result.Message;
        }
        catch (OperationCanceledException)
        {
            LastMessage = "「" + Title + "」已取消。";
        }
        catch (Exception ex)
        {
            LastMessage = "「" + Title + "」执行失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
