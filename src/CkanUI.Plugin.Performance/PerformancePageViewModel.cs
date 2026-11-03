using System.Globalization;
using System.Threading;
using CkanUI.Plugin.Performance.Telemetry;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Performance;

/// <summary>
/// 性能页签的视图模型（契约 docs/design/performance-tab.md 的 GUI 侧）。
/// </summary>
/// <remarks>
/// <para>职责边界：本类只做"把一轮遥测快照投影成可绑定的行"和"把按钮变成一条命令文件"。
/// 磁盘读写在 <see cref="KppTelemetryClient"/> / <see cref="KppCommandClient"/> 里，两者都在后台线程跑并有自己的 try/catch，
/// 本类拿到的永远是一个已经算好的结果或一段可显示的原因。</para>
/// <para>线程纪律：遥测事件来自定时器线程，统一用构造时捕获的 <see cref="SynchronizationContext"/> 封送回 UI 线程再改属性；
/// 拿不到上下文（单测/设计时）就直接改——那时本来就没有 UI 线程。</para>
/// <para>空态与错误态是分开的：没选实例 / Mod 未安装 / 在线但无数据 / 契约不兼容 / 数据陈旧各有自己的文案，
/// 页面不会用一份空表格假装一切正常。</para>
/// </remarks>
public sealed class PerformancePageViewModel : PerformanceObservable, IPluginPage, IDisposable
{
    /// <summary>缺失值的统一渲染文本（契约 §3：缺字段是 N/A，不是 0）。</summary>
    public const string NotAvailable = "N/A";

    private const string HotspotsPlugins = "plugins";
    private const string HotspotsModules = "modules";
    private const string HotspotsParts = "parts";
    private const string HotspotsAssemblies = "assemblies";

    private readonly IInstanceService? _instances;
    private readonly KppTelemetryClient _telemetry;
    private readonly KppCommandClient _commandClient;
    private readonly SynchronizationContext? _uiContext;
    private readonly PerformanceAsyncCommand[] _buttons;

    private bool _disposed;
    private string _lastRebuildSignature = string.Empty;
    private string? _statusSignature;
    private double? _padderMaxMb;

    /// <summary>无参构造：设计时预览与单测用（没有实例服务时页面显示"未选择 KSP 实例"）。</summary>
    public PerformancePageViewModel()
        : this(null)
    {
    }

    /// <summary>宿主构造（<c>ActivatorUtilities</c> 会注入 <see cref="IInstanceService"/>）。</summary>
    /// <param name="instances">实例服务；为 <c>null</c> 时本页退化为"未选择实例"的空态。</param>
    public PerformancePageViewModel(IInstanceService? instances)
    {
        _instances = instances;
        _uiContext = SynchronizationContext.Current;

        var telemetryDir = KppPaths.ForGameDir(instances?.Current?.GameDir);
        _telemetry = new KppTelemetryClient(telemetryDir);
        _commandClient = new KppCommandClient(telemetryDir);

        ExportReportCommand = new PerformanceAsyncCommand(ExportReportAsync, CanRun);
        ResetStatsCommand = new PerformanceAsyncCommand(ResetStatsAsync, CanRun);
        PadMemoryCommand = new PerformanceAsyncCommand(PadMemoryAsync, CanRun);
        ReleasePadCommand = new PerformanceAsyncCommand(ReleasePadAsync, CanRun);
        RefreshCommand = new PerformanceAsyncCommand(RefreshAsync);
        SelectHotspotCommand = new PerformanceParameterCommand(SelectHotspot);
        _buttons = new[] { ExportReportCommand, ResetStatsCommand, PadMemoryCommand, ReleasePadCommand, RefreshCommand };

        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentChanged;
        }

