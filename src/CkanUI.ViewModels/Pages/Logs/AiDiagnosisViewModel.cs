using System.Collections.ObjectModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Abstractions.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// AI 排障面板（14 篇 §6）：先把要发的内容原样给用户看，点「发送」才真的出网。
/// 三条纪律：① 不可逆的代价（花钱、把日志发到别人的服务器）必须先预览再确认；
/// ② API Key 只以 DPAPI 密文落盘，解不开就当没配；③ 模型说第几行，界面就能跳回第几行，
/// 所以假设行把 evidenceSeq 原样留着，而不是揉成一句描述。
/// </summary>
public sealed partial class AiDiagnosisViewModel : ObservableObject
{
    private readonly ILlmDiagnosticService? _service;
    private readonly IUiSettingsStore? _settings;
    private readonly ISecretProtector? _secrets;
    private readonly Func<string, string> _text;

    /// <summary>预览里那份提示词（Prepare 生成，Send 原样发出，保证"看到的=发出去的"）。</summary>
    private string _prompt = string.Empty;

    /// <param name="service">LLM 客户端；为 null 时面板只能预览，发送会提示未接线。</param>
    /// <param name="settings">配置读写；为 null 时配置只在内存里活着。</param>
    /// <param name="secrets">密钥加密；为 null 时拒绝把 Key 写进设置文件。</param>
    /// <param name="text">文案解析（键 → 译文）；为 null 时用源码中文。</param>
    public AiDiagnosisViewModel(
        ILlmDiagnosticService? service = null,
        IUiSettingsStore? settings = null,
        ISecretProtector? secrets = null,
        Func<string, string>? text = null)
    {
        _service = service;
        _settings = settings;
        _secrets = secrets;
        _text = text ?? (static key => key);
        LoadConfiguration();
        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
        SaveConfigurationCommand = new RelayCommand(SaveConfiguration);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    /// <summary>面板是否展开。</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>接口地址（OpenAI 兼容，带版本号的 base，例如 https://api.deepseek.com/v1）。</summary>
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    /// <summary>模型名。</summary>
    [ObservableProperty]
    private string _model = string.Empty;

    /// <summary>API Key（界面里是明文输入框，落盘前一定过 <see cref="ISecretProtector"/>）。</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    /// <summary>正在等模型回话。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>状态/错误提示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusText;

    /// <summary>预览文本：用户点发送前看到的就是它，也是真正发出去的内容。</summary>
    [ObservableProperty]
    private string _previewText = string.Empty;

    /// <summary>模型给的一句话结论。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _summary;

    /// <summary>严重度（low/medium/high）。</summary>
    [ObservableProperty]
    private string? _severityText;

    /// <summary>按置信度降序的假设。</summary>
    public ObservableCollection<AiHypothesisViewModel> Hypotheses { get; } = new();

    /// <summary>模型还需要的信息。</summary>
    public ObservableCollection<string> NeedMoreInfo { get; } = new();

    /// <summary>已经有诊断结果。</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(Summary);

    /// <summary>有状态提示要显示。</summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>配置齐全且客户端已接线（否则发送按钮禁用/提示）。</summary>
    public bool CanSend => _service is not null && !IsBusy && !string.IsNullOrWhiteSpace(_prompt);

    /// <summary>发送命令。</summary>
    public IAsyncRelayCommand SendCommand { get; }

    /// <summary>保存配置命令。</summary>
    public IRelayCommand SaveConfigurationCommand { get; }

    /// <summary>关闭面板命令。</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>
    /// 为一窗日志准备预览（不发送）。<paramref name="redactor"/> 在这一步就生效：
    /// 预览里看到的是脱敏后的文本，发出去的也一定是同一份。
    /// </summary>
    public void Prepare(
        IReadOnlyList<LogEvent> events,
        string? instanceName,
        string? gameVersion,
        string channel,
        ILogRedactor? redactor = null)
    {
        _prompt = DiagnosticPromptBuilder.Build(events, instanceName, gameVersion, channel, redactor);
        PreviewText = _prompt;
        Summary = null;
        SeverityText = null;
        Hypotheses.Clear();
        NeedMoreInfo.Clear();
        StatusText = _service is null
            ? _text("AI 客户端还没接线：可以先看预览，发送不可用。")
            : null;
        OnPropertyChanged(nameof(CanSend));
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>把界面上的配置写回设置（Key 加密；加密不可用时宁可不存）。</summary>
    public void SaveConfiguration()
    {
        if (_settings is null)
        {
            StatusText = _text("设置存储还没接线，配置只在本次运行有效。");
            return;
        }

        var protectedKey = _secrets?.Protect(ApiKey);
        if (_secrets is not null && !string.IsNullOrEmpty(ApiKey) && protectedKey is null)
        {
            StatusText = _text("这台机器上无法安全加密 API Key，已取消保存（本次运行仍可用）。");
            return;
        }

        _settings.Save(_settings.Current with
        {
            AiBaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl.Trim(),
            AiModel = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim(),
            AiApiKeyProtected = protectedKey,
        });
    }

    private async Task SendAsync()
    {
        if (_service is null)
        {
            StatusText = _text("AI 客户端还没接线。");
            return;
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusText = _text("先填好接口地址、模型和 API Key。");
            return;
        }

        IsBusy = true;
        StatusText = _text("正在等模型回话……");
        try
        {
            SaveConfiguration();
            var request = new LlmDiagnosisRequest
            {
                BaseUrl = BaseUrl.Trim(),
                Model = Model.Trim(),
                ApiKey = ApiKey.Trim(),
                Prompt = _prompt,
            };

            var result = await _service.DiagnoseAsync(request, CancellationToken.None).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                StatusText = result.Message ?? _text("诊断失败，稍后再试。");
                return;
            }

            var diagnosis = result.Value;
            Summary = diagnosis.Summary;
            SeverityText = diagnosis.Severity;
            Hypotheses.Clear();
            foreach (var hypothesis in diagnosis.Hypotheses)
            {
                Hypotheses.Add(new AiHypothesisViewModel(hypothesis));
            }

            NeedMoreInfo.Clear();
            foreach (var item in diagnosis.NeedMoreInfo)
            {
                NeedMoreInfo.Add(item);
            }

            StatusText = Hypotheses.Count == 0
                ? _text("模型没有给出可执行的假设，试试缩小日志范围再发一次。")
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StatusText = _text("网络不通或超时：检查接口地址和代理后重试。");
        }
        finally
        {
            IsBusy = false;
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    private void LoadConfiguration()
    {
        var current = _settings?.Current;
        if (current is null)
        {
            return;
        }

        BaseUrl = current.AiBaseUrl ?? string.Empty;
        Model = current.AiModel ?? string.Empty;
        ApiKey = _secrets?.Unprotect(current.AiApiKeyProtected) ?? string.Empty;
    }

    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
}

/// <summary>一条模型假设（界面显示用）。</summary>
public sealed class AiHypothesisViewModel
{
    /// <summary>低于这个置信度的假设在界面上折叠成一句话。</summary>
    public const double LowConfidenceThreshold = 0.5d;

    /// <summary>包一条 <see cref="LlmHypothesis"/>。</summary>
    public AiHypothesisViewModel(LlmHypothesis hypothesis)
    {
        ArgumentNullException.ThrowIfNull(hypothesis);
        Cause = hypothesis.Cause;
        Confidence = hypothesis.Confidence;
        EvidenceSeq = hypothesis.EvidenceSeq;
        Explain = hypothesis.Explain;
        Fix = hypothesis.Fix;
        ConfidenceText = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{hypothesis.Confidence * 100d:0}%");
        EvidenceText = hypothesis.EvidenceSeq.Count == 0
            ? "无"
            : string.Join(", ", hypothesis.EvidenceSeq.Select(seq => "#" + seq.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>原因。</summary>
    public string Cause { get; }

    /// <summary>置信度 0–1。</summary>
    public double Confidence { get; }

    /// <summary>置信度文本（"87%"）。</summary>
    public string ConfidenceText { get; }

    /// <summary>证据行号（点一下能跳回日志）。</summary>
    public IReadOnlyList<int> EvidenceSeq { get; }

    /// <summary>证据文本（"#12, #45"）。</summary>
    public string EvidenceText { get; }

    /// <summary>判断依据。</summary>
    public string Explain { get; }

    /// <summary>下一步动作。</summary>
    public string Fix { get; }

    /// <summary>置信度偏低（界面折叠）。</summary>
    public bool IsLowConfidence => Confidence < LowConfidenceThreshold;
}
