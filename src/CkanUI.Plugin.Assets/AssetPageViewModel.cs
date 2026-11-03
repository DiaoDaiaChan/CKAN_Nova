using System.Collections.ObjectModel;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.Plugin.Assets;

/// <summary>资产页（飞船 / 旗帜 / 加载图）的公共基类：忙闲、状态行、错误条、外壳通道。</summary>
/// <remarks>三个页面的骨架一致（取实例 → 扫描 → 操作 → 刷新），差异只在「扫什么、能改什么」。</remarks>
public abstract partial class AssetPageViewModel : PageViewModel
{
    /// <summary>构造页面。</summary>
    /// <param name="instances">实例服务。</param>
    /// <param name="errors">错误上报口。</param>
    /// <param name="dialogs">对话服务。</param>
    protected AssetPageViewModel(IInstanceService instances, IErrorReporter errors, IDialogService dialogs)
    {
        Instances = instances ?? throw new ArgumentNullException(nameof(instances));
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <summary>实例服务。</summary>
    protected IInstanceService Instances { get; }

    /// <summary>错误上报口。</summary>
    protected IErrorReporter Errors { get; }

    /// <summary>对话服务。</summary>
    protected IDialogService Dialogs { get; }

    /// <summary>系统文件管理器通道；由视图注入。</summary>
    public IAssetsShellLauncher? ShellLauncher { get; set; }

    /// <summary>是否有命令在跑。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>忙的时候的提示文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>状态行（条数统计 / 成功提示）。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误条主文案；null 表示没有错误。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>错误条的技术细节。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>是否显示错误条。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否显示状态行。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>列表为空时是否显示空态。</summary>
    public virtual bool ShowEmptyState => false;

    /// <summary>当前实例的游戏根目录；没有实例时点亮错误条并返回 null。</summary>
    protected string? CurrentGameDir()
    {
        var gameDir = Instances.Current?.GameDir;
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            SetError(Text("还没有可用的游戏实例。", LocalizationKeys.AssetsErrorNoInstance));
            return null;
        }

        return gameDir;
    }

    /// <summary>设置错误条。</summary>
    /// <param name="message">用户可读文案。</param>
    /// <param name="detail">技术细节。</param>
    protected void SetError(string? message, string? detail = null)
    {
        ErrorText = string.IsNullOrWhiteSpace(message)
            ? Text("操作失败。", LocalizationKeys.AssetsErrorOperationFailed)
            : message;
        ErrorDetail = detail;
    }

    /// <summary>清空错误条。</summary>
    protected void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    /// <summary>通知空态重新求值。</summary>
    protected void NotifyEmptyState()
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasStatus));
    }

    /// <summary>在系统文件管理器里打开一个路径。</summary>
    /// <param name="path">目录或文件路径。</param>
    protected void Reveal(string path)
    {
        if (ShellLauncher is null)
        {
            SetError(Text("当前环境无法打开系统文件管理器。", LocalizationKeys.AssetsErrorNoShell));
            return;
        }

        var result = ShellLauncher.Reveal(path);
        if (!result.IsSuccess)
        {
            SetError(result.Message, result.Detail);
        }
    }

    /// <summary>在忙闲保护下跑一个无返回值的服务调用，失败自动落到错误条。</summary>
    /// <param name="busyText">忙的时候显示的文案。</param>
    /// <param name="action">服务调用。</param>
    /// <param name="errorMessage">失败时的兜底文案。</param>
    protected async Task<OperationResult?> RunBusyAsync(string busyText, Func<Task<OperationResult>> action, string errorMessage)
    {
        IsBusy = true;
        BusyText = busyText;
        try
        {
            ClearError();
            var result = await action().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetError(result.Message ?? errorMessage, result.Detail);
            }

            return result;
        }
        catch (Exception ex)
        {
            Errors.Report(ex, "资产页", errorMessage);
            SetError(errorMessage, ex.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
            NotifyEmptyState();
        }
    }

    /// <summary>在忙闲保护下跑一个有返回值的服务调用，失败自动落到错误条。</summary>
    /// <typeparam name="T">返回值类型。</typeparam>
    /// <param name="busyText">忙的时候显示的文案。</param>
    /// <param name="action">服务调用。</param>
    /// <param name="errorMessage">失败时的兜底文案。</param>
    protected async Task<OperationResult<T>?> RunBusyAsync<T>(string busyText, Func<Task<OperationResult<T>>> action, string errorMessage)
    {
        IsBusy = true;
        BusyText = busyText;
        try
        {
            ClearError();
            var result = await action().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetError(result.Message ?? errorMessage, result.Detail);
            }

            return result;
        }
        catch (Exception ex)
        {
            Errors.Report(ex, "资产页", errorMessage);
            SetError(errorMessage, ex.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
            NotifyEmptyState();
        }
    }

    partial void OnIsBusyChanged(bool value) => NotifyEmptyState();

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>把行集合里的缩略图先烘进缓存：行集合是异步填的，现在有就立刻预热，没有就等第一次填充后再预热一次。</summary>
    /// <typeparam name="T">行类型。</typeparam>
    /// <param name="rows">行集合。</param>
    /// <param name="prewarm">预热动作。</param>
    /// <remarks>宿主只在预热阶段按 <c>IPageThumbnailWarmup</c> 举手调用一次，时序由页面自己负责（它比谁都清楚数据什么时候到）。</remarks>
    protected static void PrewarmWhenLoaded<T>(ObservableCollection<T> rows, Action<IReadOnlyList<T>> prewarm)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(prewarm);

        if (rows.Count > 0)
        {
            prewarm(rows.ToList());
            return;
        }

        void OnChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (rows.Count == 0)
            {
                return;
            }

            rows.CollectionChanged -= OnChanged;
            prewarm(rows.ToList());
        }

        rows.CollectionChanged += OnChanged;
    }
}
