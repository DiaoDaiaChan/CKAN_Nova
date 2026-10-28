using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Content;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>所选模组 ZIP 内容页（F-29 / UI-03）。</summary>
public sealed partial class ContentPageViewModel : PageViewModel, IPageProjectionTrim
{
    private readonly IContentService _service;
    private readonly IContentTargetProvider _target;
    private CancellationTokenSource? _runCts;

    public ContentPageViewModel() : this(new EmptyContentService(), new EmptyTarget()) { }

    public ContentPageViewModel(IContentService service, IContentTargetProvider target)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override string Title => "内容预览";

    /// <summary>当前选中的节点。</summary>
    [ObservableProperty]
    private ContentNodeViewModel? _selectedNode;

    /// <summary>内容树根节点集合。</summary>
    [ObservableProperty]
    private IReadOnlyList<ContentNodeViewModel> _rootNodes = Array.Empty<ContentNodeViewModel>();

    /// <summary>兼容旧平铺集合。</summary>
    [ObservableProperty]
    private IReadOnlyList<ContentNodeViewModel> _entries = Array.Empty<ContentNodeViewModel>();

    /// <summary>ZIP 归档全文件未压缩总字节数。</summary>
    [ObservableProperty]
    private long _totalBytes;

    /// <summary>当前模组 ZIP 缓存绝对路径（若有）。</summary>
    [ObservableProperty]
    private string? _cacheFilePath;

    /// <summary>当前模组标识符。</summary>
    [ObservableProperty]
    private string? _currentIdentifier;

    /// <summary>是否已缓存。</summary>
    [ObservableProperty]
    private bool _isCached;

    /// <summary>是否处于忙碌（加载/下载）状态。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDirectoryCommand))]
    private bool _isBusy;

    /// <summary>主错误信息。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private string? _errorText;

    /// <summary>详细错误信息。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>操作提示或状态文字。</summary>
    [ObservableProperty]
    private string? _statusText;

    /// <summary>空状态提示文本。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string _emptyText = "请先在模组页选择一个模组。";

    /// <summary>宿主注入点：剪贴板与文件管理器操作。</summary>
    public IContentShellLauncher? ShellLauncher { get; set; }

    public string TotalText => ContentFormat.FormatSize(TotalBytes);

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool ShowEmptyState => RootNodes.Count == 0 && !IsBusy;

    public bool HasNodes => RootNodes.Count > 0;

    public bool CanCancel => IsBusy;

    public bool CanCopyOrOpen => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task PreviewAsync() => RunAsync(false);

    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task DownloadAsync() => RunAsync(true);

    [RelayCommand(CanExecute = nameof(CanRetry))]
    public Task RetryAsync() => RunAsync(false);

    [RelayCommand(CanExecute = nameof(CanCancelMethod))]
    public void Cancel()
    {
        _runCts?.Cancel();
    }

