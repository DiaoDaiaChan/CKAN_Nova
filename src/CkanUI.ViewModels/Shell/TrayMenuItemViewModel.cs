using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Shell;

/// <summary>托盘右键菜单的七项标识（12 篇 F-51 / UI-26），顺序即菜单顺序。</summary>
public enum TrayMenuItemId
{
    /// <summary>显示主窗口。</summary>
    ShowWindow,

    /// <summary>隐藏到托盘。</summary>
    HideWindow,

    /// <summary>打开日志目录。</summary>
    OpenLogFolder,

    /// <summary>打开缓存目录。</summary>
    OpenCacheFolder,

    /// <summary>打开当前实例目录。</summary>
    OpenInstanceFolder,

    /// <summary>刷新仓库。</summary>
    RefreshRepositories,

    /// <summary>退出。</summary>
    Exit,
}

/// <summary>
/// 托盘右键菜单的一项（12 篇 F-51）。只承载「文案 + 命令 + 可用性」，
/// 真正的动作由 <see cref="ITrayHostActions"/> 实现，故本类可脱离 GUI 单测。
/// </summary>
/// <remarks>
/// 原生菜单没有绑定引擎（<c>NativeMenuItem</c> 只认 <c>ICommand</c>），
/// 因此每项自己持有一条 <see cref="IAsyncRelayCommand"/>：菜单点击与单测走同一条路径。
/// </remarks>
public sealed class TrayMenuItemViewModel : ObservableObject
{
    private readonly Func<Task>? _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isBusy;
    private string? _lastError;

    /// <summary>构造一个菜单项。</summary>
    /// <param name="id">菜单项标识。</param>
    /// <param name="header">菜单文案（中文，原生菜单不走语言包）。</param>
    /// <param name="execute">动作；为空表示这一项在当前环境里没有可用通道。</param>
    /// <param name="canExecute">附加值条件（例如「宿主已接上」）；为空表示只看 <paramref name="execute"/>。</param>
    /// <param name="startsGroup">是否在它之前插一条分隔线。</param>
    public TrayMenuItemViewModel(
        TrayMenuItemId id,
        string header,
        Func<Task>? execute,
        Func<bool>? canExecute = null,
        bool startsGroup = false)
    {
        Id = id;
        Header = header ?? throw new ArgumentNullException(nameof(header));
        _execute = execute;
        _canExecute = canExecute;
        StartsGroup = startsGroup;
        Command = new AsyncRelayCommand(ExecuteAsync, CanRun);
    }

    /// <summary>菜单项标识。</summary>
    public TrayMenuItemId Id { get; }

    /// <summary>菜单文案。</summary>
    public string Header { get; }

    /// <summary>是否在它之前插一条分隔线（原生菜单没有分组容器，只能逐项标记）。</summary>
    public bool StartsGroup { get; }

    /// <summary>是否有可执行动作。</summary>
    public bool HasAction => _execute is not null;

    /// <summary>点击时执行的命令。</summary>
    public IAsyncRelayCommand Command { get; }

    /// <summary>是否正在执行（执行期间按钮置灰，避免重复刷新仓库）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                Command.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>最近一次执行的失败原因；成功后清空。</summary>
    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    /// <summary>重新求值可用性（宿主接上/摘下、设置变化后由菜单统一调用）。</summary>
    public void NotifyCanExecuteChanged() => Command.NotifyCanExecuteChanged();

    /// <summary>当前是否可执行。</summary>
    public bool CanRun() => _execute is not null && !IsBusy && (_canExecute?.Invoke() ?? true);

    private async Task ExecuteAsync()
    {
        if (_execute is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        LastError = null;
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 托盘回调跑在宿主线程上，异常一旦逸出会静默杀掉菜单响应；这里收口成中文错误。
            LastError = $"执行「{Header}」失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
