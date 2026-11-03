using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Plugin.Ai.Chat;
using CkanUI.Plugin.Ai.Search;
using CkanUI.Plugin.Ai.Settings;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Abstractions.Security;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Ai;

/// <summary>
/// 「AI 助手」页签的视图模型。
/// </summary>
/// <remarks>
/// <para><b>依赖注入</b>：宿主用 <c>ActivatorUtilities.CreateInstance</c> 构造插件页（见
/// <c>PluginPageFactory.Create</c>），所以这里只暴露一个收 <see cref="IServiceProvider"/> 的构造函数，
/// 再从容器里按需取桥与密钥保护器。这样宿主侧桥的实现还没注册时页面仍能打开并给出明确提示，
/// 而不是整个页签构造失败变成空白。</para>
/// <para><b>线程纪律</b>：命令在 UI 线程启动；工具轨迹回调来自工作线程，统一经构造时捕获的
/// <see cref="SynchronizationContext"/> 封送回来再改集合。拿不到上下文（单测/设计时）就直接改。</para>
/// <para><b>状态分离</b>：空态、忙碌态、错误态、工具轨迹是四件不同的事，各有自己的属性；
/// 不用一份「看起来正常」的空列表掩盖失败。</para>
/// <para><b>资源生命周期</b>：HTTP 客户端页面级复用，MCP 子进程按需启动并在页面销毁时回收
/// （<see cref="Dispose"/>）；设置保存后重建引擎，避免旧 BaseUrl/Model 继续生效。</para>
/// </remarks>
public sealed class AiAssistantPageViewModel : ObservableObject, IPluginPage, IDisposable
{
    /// <summary>页签打开时的初始提示。</summary>
    private const string ReadyHint = "就绪";

    private readonly IAiCkanBridge? _bridge;
    private readonly AiSettingsStore _settings;
    private readonly HttpClient _http;
    private readonly SynchronizationContext? _sync;

    private AiSettings _snapshot;
    private AiToolRunner? _tools;
    private AiChatClient? _chat;
    private McpSearchClient? _search;
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _disposed;

    private string _draftText = string.Empty;
    private bool _isBusy;
    private string? _errorText;
    private bool _isToolTraceExpanded;
    private string _statusText = ReadyHint;
    private string _baseUrl = AiSettings.DefaultBaseUrl;
    private string _model = AiSettings.DefaultModel;
    private string _apiKey = string.Empty;
    private string _mcpCommand = AiSettings.DefaultMcpCommand;
    private string _mcpArgsText = string.Empty;

    /// <summary>宿主构造入口：从 DI 容器里取桥与密钥保护器。</summary>
    /// <param name="services">宿主 DI 容器。</param>
    public AiAssistantPageViewModel(IServiceProvider services)
        : this(
            services is null ? null : services.GetService(typeof(IAiCkanBridge)) as IAiCkanBridge,
            services is null ? null : services.GetService(typeof(ISecretProtector)) as ISecretProtector,
            settingsPath: null,
            http: null)
    {
    }

    private AiAssistantPageViewModel(IAiCkanBridge? bridge, ISecretProtector? protector, string? settingsPath, HttpClient? http)
    {
        _bridge = bridge;
        _settings = new AiSettingsStore(settingsPath, protector);
        _sync = SynchronizationContext.Current;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        _snapshot = _settings.Load();
        _baseUrl = _snapshot.BaseUrl;
        _model = _snapshot.Model;
        _mcpCommand = _snapshot.McpCommand;
        _mcpArgsText = string.Join(' ', _snapshot.McpArgs);
        _apiKey = _settings.LoadApiKey() ?? string.Empty;

        SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
        ClearCommand = new RelayCommand(Clear);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);