    [RelayCommand]
    public void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrOpenMethod))]
    public async Task CopyPathAsync()
    {
        if (SelectedNode is not { } node)
        {
            StatusText = "请先在列表中选中一个文件或目录。";
            return;
        }

        if (ShellLauncher is not { } launcher)
        {
            StatusText = "当前宿主没有提供剪贴板支持。";
            return;
        }

        var result = await launcher.CopyTextAsync(node.RelativePath).ConfigureAwait(true);
        StatusText = result.IsSuccess ? $"已复制路径：{node.RelativePath}" : (result.Message ?? "复制失败。");
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrOpenMethod))]
    public async Task OpenDirectoryAsync()
    {
        if (string.IsNullOrWhiteSpace(CacheFilePath) || !File.Exists(CacheFilePath))
        {
            StatusText = "当前模组 ZIP 尚未下载，无法打开所在目录。";
            return;
        }

        if (ShellLauncher is not { } launcher)
        {
            StatusText = "当前宿主没有提供文件管理器支持。";
            return;
        }

        var folder = Path.GetDirectoryName(CacheFilePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            StatusText = "ZIP 所在目录不存在。";
            return;
        }

        var result = await launcher.OpenFolderAsync(folder).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            StatusText = result.Message ?? "打开目录失败。";
        }
    }

    protected override async void OnActivated()
    {
        base.OnActivated();
        if (IsBusy) return;

        var selectedId = await _target.GetSelectedIdentifierAsync().ConfigureAwait(true);
        if (selectedId != CurrentIdentifier || RootNodes.Count == 0)
        {
            _ = PreviewAsync();
        }
    }

    protected override void OnDeactivated()
    {
        base.OnDeactivated();
        _runCts?.Cancel();
    }

    /// <inheritdoc />
    public string PageName => "内容预览";

    /// <summary>
    /// 丢掉 ZIP 目录树投影（18 篇 §3 L1）：文件条目来自缓存 ZIP 的中央目录，重进页面时会再读一次。
    /// </summary>
    /// <remarks>
    /// 这里丢掉的是「条目清单 + 树的包装」两份视图，ZIP 缓存文件本身留在磁盘上，
    /// 所以 <see cref="OnActivated"/> 里 <c>RootNodes.Count == 0</c> 的分支会把预览重新算出来。
    /// </remarks>
    public int TrimProjection()
    {
        var released = Entries.Count + RootNodes.Count;
        if (released == 0)
        {
            return 0;
        }

        Entries = Array.Empty<ContentNodeViewModel>();
        RootNodes = Array.Empty<ContentNodeViewModel>();
        SelectedNode = null;
        return released;
    }

    private bool CanRun() => !IsBusy;
    private bool CanRetry() => !IsBusy && HasError;
    private bool CanCancelMethod() => IsBusy;
    private bool CanCopyOrOpenMethod() => !IsBusy;

    private async Task RunAsync(bool download)
    {
        if (IsBusy) return;

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        IsBusy = true;
        ErrorText = ErrorDetail = null;
        StatusText = null;

        try
        {
            var id = await _target.GetSelectedIdentifierAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(id))
            {
                CurrentIdentifier = null;
                CacheFilePath = null;
                Entries = Array.Empty<ContentNodeViewModel>();
                RootNodes = Array.Empty<ContentNodeViewModel>();
                EmptyText = "请先在模组页选择一个模组。";
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(HasNodes));
                return;
            }

            CurrentIdentifier = id;

            if (download)
            {
                var cached = await _service.EnsureCachedAsync(id, ct: ct).ConfigureAwait(true);
                if (!cached.IsSuccess)
                {
                    ErrorText = ContentErrorText.Compose(cached.Message);
                    ErrorDetail = cached.Detail;
                    return;
                }
            }

            var result = await _service.PreviewAsync(id, ct).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                Entries = Array.Empty<ContentNodeViewModel>();
                RootNodes = Array.Empty<ContentNodeViewModel>();
                ErrorText = ContentErrorText.Compose(result.Message);
                ErrorDetail = result.Detail;
                EmptyText = "无法读取此模组的内容。";
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(HasNodes));
                return;
            }

            var preview = result.Value;
            IsCached = preview.IsCached;
            CacheFilePath = preview.CacheFilePath;
            TotalBytes = preview.TotalBytes;

            var rawNodes = preview.Entries.Select(ContentNodeViewModel.From).ToArray();
            Entries = rawNodes;
            RootNodes = ContentTreeBuilder.Build(preview.Entries);

            EmptyText = RootNodes.Count == 0 ? "ZIP 中没有文件条目。" : string.Empty;
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(HasNodes));
        }
        catch (OperationCanceledException)
        {
            ErrorText = "操作已取消。";
            ErrorDetail = null;
        }
        catch (Exception ex)
        {
            ErrorText = "读取模组内容失败。";
            ErrorDetail = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private sealed class EmptyTarget : IContentTargetProvider
    {
        public Task<string?> GetSelectedIdentifierAsync() => Task.FromResult<string?>(null);
    }

    private sealed class EmptyContentService : IContentService
    {
        public Task<OperationResult<ContentPreview>> PreviewAsync(string identifier, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<ContentPreview>.Fail("内容服务尚未初始化。"));

        public Task<OperationResult<string>> EnsureCachedAsync(string identifier, IProgress<OperationProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string>.Fail("内容服务尚未初始化。"));
    }
}