        _telemetry.Updated += OnTelemetryUpdated;
        ApplyInstance();
        ResetData();
        _telemetry.Start();
    }

    // ── 状态条 ────────────────────────────────────────────────────────────────

    /// <summary>状态条主文案（在线 / 数据陈旧 / Mod 未安装 …）。</summary>
    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    /// <summary>状态条原因文案（为什么不是在线、目录在哪、写失败几次）。</summary>
    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    /// <summary>状态徽标文字。</summary>
    public string StatusPill
    {
        get => _statusPill;
        private set => SetProperty(ref _statusPill, value);
    }

    /// <summary>在线（可据此给徽标上"成功"色）。</summary>
    public bool IsOnline => _state == KppTelemetryState.Online;

    /// <summary>陈旧 / 在线但无数据（警告色）。</summary>
    public bool IsWarningState =>
        _state is KppTelemetryState.Stale or KppTelemetryState.NoData or KppTelemetryState.ReadError;

    /// <summary>Mod 未安装 / 版本不带桥 / 契约不兼容（错误色）。</summary>
    public bool IsCriticalState =>
        _state is KppTelemetryState.ModMissing or KppTelemetryState.ModOutdated or KppTelemetryState.Incompatible;

    /// <summary>未选择实例（中性色）。</summary>
    public bool IsIdleState => _state == KppTelemetryState.NoInstance;

    /// <summary>有没有快照可显示：决定数据区与空态二选一。</summary>
    public bool ShowContent => _snapshot is not null && _state != KppTelemetryState.Incompatible;

    /// <summary>没有可显示的数据时展示空态卡。</summary>
    public bool ShowEmptyState => !ShowContent;

    /// <summary>空态标题。</summary>
    public string EmptyTitle
    {
        get => _emptyTitle;
        private set => SetProperty(ref _emptyTitle, value);
    }

    /// <summary>空态说明。</summary>
    public string EmptyDetail
    {
        get => _emptyDetail;
        private set => SetProperty(ref _emptyDetail, value);
    }

    // ── 页头信息 ──────────────────────────────────────────────────────────────

    /// <summary>实例名。</summary>
    public string InstanceName
    {
        get => _instanceName;
        private set => SetProperty(ref _instanceName, value);
    }

    /// <summary>实例游戏目录。</summary>
    public string InstanceGameDir
    {
        get => _instanceGameDir;
        private set => SetProperty(ref _instanceGameDir, value);
    }

    /// <summary>本页实际盯的遥测目录。</summary>
    public string TelemetryDirectoryText
    {
        get => _telemetryDirectoryText;
        private set => SetProperty(ref _telemetryDirectoryText, value);
    }

    /// <summary>KSP 版本（来自 latest.json 的 writer）。</summary>
    public string KspVersionText
    {
        get => _kspVersionText;
        private set => SetProperty(ref _kspVersionText, value);
    }

    /// <summary>KPP DLL 版本。</summary>
    public string DllVersionText
    {
        get => _dllVersionText;
        private set => SetProperty(ref _dllVersionText, value);
    }

    /// <summary>Unity 版本。</summary>
    public string UnityVersionText
    {
        get => _unityVersionText;
        private set => SetProperty(ref _unityVersionText, value);
    }

    /// <summary>桥版本（来自 status.json）。</summary>
    public string BridgeVersionText
    {
        get => _bridgeVersionText;
        private set => SetProperty(ref _bridgeVersionText, value);
    }

    /// <summary>会话（id + 已运行时长）。</summary>
    public string SessionText
    {
        get => _sessionText;
        private set => SetProperty(ref _sessionText, value);
    }

    /// <summary>场景 / 载具 / 部件数 / Mod 数。</summary>
    public string ContextText
    {
        get => _contextText;
        private set => SetProperty(ref _contextText, value);
    }

    /// <summary>暂停与时间倍率。</summary>
    public string TimeScaleText
    {
        get => _timeScaleText;
        private set => SetProperty(ref _timeScaleText, value);
    }

    /// <summary>快照序号。</summary>
    public string SeqText
    {
        get => _seqText;
        private set => SetProperty(ref _seqText, value);
    }

    /// <summary>数据陈旧度。</summary>
    public string AgeText
    {
        get => _ageText;
        private set => SetProperty(ref _ageText, value);
    }

    /// <summary>发布时刻（本地时间）。</summary>
    public string UpdatedAtText
    {
        get => _updatedAtText;
        private set => SetProperty(ref _updatedAtText, value);
    }

    /// <summary>页头键值行（实例 / 版本 / 会话 / 上下文 / 陈旧度 / 遥测目录）。</summary>
    /// <remarks>每次重建时整体生成：这些值来自同一份快照，逐字段增量通知只会让视图半新半旧。</remarks>
    public IReadOnlyList<PerformanceKeyValueRow> HeaderRows => new[]
    {
        new PerformanceKeyValueRow("实例", InstanceName),
        new PerformanceKeyValueRow("游戏目录", InstanceGameDir),
        new PerformanceKeyValueRow("KSP 版本", KspVersionText),
        new PerformanceKeyValueRow("KPP DLL", DllVersionText),
        new PerformanceKeyValueRow("Unity", UnityVersionText),
        new PerformanceKeyValueRow("桥版本", BridgeVersionText),
        new PerformanceKeyValueRow("会话", SessionText),
        new PerformanceKeyValueRow("上下文", ContextText),
        new PerformanceKeyValueRow("运行状态", TimeScaleText),
        new PerformanceKeyValueRow("快照序号", SeqText),
        new PerformanceKeyValueRow("数据陈旧度", AgeText),
        new PerformanceKeyValueRow("更新时间", UpdatedAtText),
        new PerformanceKeyValueRow("遥测目录", TelemetryDirectoryText),
    };

    /// <summary>桥健康度键值行（写失败、命令计数等）。</summary>
    public IReadOnlyList<PerformanceKeyValueRow> StatusRows
    {
        get => _statusRows;
        private set => SetProperty(ref _statusRows, value);
    }

    // ── 实时指标 ──────────────────────────────────────────────────────────────

    /// <summary>五个实时指标块（FPS / 帧耗时 / 1% Low / PTR / 帧抖动）。</summary>
    public IReadOnlyList<PerformanceMetricRow> Metrics
    {
        get => _metrics;
        private set => SetProperty(ref _metrics, value);
    }

    /// <summary>时间线摘要（窗口与帧数）。</summary>
    public string TimelineSummaryText
    {
        get => _timelineSummaryText;
        private set => SetProperty(ref _timelineSummaryText, value);
    }

    // ── 分解条 ────────────────────────────────────────────────────────────────

    /// <summary>分解条（模块 / 插件 / 物理 / 渲染残差 / 相机 / TUFX / GC）。</summary>
    public IReadOnlyList<PerformanceBreakdownRow> BreakdownRows
    {
        get => _breakdownRows;
        private set => SetProperty(ref _breakdownRows, value);
    }

    /// <summary>分解合计（已知段之和）。</summary>
    public string BreakdownTotalText
    {
        get => _breakdownTotalText;
        private set => SetProperty(ref _breakdownTotalText, value);
    }

    // ── GPU ──────────────────────────────────────────────────────────────────

    /// <summary>GPU 计时是否可用。</summary>
    public bool GpuAvailable
    {
        get => _gpuAvailable;
        private set => SetProperty(ref _gpuAvailable, value);
    }

    /// <summary>GPU 不可用时的原因文案（available=false 时必须给原因，不画空图）。</summary>
    public string GpuStatusText
    {
        get => _gpuStatusText;
        private set => SetProperty(ref _gpuStatusText, value);
    }

    /// <summary>五段耗时行（不可用时为空）。</summary>
    public IReadOnlyList<PerformanceGpuStageRow> GpuStageRows
    {
        get => _gpuStageRows;
        private set => SetProperty(ref _gpuStageRows, value);
    }

    /// <summary>GPU 括号段（相机 / 事件归属）。</summary>
    public IReadOnlyList<PerformanceKeyValueRow> GpuBracketRows
    {
        get => _gpuBracketRows;
        private set => SetProperty(ref _gpuBracketRows, value);
    }

    /// <summary>GPU 统计脚注（未归因 / 发布帧数 / 无效段帧数）。</summary>
    public string GpuNoteText
    {
        get => _gpuNoteText;
        private set => SetProperty(ref _gpuNoteText, value);
    }

    // ── 热点榜 ────────────────────────────────────────────────────────────────

    /// <summary>四个页签（插件 / 模块 / 部件 / 程序集），带条数。</summary>
    public IReadOnlyList<PerformanceHotspotTab> HotspotTabs
    {
        get => _hotspotTabs;
        private set => SetProperty(ref _hotspotTabs, value);
    }

    /// <summary>当前热点榜页签键。</summary>
    public string SelectedHotspotKey
    {
        get => _selectedHotspotKey;
        private set => SetProperty(ref _selectedHotspotKey, value);
    }

    /// <summary>当前页签的热点行。</summary>
    public IReadOnlyList<PerformanceHotspotRow> HotspotRows
    {
        get => _hotspotRows;
        private set
        {
            if (SetProperty(ref _hotspotRows, value))
            {
                Raise(nameof(HasHotspotRows));
            }
        }
    }

    /// <summary>当前页签是否有热点条目（视图据此显示空态文案）。</summary>
    public bool HasHotspotRows => _hotspotRows.Count > 0;

    /// <summary>热点榜说明（当前页签 + 条数）。</summary>
    public string HotspotHeaderText
    {
        get => _hotspotHeaderText;
        private set => SetProperty(ref _hotspotHeaderText, value);
    }

    /// <summary>当前页签没有条目时的提示。</summary>
    public string HotspotEmptyText
    {
        get => _hotspotEmptyText;
        private set => SetProperty(ref _hotspotEmptyText, value);
    }

    // ── 诊断卡 ────────────────────────────────────────────────────────────────

    /// <summary>是否有诊断内容。</summary>
    public bool HasDiagnostics
    {
        get => _hasDiagnostics;
        private set => SetProperty(ref _hasDiagnostics, value);
    }

    /// <summary>诊断标题。</summary>
    public string DiagnosticTitle
    {
        get => _diagnosticTitle;
        private set => SetProperty(ref _diagnosticTitle, value);
    }

    /// <summary>诊断描述。</summary>
    public string DiagnosticDescription
    {
        get => _diagnosticDescription;
        private set => SetProperty(ref _diagnosticDescription, value);
    }

    /// <summary>诊断建议。</summary>
    public string DiagnosticAdvice
    {
        get => _diagnosticAdvice;
        private set => SetProperty(ref _diagnosticAdvice, value);
    }

    /// <summary>诊断严重度：ok / warn / critical / unknown。</summary>
    public string DiagnosticSeverity
    {
        get => _diagnosticSeverity;
        private set => SetProperty(ref _diagnosticSeverity, value);
    }

    /// <summary>诊断问题条目。</summary>
    public IReadOnlyList<PerformanceCulpritRow> DiagnosticCulprits
    {
        get => _diagnosticCulprits;
        private set => SetProperty(ref _diagnosticCulprits, value);
    }

    /// <summary>诊断检查项。</summary>
    public IReadOnlyList<PerformanceCheckRow> DiagnosticChecks
    {
        get => _diagnosticChecks;
        private set => SetProperty(ref _diagnosticChecks, value);
    }

    /// <summary>严重度是否为 ok（视图配色）。</summary>
    public bool SeverityOk => DiagnosticSeverity == "ok";

    /// <summary>严重度是否为 warn。</summary>
    public bool SeverityWarn => DiagnosticSeverity == "warn";

    /// <summary>严重度是否为 critical。</summary>
    public bool SeverityCritical => DiagnosticSeverity == "critical";

    // ── 内存 ─────────────────────────────────────────────────────────────────

    /// <summary>堆 / 预留 / 已用 / GC 行。</summary>
    public IReadOnlyList<PerformanceKeyValueRow> MemoryRows
    {
        get => _memoryRows;
        private set => SetProperty(ref _memoryRows, value);
    }

    /// <summary>垫高器状态行。</summary>
    public IReadOnlyList<PerformanceKeyValueRow> PadderRows
    {
        get => _padderRows;
        private set => SetProperty(ref _padderRows, value);
    }

    /// <summary>垫高器一句话状态。</summary>
    public string PadderStateText
    {
        get => _padderStateText;
        private set => SetProperty(ref _padderStateText, value);
    }

    /// <summary>资产探针状态。</summary>
    public string ProbeText
    {
        get => _probeText;
        private set => SetProperty(ref _probeText, value);
    }

    /// <summary>top allocators。</summary>
    public IReadOnlyList<PerformanceAllocatorRow> AllocatorRows
    {
        get => _allocatorRows;
        private set => SetProperty(ref _allocatorRows, value);
    }

    // ── 尖峰 ─────────────────────────────────────────────────────────────────

    /// <summary>掉帧尖峰行。</summary>
    public IReadOnlyList<PerformanceSpikeRow> SpikeRows
    {
        get => _spikeRows;
        private set => SetProperty(ref _spikeRows, value);
    }

    /// <summary>尖峰区标题（含计数）。</summary>
    public string SpikeHeaderText
    {
        get => _spikeHeaderText;
        private set => SetProperty(ref _spikeHeaderText, value);
    }

    // ── 命令 ─────────────────────────────────────────────────────────────────

    /// <summary>导出报告（export_report）。</summary>
    public PerformanceAsyncCommand ExportReportCommand { get; }

    /// <summary>重置统计（reset_stats）。</summary>
    public PerformanceAsyncCommand ResetStatsCommand { get; }

    /// <summary>垫高 Mono 堆（set_pad）。</summary>
    public PerformanceAsyncCommand PadMemoryCommand { get; }

    /// <summary>归还物理内存（release_pad）。</summary>
    public PerformanceAsyncCommand ReleasePadCommand { get; }

    /// <summary>立即重读一次遥测（本地刷新，不发命令）。</summary>
    public PerformanceAsyncCommand RefreshCommand { get; }

    /// <summary>切换热点榜页签。</summary>
    public PerformanceParameterCommand SelectHotspotCommand { get; }

    /// <summary>命令是否在途（按钮同时禁用，避免连点）。</summary>
    public bool IsCommandBusy
    {
        get => _isCommandBusy;
        private set
        {
            if (!SetProperty(ref _isCommandBusy, value))
            {
                return;
            }

            Raise(nameof(CanRunCommands));
            foreach (var button in _buttons)
            {
                button.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>命令按钮可用性（在线 + 空闲）。</summary>
    public bool CanRunCommands => !IsCommandBusy;

    /// <summary>垫高目标（MB）。KPP 的 padder 上限已知时会被夹到上限以内。</summary>
    public double PadTargetMb
    {
        get => _padTargetMb;
        set => SetProperty(ref _padTargetMb, value);
    }

    /// <summary>上一条命令成功文案。</summary>
    public string CommandMessage
    {
        get => _commandMessage;
        private set
        {
            if (SetProperty(ref _commandMessage, value))
            {
                Raise(nameof(HasCommandMessage));
            }
        }
    }

    /// <summary>上一条命令失败文案。</summary>
    public string CommandError
    {
        get => _commandError;
        private set
        {
            if (SetProperty(ref _commandError, value))
            {
                Raise(nameof(HasCommandError));
            }
        }
    }

    /// <summary>是否有成功文案。</summary>
    public bool HasCommandMessage => _commandMessage.Length > 0;

    /// <summary>是否有失败文案。</summary>
    public bool HasCommandError => _commandError.Length > 0;

    // ── 生命周期 ─────────────────────────────────────────────────────────────

    /// <summary>视图挂进可视树时开始轮询（宿主可能缓存页面实例，所以由视图告知"现在有人在看"）。</summary>
    public void StartPolling()
    {
        if (!_disposed)
        {
            _telemetry.Start();
        }
    }

    /// <summary>视图离开可视树时停轮询（重新挂上会恢复）。</summary>
    public void StopPolling()
    {
        if (!_disposed)
        {
            _telemetry.Stop();
        }
    }

    /// <summary>停订阅、停定时器（宿主卸载插件页时调用；重复调用安全）。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _telemetry.Updated -= OnTelemetryUpdated;
        _telemetry.Dispose();
        if (_instances is not null)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
        }
    }

    // ── 内部实现 ─────────────────────────────────────────────────────────────

    private void OnCurrentChanged(InstanceInfo? instance)
    {
        if (_disposed)
        {
            return;
        }

        ApplyInstance();
        ResetData();
        _telemetry.Poll();
    }

    private void ApplyInstance()
    {
        var current = _instances?.Current;
        var telemetryDir = KppPaths.ForGameDir(current?.GameDir);
        _telemetry.Retarget(telemetryDir);
        _commandClient.Retarget(telemetryDir);

        InstanceName = current?.Name ?? NotAvailable;
        InstanceGameDir = string.IsNullOrWhiteSpace(current?.GameDir) ? NotAvailable : current!.GameDir;
        TelemetryDirectoryText = telemetryDir ?? NotAvailable;
        Raise(nameof(HeaderRows));
    }

    private void OnTelemetryUpdated(KppTelemetryUpdate update)
    {
        var context = _uiContext;
        if (context is null)
        {
            ApplyUpdate(update);
            return;
        }

        // 载荷必须是 VM 本身（Post 的 state 参数），不是 update——否则回调里强转会炸在 UI 线程。
        context.Post(static state => ((PerformancePageViewModel)state!).ApplyUpdateFromPost(), this);
    }

    // Post 的载荷是 update，但 Post(object?) 只能传一个对象；这里用"最近一轮"避免闭包分配，
    // 代价是极端并发下可能合并两轮——性能页 1Hz，合并只影响一帧的显示延迟。
    private void ApplyUpdateFromPost()
    {
        var latest = _telemetry.Latest;
        if (latest is not null)
        {
            ApplyUpdate(latest);
        }
    }

    private void ApplyUpdate(KppTelemetryUpdate update)
    {
        if (_disposed)
        {
            return;
        }

        SetState(update);
        _state = update.State;
        _snapshot = update.State == KppTelemetryState.Incompatible ? null : update.Snapshot;

        RaiseAll(
            nameof(IsOnline),
            nameof(IsWarningState),
            nameof(IsCriticalState),
            nameof(IsIdleState),
            nameof(ShowContent),
            nameof(ShowEmptyState));

        var snapshot = _snapshot;
        if (snapshot is null)
        {
            return;
        }

        AgeText = FormatAge(update.AgeMs);
        UpdatedAtText = snapshot.PublishedUnixMs is { } published && published > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)published).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : NotAvailable;
        SeqText = Format(snapshot.Seq, "0") is { } seqText && snapshot.Seq is not null ? "#" + seqText : NotAvailable;

        var signature = string.Create(
            CultureInfo.InvariantCulture,
            $"{snapshot.Seq}|{snapshot.PublishedUnixMs}|{_selectedHotspotKey}");
        if (string.Equals(signature, _lastRebuildSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastRebuildSignature = signature;
        RebuildFromSnapshot(snapshot);
    }

    private void SetState(KppTelemetryUpdate update)
    {
        var stateSignature = update.State + "|" + update.Detail;
        if (!string.Equals(stateSignature, _statusSignature, StringComparison.Ordinal))
        {
            _statusSignature = stateSignature;
            StatusTitle = update.State switch
            {
                KppTelemetryState.NoInstance => "未选择 KSP 实例",
                KppTelemetryState.ModMissing => "未安装 KPP 遥测 Mod",
                KppTelemetryState.ModOutdated => "KPP 已安装，但版本不带遥测桥",
                KppTelemetryState.NoData => "KPP 在线，暂无数据",
                KppTelemetryState.Stale => "数据已陈旧",
                KppTelemetryState.Online => "遥测在线",
                KppTelemetryState.Incompatible => "遥测契约不兼容",
                _ => "读取遥测失败",
            };
            StatusPill = update.State switch
            {
                KppTelemetryState.Online => "在线",
                KppTelemetryState.Stale => "陈旧",
                KppTelemetryState.NoData => "无数据",
                KppTelemetryState.ModMissing => "未安装",
                KppTelemetryState.ModOutdated => "待升级",
                KppTelemetryState.Incompatible => "不兼容",
                KppTelemetryState.NoInstance => "无实例",
                _ => "读失败",
            };
            StatusDetail = update.Detail ?? BuildDefaultDetail(update.State, update.TelemetryDirectory);
            EmptyTitle = StatusTitle;
            EmptyDetail = BuildEmptyDetail(update);
        }

        StatusRows = BuildStatusRows(update.Status, update.TelemetryDirectory);
        BridgeVersionText = update.Status?.BridgeVersion is { Length: > 0 } bridge ? bridge : NotAvailable;
    }

    private static string BuildDefaultDetail(KppTelemetryState state, string? telemetryDir) => state switch
    {
        KppTelemetryState.NoInstance => "先在「实例」页选择一个 KSP 实例，本页才能定位遥测目录。",
        KppTelemetryState.ModMissing => "遥测目录不存在：" + (telemetryDir ?? NotAvailable),
        KppTelemetryState.ModOutdated => "找到了 KPP 本体，但没有遥测目录：" + (telemetryDir ?? NotAvailable),
        KppTelemetryState.NoData => "latest.json 尚不存在：进游戏并加载存档后才会出现数据。",
        KppTelemetryState.Stale => "发布时刻超过 3 倍发布周期：游戏已暂停或已退出。",
        KppTelemetryState.Online => "数据新鲜。",
        _ => "见下方原因。",
    };

    private static string BuildEmptyDetail(KppTelemetryUpdate update) => update.State switch
    {
        KppTelemetryState.NoInstance => "未选择实例时不显示任何指标——不会用 0 冒充遥测数据。",
        KppTelemetryState.ModMissing =>
            "把 KSPPerformanceProfiler 装进实例的 GameData 目录。期望路径：" + (update.TelemetryDirectory ?? NotAvailable),
        KppTelemetryState.ModOutdated =>
            (update.Detail ?? "安装的 KPP 不带文件桥。")
            + " 升级到 1.2.0+ 后重启游戏，本页会自动出现数据；期望路径："
            + (update.TelemetryDirectory ?? NotAvailable),
        KppTelemetryState.Incompatible =>
            update.Detail ?? "本案只支持 kpp.telemetry/1；主版本不同（如 2）时不尝试解析。",
        KppTelemetryState.NoData =>
            (update.Detail ?? "还没有数据") + "；status.json 能说明「在线但没数据」的原因（磁盘满 / 权限 / 路径错）。",
        _ => update.Detail ?? "本轮没有可显示的遥测。",
    };

    private static IReadOnlyList<PerformanceKeyValueRow> BuildStatusRows(KppStatus? status, string? telemetryDir)
    {
        if (status is null)
        {
            return new[]
            {
                new PerformanceKeyValueRow("status.json", "未读到（桥健康度不可用）"),
                new PerformanceKeyValueRow("遥测目录", telemetryDir ?? NotAvailable),
            };
        }

        return new[]
        {
            new PerformanceKeyValueRow("写方存活", status.WriterAlive is null ? NotAvailable : status.WriterAlive.Value ? "是" : "否"),
            new PerformanceKeyValueRow("写入失败", Format(status.WriteFailures, "0") is { } w ? w + " 次" : NotAvailable),
            new PerformanceKeyValueRow("命令 见/成/败", string.Create(
                CultureInfo.InvariantCulture,
                $"{Format(status.CommandsSeen, "0") ?? NotAvailable} / {Format(status.CommandsApplied, "0") ?? NotAvailable} / {Format(status.CommandsFailed, "0") ?? NotAvailable}")),
            new PerformanceKeyValueRow("桥版本", status.BridgeVersion ?? NotAvailable),
            new PerformanceKeyValueRow("发布周期", status.PublishIntervalMs is { } interval ? Format(interval / 1000d, "0.0") + " s" : NotAvailable),
            new PerformanceKeyValueRow("写方 PID", Format(status.Pid, "0") ?? NotAvailable),
            new PerformanceKeyValueRow("遥测目录", status.TelemetryDirectory ?? telemetryDir ?? NotAvailable),
        };
    }

    private void ResetData()
    {
        _lastRebuildSignature = string.Empty;
        _statusSignature = null;
        _snapshot = null;
        KspVersionText = NotAvailable;
        DllVersionText = NotAvailable;
        UnityVersionText = NotAvailable;
        SessionText = NotAvailable;
        ContextText = NotAvailable;
        TimeScaleText = NotAvailable;
        SeqText = NotAvailable;
        AgeText = NotAvailable;
        UpdatedAtText = NotAvailable;
        TimelineSummaryText = NotAvailable;
        Metrics = Array.Empty<PerformanceMetricRow>();
        BreakdownRows = Array.Empty<PerformanceBreakdownRow>();
        BreakdownTotalText = NotAvailable;
        GpuAvailable = false;
        GpuStatusText = NotAvailable;
        GpuStageRows = Array.Empty<PerformanceGpuStageRow>();
        GpuBracketRows = Array.Empty<PerformanceKeyValueRow>();
        GpuNoteText = NotAvailable;
        HotspotRows = Array.Empty<PerformanceHotspotRow>();
        HotspotTabs = Array.Empty<PerformanceHotspotTab>();
        HotspotHeaderText = "暂无热点";
        HotspotEmptyText = "还没有热点数据。";
        HasDiagnostics = false;
        DiagnosticTitle = NotAvailable;
        DiagnosticDescription = NotAvailable;
        DiagnosticAdvice = NotAvailable;
        DiagnosticSeverity = "unknown";
        DiagnosticCulprits = Array.Empty<PerformanceCulpritRow>();
        DiagnosticChecks = Array.Empty<PerformanceCheckRow>();
        MemoryRows = Array.Empty<PerformanceKeyValueRow>();
        PadderRows = Array.Empty<PerformanceKeyValueRow>();
        PadderStateText = NotAvailable;
        ProbeText = NotAvailable;
        AllocatorRows = Array.Empty<PerformanceAllocatorRow>();
        SpikeRows = Array.Empty<PerformanceSpikeRow>();
        SpikeHeaderText = "掉帧尖峰";
        RaiseAll(nameof(SeverityOk), nameof(SeverityWarn), nameof(SeverityCritical), nameof(ShowContent), nameof(ShowEmptyState), nameof(HeaderRows));
    }

    private void RebuildFromSnapshot(KppTelemetrySnapshot snapshot)
    {
        KspVersionText = snapshot.Writer?.KspVersion ?? NotAvailable;
        DllVersionText = snapshot.Writer?.DllVersion ?? NotAvailable;
        UnityVersionText = snapshot.Writer?.UnityVersion ?? NotAvailable;
        SessionText = snapshot.Session is { } session
            ? string.Create(CultureInfo.InvariantCulture, $"{session.Id ?? NotAvailable} · 已运行 {FormatDuration(session.UptimeSec)}")
            : NotAvailable;
        ContextText = snapshot.Context is { } context
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{context.Scene ?? NotAvailable} · {context.VesselName ?? "无载具"} · {Format(context.PartCount, "0") ?? NotAvailable} 部件 · {Format(context.ModCount, "0") ?? NotAvailable} Mod")
            : NotAvailable;
        TimeScaleText = snapshot.Context is { } ctx
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(ctx.Paused == true ? "已暂停" : "运行中")} · 时间倍率 {Format(ctx.TimeScale, "0.0") ?? NotAvailable}")
            : NotAvailable;

        TimelineSummaryText = snapshot.Timeline is { } timeline
            ? string.Create(CultureInfo.InvariantCulture, $"{timeline.Frames.Count} 帧 · 窗口 {Format(timeline.Window, "0.0") ?? NotAvailable}s")
            : "时间线不可用";

        Metrics = BuildMetrics(snapshot);
        BuildBreakdown(snapshot);
        BuildGpu(snapshot);
        BuildHotspots(snapshot);
        BuildDiagnostics(snapshot);
        BuildMemory(snapshot);
        BuildSpikes(snapshot);
        Raise(nameof(HeaderRows));
    }

    private static IReadOnlyList<PerformanceMetricRow> BuildMetrics(KppTelemetrySnapshot snapshot)
    {
        var frames = snapshot.Timeline?.Frames ?? Array.Empty<KppTimelineFrame>();
        var fpsTrend = Series(frames, static f => f.Fps);
        var frameTrend = Series(frames, static f => f.FrameMs);
        var jitterTrend = DiffSeries(frames);
        var frame = snapshot.Frame;

        return new[]
        {
            new PerformanceMetricRow(
                "FPS",
                Format(frame?.Fps, "0.0") ?? NotAvailable,
                "平均 " + (Format(frame?.AvgFps, "0.0") ?? NotAvailable) + " · dt " + (Format(frame?.Dt, "0.000") ?? NotAvailable),
                fpsTrend,
                fpsTrend.Count > 1,
                "走势来源 timeline[].fps"),
            new PerformanceMetricRow(
                "帧耗时",
                Ms(frame?.FrameMs),
                "平均 " + Ms(frame?.AvgFrameMs) + " · 物理步 " + (Format(frame?.PhysSteps, "0") ?? NotAvailable),
                frameTrend,
                frameTrend.Count > 1,
                "走势来源 timeline[].frameMs"),
            new PerformanceMetricRow(
                "1% Low",
                Format(frame?.OnePercentLowFps, "0.0") ?? NotAvailable,
                "0.1% Low " + (Format(frame?.PointOnePercentLowFps, "0.0") ?? NotAvailable),
                Array.Empty<double>(),
                false,
                "契约未提供该指标的序列，不画走势"),
            new PerformanceMetricRow(
                "PTR",
                Format(frame?.Ptr, "0.000") ?? NotAvailable,
                "契约字段 frame.ptr（由 KPP 计算，本页不二次解释）",
                Array.Empty<double>(),
                false,
                "契约未提供该指标的序列，不画走势"),
            new PerformanceMetricRow(
                "帧抖动",
                Ms(frame?.JitterMs),
                "相邻帧耗时差分的近期均值",
                jitterTrend,
                jitterTrend.Count > 1,
                "走势来源 timeline[].frameMs 的一阶差分"),
        };
    }

    private void BuildBreakdown(KppTelemetrySnapshot snapshot)
    {
        var breakdown = snapshot.Breakdown;
        if (breakdown is null)
        {
            BreakdownRows = Array.Empty<PerformanceBreakdownRow>();
            BreakdownTotalText = "分解不可用（latest.json 无 breakdownMs）";
            return;
        }

        var tufx = Sum(breakdown.TufxPostFx, breakdown.TufxCpu);
        var segments = new (string Title, double? Ms)[]
        {
            ("模块脚本", breakdown.ModuleScripts),
            ("插件脚本", breakdown.PluginScripts),
            ("物理", breakdown.Physx),
            ("渲染残差", breakdown.RenderResidual),
            ("相机", breakdown.Camera),
            ("TUFX", tufx),
            ("GC", breakdown.Gc),
        };

        var total = segments.Sum(static s => s.Ms ?? 0d);
        var rows = new List<PerformanceBreakdownRow>(segments.Length);
        foreach (var (title, ms) in segments)
        {
            var fraction = ms is { } value && total > 0 ? Math.Clamp(value / total, 0d, 1d) : 0d;
            rows.Add(new PerformanceBreakdownRow(
                title,
                Ms(ms),
                ms is not null && total > 0 ? Format(ms.Value / total * 100d, "0.0") + " %" : NotAvailable,
                fraction));
        }

        BreakdownRows = rows;
        BreakdownTotalText = total > 0
            ? string.Create(CultureInfo.InvariantCulture, $"已知段合计 {total:0.0} ms（不含 GPU 与未归因部分）")
            : "分解合计不可用";
    }

    private void BuildGpu(KppTelemetrySnapshot snapshot)
    {
        var gpu = snapshot.Gpu;
        if (gpu is null)
        {
            GpuAvailable = false;
            GpuStatusText = "本快照没有 gpu 字段（KPP 未启用 GPU 计时）";
            GpuStageRows = Array.Empty<PerformanceGpuStageRow>();
            GpuBracketRows = Array.Empty<PerformanceKeyValueRow>();
            GpuNoteText = NotAvailable;
            return;
        }

        GpuAvailable = gpu.Available == true;
        GpuStatusText = gpu.Available == true
            ? "GPU 五段计时可用"
            : gpu.StatusText is { Length: > 0 } reason ? reason : "GPU 计时不可用（available=false，原因未提供）";

        if (gpu.Available != true)
        {
            // available=false：不画空图，五段一律 N/A，只用原因文案交代（契约 §3.1）。
            GpuStageRows = new[]
            {
                new PerformanceGpuStageRow("depth", NotAvailable, 0, false),
                new PerformanceGpuStageRow("opaque", NotAvailable, 0, false),
                new PerformanceGpuStageRow("transparent", NotAvailable, 0, false),
                new PerformanceGpuStageRow("postFx", NotAvailable, 0, false),
                new PerformanceGpuStageRow("final", NotAvailable, 0, false),
            };
            GpuBracketRows = Array.Empty<PerformanceKeyValueRow>();
            GpuNoteText = "GPU 计时不可用：五段显示 N/A，不画曲线";
            return;
        }

        var denominator = gpu.FrameSpanMs is { } span && span > 0
            ? span
            : gpu.StageTotalMs is { } stageTotal && stageTotal > 0
                ? stageTotal
                : MaxKnownStage(gpu.Stages);

        var rows = new List<PerformanceGpuStageRow>();
        foreach (var stage in gpu.Stages)
        {
            var label = StageLabel(stage.Key);
            var fraction = stage.Ms is { } ms && denominator > 0 ? Math.Clamp(ms / denominator, 0d, 1d) : 0d;
            rows.Add(new PerformanceGpuStageRow(label, Ms(stage.Ms), fraction, stage.Valid != false && stage.Ms is not null));
        }

        GpuStageRows = rows;
        GpuBracketRows = (snapshot.GpuBrackets ?? Array.Empty<KppGpuBracket>())
            .Where(static bracket => bracket.Ms is not null)
            .Take(8)
            .Select(static bracket => new PerformanceKeyValueRow(
                (bracket.Camera ?? "相机?") + " · " + (bracket.EventName ?? "事件?"),
                Ms(bracket.Ms) + " · " + (bracket.Owner ?? NotAvailable)))
            .ToArray();

        GpuNoteText = string.Create(
            CultureInfo.InvariantCulture,
            $"未归因 {Ms(gpu.UnaccountedMs)} · 段合计 {Ms(gpu.StageTotalMs)} · 帧跨度 {Ms(gpu.FrameSpanMs)} · 发布帧 {Format(gpu.PublishedFrames, "0") ?? NotAvailable} · 无效段帧 {Format(gpu.InvalidStageFrames, "0") ?? NotAvailable}");
    }

    private void BuildHotspots(KppTelemetrySnapshot snapshot)
    {
        var hotspots = snapshot.Hotspots;
        var tabs = new[]
        {
            new PerformanceHotspotTab(HotspotsPlugins, "插件", hotspots?.Plugins.Count ?? 0, _selectedHotspotKey == HotspotsPlugins),
            new PerformanceHotspotTab(HotspotsModules, "模块", hotspots?.Modules.Count ?? 0, _selectedHotspotKey == HotspotsModules),
            new PerformanceHotspotTab(HotspotsParts, "部件", hotspots?.Parts.Count ?? 0, _selectedHotspotKey == HotspotsParts),
            new PerformanceHotspotTab(HotspotsAssemblies, "程序集", hotspots?.Assemblies.Count ?? 0, _selectedHotspotKey == HotspotsAssemblies),
        };
        HotspotTabs = tabs;

        var entries = SelectHotspotEntries(hotspots, _selectedHotspotKey);
        var label = tabs.FirstOrDefault(tab => string.Equals(tab.Key, _selectedHotspotKey, StringComparison.Ordinal))?.Label ?? "插件";
        HotspotHeaderText = string.Create(CultureInfo.InvariantCulture, $"{label} · {entries.Count} 条（按耗时降序，契约上限 24）");

        if (entries.Count == 0)
        {
            HotspotRows = Array.Empty<PerformanceHotspotRow>();
            HotspotEmptyText = "该分类没有热点条目（KPP 未采集或本帧为空）。";
            return;
        }

        HotspotEmptyText = string.Empty;
        HotspotRows = entries.Select(BuildHotspotRow).ToArray();
    }

    private static IReadOnlyList<KppHotspot> SelectHotspotEntries(KppHotspots? hotspots, string key)
    {
        if (hotspots is null)
        {
            return Array.Empty<KppHotspot>();
        }

        return key switch
        {
            HotspotsModules => hotspots.Modules,
            HotspotsParts => hotspots.Parts,
            HotspotsAssemblies => hotspots.Assemblies,
            _ => hotspots.Plugins,
        };
    }

    private static PerformanceHotspotRow BuildHotspotRow(KppHotspot hotspot)
    {
        var name = FirstNonEmpty(hotspot.Friendly, hotspot.Name, "(未命名)");
        var assembly = FirstNonEmpty(hotspot.Assembly, NotAvailable);

        var extras = new List<string>(3);
        if (hotspot.Calls is { } calls)
        {
            extras.Add(string.Create(CultureInfo.InvariantCulture, $"调用 {calls}"));
        }

        if (hotspot.BytesPerFrame is { } bytes)
        {
            extras.Add("每帧 " + FormatBytes(bytes));
        }

        if (hotspot.PeakMs is { } peak)
        {
            extras.Add("峰值 " + Ms(peak));
        }

        if (hotspot.IsTufx == true)
        {
            extras.Add("TUFX");
        }

        return new PerformanceHotspotRow(
            name,
            assembly,
            Ms(hotspot.Ms),
            hotspot.Pct is { } pct ? Format(pct, "0.0") + " %" : NotAvailable,
            string.Join(" · ", extras));
    }

    private void BuildDiagnostics(KppTelemetrySnapshot snapshot)
    {
        var diagnostics = snapshot.Diagnostics;
        if (diagnostics is null)
        {
            HasDiagnostics = false;
            DiagnosticSeverity = "unknown";
            DiagnosticTitle = "本快照没有诊断卡";
            DiagnosticDescription = NotAvailable;
            DiagnosticAdvice = NotAvailable;
            DiagnosticCulprits = Array.Empty<PerformanceCulpritRow>();
            DiagnosticChecks = Array.Empty<PerformanceCheckRow>();
            RaiseAll(nameof(SeverityOk), nameof(SeverityWarn), nameof(SeverityCritical));
            return;
        }

        HasDiagnostics = true;
        DiagnosticTitle = FirstNonEmpty(diagnostics.Title, "诊断");
        DiagnosticDescription = FirstNonEmpty(diagnostics.Description, NotAvailable);
        DiagnosticAdvice = FirstNonEmpty(diagnostics.Advice, NotAvailable);
        DiagnosticSeverity = diagnostics.Severity;
        DiagnosticCulprits = diagnostics.Culprits
            .Select(static culprit => new PerformanceCulpritRow(
                FirstNonEmpty(culprit.Source, "?"),
                FirstNonEmpty(culprit.Name, culprit.Assembly, "(未命名)"),
                Ms(culprit.Ms),
                culprit.Pct is { } pct ? Format(pct, "0.0") + " %" : NotAvailable))
            .ToArray();
        DiagnosticChecks = diagnostics.Checks
            .Select(static check => new PerformanceCheckRow(
                FirstNonEmpty(check.Label, check.Id, "检查"),
                FirstNonEmpty(check.Value, check.Detail, NotAvailable),
                check.State ?? "unknown"))
            .ToArray();
        RaiseAll(nameof(SeverityOk), nameof(SeverityWarn), nameof(SeverityCritical));
    }

    private void BuildMemory(KppTelemetrySnapshot snapshot)
    {
        var memory = snapshot.Memory;
        if (memory is null)
        {
            MemoryRows = Array.Empty<PerformanceKeyValueRow>();
            PadderRows = Array.Empty<PerformanceKeyValueRow>();
            PadderStateText = "内存段不可用";
            ProbeText = NotAvailable;
            AllocatorRows = Array.Empty<PerformanceAllocatorRow>();
            return;
        }

        MemoryRows = new[]
        {
            new PerformanceKeyValueRow("Mono 堆", Mb(memory.MonoHeapMb)),
            new PerformanceKeyValueRow("预留", Mb(memory.ReservedMb)),
            new PerformanceKeyValueRow("已用", Mb(memory.MonoUsedMb)),
            new PerformanceKeyValueRow("GC 上次", Ms(memory.GcLastMs)),
            new PerformanceKeyValueRow("GC 平均", Ms(memory.GcAvgMs)),
            new PerformanceKeyValueRow("GC 次数", Format(memory.GcCount, "0") ?? NotAvailable),
        };

        var padder = memory.Padder;
        _padderMaxMb = padder?.MaxMb;
        if (padder is null)
        {
            PadderRows = Array.Empty<PerformanceKeyValueRow>();
            PadderStateText = "垫高器状态不可用（无 padder 段）";
        }
        else
        {
            PadderRows = new[]
            {
                new PerformanceKeyValueRow("启用", padder.Enabled is null ? NotAvailable : padder.Enabled.Value ? "是" : "否"),
                new PerformanceKeyValueRow("目标", Mb(padder.TargetMb)),
                new PerformanceKeyValueRow("已垫高", Mb(padder.HeldMb)),
                new PerformanceKeyValueRow("上限", Mb(padder.MaxMb)),
                new PerformanceKeyValueRow("物理内存", Mb(padder.PhysicalMb)),
            };
            PadderStateText = padder.Enabled == true
                ? string.Create(CultureInfo.InvariantCulture, $"已垫高 {Mb(padder.HeldMb)}（目标 {Mb(padder.TargetMb)}，上限 {Mb(padder.MaxMb)}）")
                : "垫高器未启用";
        }

        var probe = memory.Probe;
        ProbeText = probe is null
            ? "资产探针不可用"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{probe.State ?? NotAvailable} · 资产 {Format(probe.AssetsScanned, "0") ?? NotAvailable} · 贴图 {Mb(probe.TexturesMb)} · 网格 {Mb(probe.MeshesMb)} · 声音 {Mb(probe.SoundsMb)} · DBTex {Mb(probe.DbTexturesMb)}");

        AllocatorRows = memory.TopAllocators
            .Take(10)
            .Select(static allocator => new PerformanceAllocatorRow(
                FirstNonEmpty(allocator.Name, "(未命名)"),
                FirstNonEmpty(allocator.Assembly, NotAvailable),
                allocator.BytesPerFrame is { } bytes ? FormatBytes(bytes) + " /帧" : NotAvailable))
            .ToArray();
    }

    private void BuildSpikes(KppTelemetrySnapshot snapshot)
    {
        var spikes = snapshot.Spikes ?? Array.Empty<KppSpike>();
        SpikeHeaderText = string.Create(CultureInfo.InvariantCulture, $"掉帧尖峰 · {spikes.Count} 条");
        SpikeRows = spikes
            .Take(8)
            .Select(static spike => new PerformanceSpikeRow(
                spike.Frame is { } frame ? "#" + frame.ToString(CultureInfo.InvariantCulture) : NotAvailable,
                Ms(spike.FrameMs),
                FirstNonEmpty(spike.Reason, "未标注原因"),
                spike.Culprits.Count == 0
                    ? "无嫌疑条目"
                    : FirstNonEmpty(spike.Culprits[0].Friendly, spike.Culprits[0].Name, "?") + " " + Ms(spike.Culprits[0].Ms)))
            .ToArray();
    }

    // ── 命令 ─────────────────────────────────────────────────────────────────

    private bool CanRun() => !IsCommandBusy;

    private void SelectHotspot(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, _selectedHotspotKey, StringComparison.Ordinal))
        {
            return;
        }

        SelectedHotspotKey = key;
        var snapshot = _snapshot;
        if (snapshot is not null)
        {
            // 页签切换只影响热点榜，但签名里带了页签键：这里清掉签名强制整页重建一次（代价可忽略）。
            _lastRebuildSignature = string.Empty;
            RebuildFromSnapshot(snapshot);
            _lastRebuildSignature = string.Create(
                CultureInfo.InvariantCulture,
                $"{snapshot.Seq}|{snapshot.PublishedUnixMs}|{key}");
        }
    }

    private async Task RefreshAsync()
    {
        TelemetryDirectoryText = _telemetry.TelemetryDirectory ?? NotAvailable;
        _telemetry.Poll();
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task ExportReportAsync()
    {
        var ack = await SendAsync("export_report", null).ConfigureAwait(true);
        ReportAck(ack);
    }

    private async Task ResetStatsAsync()
    {
        var ack = await SendAsync("reset_stats", null).ConfigureAwait(true);
        ReportAck(ack);
    }

    private async Task PadMemoryAsync()
    {
        var target = PadTargetMb;
        if (_padderMaxMb is { } max && max > 0)
        {
            target = Math.Min(target, max);
        }

        var megabytes = (long)Math.Round(Math.Max(0d, target), MidpointRounding.AwayFromZero);
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["megabytes"] = megabytes,
        };
        var ack = await SendAsync("set_pad", args).ConfigureAwait(true);
        ReportAck(ack);
    }

    private async Task ReleasePadAsync()
    {
        var ack = await SendAsync("release_pad", null).ConfigureAwait(true);
        ReportAck(ack);
    }

    private async Task<KppCommandAck> SendAsync(string op, IReadOnlyDictionary<string, object?>? args)
    {
        IsCommandBusy = true;
        CommandError = string.Empty;
        CommandMessage = string.Create(CultureInfo.InvariantCulture, $"{op} 已下发，等待 KPP 响应…");
        try
        {
            return await _commandClient.SendAsync(op, args).ConfigureAwait(true);
        }
        finally
        {
            IsCommandBusy = false;
        }
    }

    private void ReportAck(KppCommandAck ack)
    {
        if (ack.Ok)
        {
            CommandError = string.Empty;
            CommandMessage = ack.ResultPath is { Length: > 0 } path
                ? string.Create(CultureInfo.InvariantCulture, $"{ack.Op} 已执行（{ack.ElapsedMs:0} ms）：{path}")
                : string.Create(CultureInfo.InvariantCulture, $"{ack.Op} 已执行（{ack.ElapsedMs:0} ms）：{ack.Message}");
        }
        else
        {
            CommandMessage = string.Empty;
            CommandError = string.Create(CultureInfo.InvariantCulture, $"{ack.Op} 失败：{ack.Message}");
        }

        // 命令改变了游戏侧状态（垫高、重置统计），立刻重读一次，不让用户盯着旧数字。
        _telemetry.Poll();
    }

    // ── 格式化 ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<double> Series(IReadOnlyList<KppTimelineFrame> frames, Func<KppTimelineFrame, double?> selector)
    {
        if (frames.Count == 0)
        {
            return Array.Empty<double>();
        }

        var values = new List<double>(frames.Count);
        foreach (var frame in frames)
        {
            if (selector(frame) is { } value && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static IReadOnlyList<double> DiffSeries(IReadOnlyList<KppTimelineFrame> frames)
    {
        if (frames.Count < 2)
        {
            return Array.Empty<double>();
        }

        var values = new List<double>(frames.Count - 1);
        double? previous = null;
        foreach (var frame in frames)
        {
            var current = frame.FrameMs;
            if (current is null)
            {
                continue;
            }

            if (previous is { } last)
            {
                values.Add(Math.Abs(current.Value - last));
            }

            previous = current;
        }

        return values;
    }

    private static double? Sum(double? left, double? right) => left is null && right is null
        ? null
        : (left ?? 0d) + (right ?? 0d);

    private static double MaxKnownStage(IReadOnlyList<KppGpuStage> stages)
    {
        var max = 0d;
        foreach (var stage in stages)
        {
            if (stage.Ms is { } ms && ms > max)
            {
                max = ms;
            }
        }

        return max;
    }

    private static string StageLabel(string? key) => key switch
    {
        "depth" => "depth 深度",
        "opaque" => "opaque 不透明",
        "transparent" => "transparent 透明",
        "postFx" => "postFx 后处理",
        "final" => "final 最终",
        _ => key ?? "(未命名段)",
    };

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return NotAvailable;
    }

    private static string Ms(double? milliseconds) =>
        milliseconds is { } value && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value.ToString("0.00", CultureInfo.InvariantCulture) + " ms"
            : NotAvailable;

    private static string Mb(double? megabytes) =>
        megabytes is { } value && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value.ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : NotAvailable;

    private static string FormatBytes(double bytes) => bytes switch
    {
        >= 1024d * 1024d => (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1024d => (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => bytes.ToString("0", CultureInfo.InvariantCulture) + " B",
    };

    private static string? Format(double? value, string format) =>
        value is { } number && !double.IsNaN(number) && !double.IsInfinity(number)
            ? number.ToString(format, CultureInfo.InvariantCulture)
            : null;

    private static string? Format(long? value, string format) =>
        value is { } number ? number.ToString(format, CultureInfo.InvariantCulture) : null;

    private static string FormatAge(double? ageMs) =>
        ageMs is { } age
            ? age < 1000d
                ? age.ToString("0", CultureInfo.InvariantCulture) + " ms 前"
                : (age / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + " s 前"
            : NotAvailable;

    private static string FormatDuration(double? seconds)
    {
        if (seconds is not { } total || double.IsNaN(total) || total < 0)
        {
            return NotAvailable;
        }

        var span = TimeSpan.FromSeconds(total);
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}m {span.Seconds}s");
    }

    // 字段放在类末尾，便于"绑定面"从上到下阅读。
    private KppTelemetryState _state = KppTelemetryState.NoInstance;
    private KppTelemetrySnapshot? _snapshot;
    private string _statusTitle = "未选择 KSP 实例";
    private string _statusDetail = string.Empty;
    private string _statusPill = "无实例";
    private string _emptyTitle = "未选择 KSP 实例";
    private string _emptyDetail = string.Empty;
    private string _instanceName = NotAvailable;
    private string _instanceGameDir = NotAvailable;
    private string _telemetryDirectoryText = NotAvailable;
    private string _kspVersionText = NotAvailable;
    private string _dllVersionText = NotAvailable;
    private string _unityVersionText = NotAvailable;
    private string _bridgeVersionText = NotAvailable;
    private string _sessionText = NotAvailable;
    private string _contextText = NotAvailable;
    private string _timeScaleText = NotAvailable;
    private string _seqText = NotAvailable;
    private string _ageText = NotAvailable;
    private string _updatedAtText = NotAvailable;
    private IReadOnlyList<PerformanceKeyValueRow> _statusRows = Array.Empty<PerformanceKeyValueRow>();
    private IReadOnlyList<PerformanceMetricRow> _metrics = Array.Empty<PerformanceMetricRow>();
    private string _timelineSummaryText = NotAvailable;
    private IReadOnlyList<PerformanceBreakdownRow> _breakdownRows = Array.Empty<PerformanceBreakdownRow>();
    private string _breakdownTotalText = NotAvailable;
    private bool _gpuAvailable;
    private string _gpuStatusText = NotAvailable;
    private IReadOnlyList<PerformanceGpuStageRow> _gpuStageRows = Array.Empty<PerformanceGpuStageRow>();
    private IReadOnlyList<PerformanceKeyValueRow> _gpuBracketRows = Array.Empty<PerformanceKeyValueRow>();
    private string _gpuNoteText = NotAvailable;
    private IReadOnlyList<PerformanceHotspotTab> _hotspotTabs = Array.Empty<PerformanceHotspotTab>();
    private string _selectedHotspotKey = HotspotsPlugins;
    private IReadOnlyList<PerformanceHotspotRow> _hotspotRows = Array.Empty<PerformanceHotspotRow>();
    private string _hotspotHeaderText = "暂无热点";
    private string _hotspotEmptyText = "还没有热点数据。";
    private bool _hasDiagnostics;
    private string _diagnosticTitle = NotAvailable;
    private string _diagnosticDescription = NotAvailable;
    private string _diagnosticAdvice = NotAvailable;
    private string _diagnosticSeverity = "unknown";
    private IReadOnlyList<PerformanceCulpritRow> _diagnosticCulprits = Array.Empty<PerformanceCulpritRow>();
    private IReadOnlyList<PerformanceCheckRow> _diagnosticChecks = Array.Empty<PerformanceCheckRow>();
    private IReadOnlyList<PerformanceKeyValueRow> _memoryRows = Array.Empty<PerformanceKeyValueRow>();
    private IReadOnlyList<PerformanceKeyValueRow> _padderRows = Array.Empty<PerformanceKeyValueRow>();
    private string _padderStateText = NotAvailable;
    private string _probeText = NotAvailable;
    private IReadOnlyList<PerformanceAllocatorRow> _allocatorRows = Array.Empty<PerformanceAllocatorRow>();
    private IReadOnlyList<PerformanceSpikeRow> _spikeRows = Array.Empty<PerformanceSpikeRow>();
    private string _spikeHeaderText = "掉帧尖峰";
    private bool _isCommandBusy;
    private double _padTargetMb = 4096d;
    private string _commandMessage = string.Empty;
    private string _commandError = string.Empty;
}