        // 折叠区的标题要随条数走：不订阅的话「工具轨迹（3 步）」会一直显示旧数字。
        ToolTraces.CollectionChanged += OnToolTracesChanged;
    }

    /// <summary>给测试与设计时用的构造入口（不经过 DI 容器）。</summary>
    /// <param name="bridge">宿主桥；<c>null</c> 时工具都会回「宿主未连接」。</param>
    /// <param name="protector">密钥保护器；<c>null</c> 时无法保存 Key。</param>
    /// <param name="settingsPath">设置文件路径；<c>null</c> 用默认路径。</param>
    /// <param name="http">复用的 HTTP 客户端；<c>null</c> 时自建。</param>
    /// <returns>视图模型实例。</returns>
    public static AiAssistantPageViewModel Create(
        IAiCkanBridge? bridge = null,
        ISecretProtector? protector = null,
        string? settingsPath = null,
        HttpClient? http = null)
        => new(bridge, protector, settingsPath, http);

    // ── 对话 ────────────────────────────────────────────────────────────────

    /// <summary>对话消息（用户与助手混排，按时间顺序）。</summary>
    public ObservableCollection<AiChatMessage> Messages { get; } = new();

    /// <summary>本轮问答的工具轨迹行。</summary>
    public ObservableCollection<AiToolTrace> ToolTraces { get; } = new();

    /// <summary>有没有工具轨迹（决定折叠区显不显示）。</summary>
    public bool HasToolTraces => ToolTraces.Count > 0;

    /// <summary>折叠区标题。</summary>
    public string ToolTraceSummary => HasToolTraces ? $"工具轨迹（{ToolTraces.Count} 步）" : "工具轨迹";

    /// <summary>折叠区是否展开。</summary>
    public bool IsToolTraceExpanded
    {
        get => _isToolTraceExpanded;
        set => SetProperty(ref _isToolTraceExpanded, value);
    }

    /// <summary>输入框内容。</summary>
    public string DraftText
    {
        get => _draftText;
        set
        {
            if (SetProperty(ref _draftText, value))
            {
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>是否有一轮问答正在跑（发送按钮禁用、停止按钮启用）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CancelCommand.NotifyCanExecuteChanged();
                SendCommand.NotifyCanExecuteChanged();
                TestConnectionCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    /// <summary>空闲态（界面用来显示输入提示）。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>错误条文案；<c>null</c> 表示没有错误。</summary>
    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>有没有错误要显示。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>状态栏文案（就绪 / 正在测试连接 / 设置已保存 …）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    // ── 设置面板 ────────────────────────────────────────────────────────────

    /// <summary>OpenAI 兼容端点前缀。</summary>
    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    /// <summary>模型名。</summary>
    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    /// <summary>明文 API Key（只在内存里；保存时由 <see cref="ISecretProtector"/> 加密）。</summary>
    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    /// <summary>MCP 搜索服务器命令。</summary>
    public string McpCommand
    {
        get => _mcpCommand;
        set => SetProperty(ref _mcpCommand, value);
    }

    /// <summary>MCP 参数（空格分隔，带引号的段算一个参数）。</summary>
    public string McpArgsText
    {
        get => _mcpArgsText;
        set => SetProperty(ref _mcpArgsText, value);
    }

    /// <summary>设置文件位置（显示在设置面板里，方便用户备份/排查）。</summary>
    public string SettingsPath => _settings.FilePath;

    // ── 命令 ────────────────────────────────────────────────────────────────

    /// <summary>发送当前输入。</summary>
    public IAsyncRelayCommand SendCommand { get; }

    /// <summary>清空对话与轨迹。</summary>
    public IRelayCommand ClearCommand { get; }

    /// <summary>停止当前回答。</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>保存设置（含加密后的 Key）。</summary>
    public IAsyncRelayCommand SaveSettingsCommand { get; }

    /// <summary>测试端点连通性。</summary>
    public IAsyncRelayCommand TestConnectionCommand { get; }

    /// <summary>页面首次挂到可视树时调用；幂等（视图可能被复用）。</summary>
    public void Initialize()
    {
        if (_initialized || _disposed)
        {
            return;
        }

        _initialized = true;

        if (_bridge is null)
        {
            ErrorText = "AI 助手没有连接到宿主：缺少 IAiCkanBridge 实现，CKAN 数据工具不可用。";
            return;
        }

        StatusText = string.IsNullOrWhiteSpace(_apiKey)
            ? "请先在设置面板填入 API Key 并保存。"
            : ReadyHint;
    }

    /// <summary>释放页面级资源（HTTP 客户端与 MCP 子进程）。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ToolTraces.CollectionChanged -= OnToolTracesChanged;
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经释放过的 CTS：取消失败无所谓，接下来就整体回收。
        }

        _cts?.Dispose();
        _cts = null;
        _search?.Dispose();
        _search = null;
        _http.Dispose();
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(DraftText);

    private async Task SendAsync()
    {
        var text = (DraftText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (_bridge is null)
        {
            ErrorText = "AI 助手没有连接到宿主：缺少 IAiCkanBridge 实现。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ErrorText = "请先在设置面板填入 API Key 并保存。";
            return;
        }

        ErrorText = null;
        Messages.Add(AiChatMessage.User(text));
        DraftText = string.Empty;
        ToolTraces.Clear();
        StatusText = "正在思考…";
        IsBusy = true;

        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var chat = EnsureChat();
            var outcome = await chat
                .RunAsync(BuildOptions(), SnapshotHistory(), trace => PostToUi(() => ToolTraces.Add(trace)), cts.Token);

            Messages.Add(AiChatMessage.Assistant(outcome.Text));
            StatusText = outcome.StopReason switch
            {
                AiStopReason.ToolRoundsExhausted => $"工具轮次已用尽（{outcome.ToolRounds} 轮），换个更具体的问题再试。",
                AiStopReason.EmptyAnswer => "模型返回了空回答。",
                _ => outcome.ToolRounds == 0 ? "回答完成" : $"回答完成（{outcome.ToolRounds} 轮工具调用）",
            };
        }
        catch (OperationCanceledException)
        {
            Messages.Add(AiChatMessage.Assistant("已停止本次回答。"));
            StatusText = "已停止";
        }
        catch (AiChatException ex)
        {
            ErrorText = ex.Message;
            StatusText = "回答失败";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or NotSupportedException or ObjectDisposedException)
        {
            ErrorText = "回答失败：" + ex.Message;
            StatusText = "回答失败";
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }

            cts.Dispose();
        }
    }

    private void Clear()
    {
        Messages.Clear();
        ToolTraces.Clear();
        ErrorText = null;
        StatusText = "已清空对话";
    }

    private void Cancel()
    {
        var cts = _cts;
        if (cts is null)
        {
            return;
        }

        StatusText = "正在停止…";
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 这一轮刚好结束：没什么可取消的。
        }
    }

    private Task SaveSettingsAsync()
    {
        var updated = _snapshot with
        {
            BaseUrl = BaseUrl,
            Model = Model,
            McpCommand = McpCommand,
            McpArgs = SplitArguments(McpArgsText),
        };

        var result = _settings.SaveWithApiKey(updated, ApiKey ?? string.Empty);
        if (!result.IsSuccess)
        {
            ErrorText = result.Message ?? "保存设置失败。";
            StatusText = "设置未保存";
            return Task.CompletedTask;
        }

        _snapshot = updated.Normalized();
        BaseUrl = _snapshot.BaseUrl;
        Model = _snapshot.Model;
        McpCommand = _snapshot.McpCommand;
        McpArgsText = string.Join(' ', _snapshot.McpArgs);
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        ErrorText = null;
        StatusText = "设置已保存：" + _settings.FilePath;
        ResetEngine();
        return Task.CompletedTask;
    }

    private async Task TestConnectionAsync()
    {
        ErrorText = null;
        StatusText = "正在测试连接…";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AiEndpoints.Models(BaseUrl));
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey.Trim());
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                StatusText = $"连接正常：HTTP {(int)response.StatusCode}";
            }
            else
            {
                StatusText = $"连接失败：HTTP {(int)response.StatusCode}";
                ErrorText = $"端点返回 HTTP {(int)response.StatusCode}，检查 Base URL 与 API Key。";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or OperationCanceledException or AiChatException or InvalidOperationException)
        {
            StatusText = "连接失败";
            ErrorText = "连接失败：" + ex.Message;
        }
    }

    private AiChatClient EnsureChat()
    {
        if (_chat is not null)
        {
            return _chat;
        }

        _tools ??= new AiToolRunner(_bridge, _search ??= CreateSearchClient());
        _chat = new AiChatClient(_http, _tools);
        return _chat;
    }

    private McpSearchClient? CreateSearchClient()
    {
        if (string.IsNullOrWhiteSpace(McpCommand))
        {
            return null;
        }

        return new McpSearchClient(McpCommand.Trim(), SplitArguments(McpArgsText));
    }

    private void ResetEngine()
    {
        _chat = null;
        _tools = null;
        _search?.Dispose();
        _search = null;
    }

    private AiChatOptions BuildOptions() => new(
        _snapshot.BaseUrl,
        _snapshot.Model,
        ApiKey,
        _snapshot.Temperature,
        _snapshot.MaxToolRounds,
        _snapshot.SystemPrompt);

    private IReadOnlyList<AiChatMessage> SnapshotHistory()
        => Messages.Where(message => !string.IsNullOrWhiteSpace(message.Text)).ToList();

    private void PostToUi(Action action)
    {
        if (_sync is null)
        {
            action();
            return;
        }

        _sync.Post(static state => ((Action)state!)(), action);
    }

    private void OnToolTracesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasToolTraces));
        OnPropertyChanged(nameof(ToolTraceSummary));
    }

    /// <summary>把「空格分隔、支持双引号」的参数串切成数组。</summary>
    /// <param name="text">用户输入的参数串。</param>
    /// <returns>参数数组；空串返回空数组。</returns>
    private static string[] SplitArguments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments.ToArray();
    }
}
