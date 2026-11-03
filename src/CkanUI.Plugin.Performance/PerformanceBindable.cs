using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CkanUI.Plugin.Performance;

/// <summary>
/// 插件自己的最小 MVVM 基类。
/// </summary>
/// <remarks>
/// 插件只允许引用契约层与 Avalonia（架构铁律），而宿主的 <c>ObservableObject</c> 来自
/// <c>CkanUI.ViewModels</c> 里的 CommunityToolkit——那是宿主实现，插件不能引。
/// 与其为一个 <c>SetProperty</c> 破例加包，不如在这里写 20 行：依赖面越小，插件越不会被宿主重构带崩。
/// </remarks>
public abstract class PerformanceObservable : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>赋值并在变化时通知；返回是否真的变了。</summary>
    /// <typeparam name="T">属性类型。</typeparam>
    /// <param name="field">字段引用。</param>
    /// <param name="value">新值。</param>
    /// <param name="propertyName">属性名（编译器自动填）。</param>
    /// <returns>是否发生变化。</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    /// <summary>发出属性变更通知。</summary>
    /// <param name="propertyName">属性名（编译器自动填）。</param>
    protected void Raise([CallerMemberName] string? propertyName = null)
    {
        if (propertyName is null)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>批量发通知（派生属性用）。</summary>
    /// <param name="propertyNames">属性名列表。</param>
    protected void RaiseAll(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            Raise(name);
        }
    }
}

/// <summary>异步命令：执行期间自锁，避免连点。</summary>
public sealed class PerformanceAsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _running;

    /// <summary>构造。</summary>
    /// <param name="execute">执行体（内部必须自己兜异常；这里再兜一层只是防御）。</param>
    /// <param name="canExecute">可用性判定。</param>
    /// <param name="onError">未捕获异常的上报回调。</param>
    public PerformanceAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>通知可用性变化。</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>带参数的命令（热点榜页签切换）。</summary>
public sealed class PerformanceParameterCommand : ICommand
{
    private readonly Action<string> _execute;

    /// <summary>构造。</summary>
    /// <param name="execute">执行体，参数为字符串键。</param>
    public PerformanceParameterCommand(Action<string> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => parameter is string;

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (parameter is string key)
        {
            _execute(key);
        }
    }
}
