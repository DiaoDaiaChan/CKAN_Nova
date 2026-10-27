using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Installers;

/// <summary>
/// 安装/卸载编排服务（INT-08）。对齐规格 02 篇 §4/§7、05 篇 §4/§6/§9、06 篇 INT-08：
/// 长任务一律登记到 <see cref="IOperationQueue"/>（可取消）、进度经它回报、
/// 任一步失败或取消都回滚到调用前的状态、失败项逐条说清。
/// </summary>
/// <remarks>
/// 与 CKAN 的接触面全部收在 <see cref="IInstallerBackend"/> 后面：本类只做编排，
/// 因而可以在测试里用假后端覆盖成功/依赖缺失/取消/回滚/卸载全部分支，不触网。
/// </remarks>
public sealed class InstallerService : IInstallerService
{
    /// <summary>登记到操作队列时使用的任务类别（05 篇 §6：应用级同时只允许一个）。</summary>
    public const string TaskKind = "install";

    /// <summary>进度节流窗口（05 篇 §4：100ms 一次，同文案不重发）。</summary>
    private const int ThrottleMilliseconds = 100;

    private const string CancelledMessage = "已取消，未做改动。";

    private readonly IInstallerBackendFactory _factory;
    private readonly IOperationQueue _operations;
    private readonly IDispatcher _dispatcher;
    private readonly IErrorReporter? _errors;

    public InstallerService(IInstallerBackendFactory factory,
                            IOperationQueue operations,
                            IDispatcher dispatcher,
                            IErrorReporter? errors = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _errors = errors;
    }

    /// <inheritdoc />
    public async Task<OperationResult<InstallReport>> ApplyAsync(ChangeSetPreview changeSet,
                                                                 IProgress<OperationProgress>? progress = null,
                                                                 CancellationToken ct = default)
    {
        // 参数问题也不穿透服务层（01 篇 §8），统一走 OperationResult。
        if (changeSet is null)
        {
            return OperationResult<InstallReport>.Fail("变更集为空，未执行任何操作。");
        }

        if (changeSet.IsEmpty)
        {
            // 空变更集是合法输入（用户什么都没选），按成功处理而不是失败。
            return OperationResult<InstallReport>.Ok(EmptyReport());
        }

        if (changeSet.HasConflicts)
        {
            return OperationResult<InstallReport>.Fail("变更集存在冲突，未执行任何操作。",
                                                      string.Join("；", changeSet.Conflicts));
        }

        if (HasRunningInstall())
        {
            return OperationResult<InstallReport>.Fail("已有正在进行的安装/卸载任务，请等待其完成。", null, retryable: true);
        }

        if (ct.IsCancellationRequested)
        {
            return OperationResult<InstallReport>.Fail(CancelledMessage);
        }

        if (!_factory.TryCreate(out var backend) || backend is null)
        {
            return OperationResult<InstallReport>.Fail("尚未选定游戏实例，无法安装或卸载模组。");
        }

        var title = BuildTitle(changeSet);
        var reporter = new ProgressReporter(progress, _dispatcher, TaskKind, title);
        var completion = new TaskCompletionSource<OperationResult<InstallReport>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        string queueTaskId;
        try
        {
            queueTaskId = _operations.Enqueue(TaskKind, title,
                queueToken => RunAsync(backend, changeSet, reporter, completion, queueToken));
        }
        catch (Exception ex)
        {
            _errors?.Report(ex, "InstallerService.ApplyAsync", "无法开始安装，请稍后重试。");
            return OperationResult<InstallReport>.Fail("无法开始安装，请稍后重试。", ex.Message, retryable: true);
        }

        // 队列分配的 id 是权威 id：外部取消与进度回报都必须用它，不能用自建的备用 id。
        reporter.AssignTaskId(queueTaskId);

        // UI 侧取消（05 篇 §4）：外部令牌一触发就转成队列取消，取消真正传导到后台工作项。
        using var registration = ct.Register(() => _operations.Cancel(queueTaskId));

        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>队列工作项：无论成功、失败还是取消都给出唯一结论，且不把异常抛给队列。</summary>
    private async Task RunAsync(IInstallerBackend backend,
                                ChangeSetPreview changeSet,
                                ProgressReporter reporter,
                                TaskCompletionSource<OperationResult<InstallReport>> completion,
                                CancellationToken queueToken)
    {
        try
        {
            // 后端调用是同步阻塞的 CKAN API：整体挪到线程池，绝不在 UI 线程上跑（05 篇 §2）。
            var outcome = await Task.Run(() => Execute(backend, changeSet, reporter, queueToken),
                                         CancellationToken.None).ConfigureAwait(false);
            completion.TrySetResult(outcome);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetResult(OperationResult<InstallReport>.Fail(CancelledMessage));
        }
        catch (Exception ex)
        {
            _errors?.Report(ex, "InstallerService.ApplyAsync", "安装过程中发生未预期的错误。");
            completion.TrySetResult(OperationResult<InstallReport>.Fail(
                "安装过程中发生未预期的错误，已尝试还原。", ex.Message, retryable: true));
        }
    }

    /// <summary>同步执行体（跑在线程池上）：解析 → 卸载 → 安装，失败/取消即回滚。</summary>
    private OperationResult<InstallReport> Execute(IInstallerBackend backend,
                                                   ChangeSetPreview changeSet,
                                                   ProgressReporter reporter,
                                                   CancellationToken ct)
    {
        reporter.Report("正在解析依赖…", 2, bypassThrottle: true);

        // 调用前的实例快照：回滚时用它算「哪些被卸掉了要装回」「哪些是半成品要清掉」。
        IReadOnlyList<string> before;
        try
        {
            before = backend.InstalledIdentifiers();
        }
        catch (Exception ex)
        {
            _errors?.Report(ex, "InstallerService.Execute", "无法读取已安装模组列表。");
            return OperationResult<InstallReport>.Fail("无法读取已安装模组列表，未执行任何操作。", ex.Message);
        }

        var resolve = backend.Resolve(changeSet, ct, out var plan);
        if (resolve.Status == InstallerBackendStatus.Cancelled)
        {
            return Cancelled(resolve);
        }

        if (!resolve.IsSuccess || plan is null)
        {
            return Failure(resolve, "依赖解析失败，未执行任何操作。");
        }

        reporter.Report("准备就绪，开始执行…", 8, bypassThrottle: true);

        var warnings = new List<string>(plan.Warnings);
        var removed = new List<string>();

        if (plan.ToRemove.Count > 0)
        {
            var remove = backend.Remove(plan, reporter.BackendProgress, ct);
            warnings.AddRange(remove.Warnings);
            if (remove.Status == InstallerBackendStatus.Cancelled)
            {
                return Abort(backend, reporter, before, removed, warnings, null, CancelledMessage, remove.Detail);
            }

            if (!remove.IsSuccess)
            {
                return Abort(backend, reporter, before, removed, warnings, remove,
                             "卸载失败，已尝试还原到操作前的状态。", remove.Detail);
            }

            removed.AddRange(remove.Completed);
        }

        if (plan.ToInstall.Count > 0)
        {
            var install = backend.Install(plan, reporter.BackendProgress, ct);
            warnings.AddRange(install.Warnings);
            if (install.Status == InstallerBackendStatus.Cancelled)
            {
                return Abort(backend, reporter, before, removed, warnings, null, CancelledMessage, install.Detail);
            }

            if (!install.IsSuccess)
            {
                return Abort(backend, reporter, before, removed, warnings, install,
                             "安装失败，已尝试还原到操作前的状态。", install.Detail);
            }
        }

        reporter.Report(OperationState.Succeeded, "全部完成。", 100);

        return OperationResult<InstallReport>.Ok(
            new InstallReport(plan.ToInstall, removed, Array.Empty<string>(), warnings));
    }

    /// <summary>
    /// 失败/取消后的统一收尾：先算出被卸掉的（要装回）与本次新装的（要清掉），
    /// 再让后端一次做完回滚。回滚不再接受取消（05 篇 §9：取消后界面要能马上继续用）。
    /// </summary>
    private OperationResult<InstallReport> Abort(IInstallerBackend backend,
                                                 ProgressReporter reporter,
                                                 IReadOnlyList<string> before,
                                                 List<string> removed,
                                                 List<string> warnings,
                                                 InstallerBackendResult? cause,
                                                 string message,
                                                 string? detail)
    {
        var cleanup = new List<string>();
        try
        {
            var now = backend.InstalledIdentifiers();
            foreach (var identifier in before.Where(identifier => !removed.Contains(identifier) && !now.Contains(identifier)))
            {
                // Remove 中途失败时后端可能已经删掉一部分，这里按差异补全还原清单。
                removed.Add(identifier);
            }

            cleanup.AddRange(now.Where(identifier => !before.Contains(identifier)));
        }
        catch (Exception ex)
        {
            // 快照失败也要继续回滚：removed 里已有的信息足够还原大部分。
            _errors?.Report(ex, "InstallerService.Abort", "无法确认当前已安装列表，回滚可能不完整。");
            warnings.Add("无法确认当前已安装列表，回滚可能不完整。");
        }

        var failed = new List<string>();
        AddRange(failed, cause?.Failed);

        var rollback = backend.Rollback(removed, cleanup, reporter.BackendProgress);
        warnings.AddRange(rollback.Warnings);
        AddRange(failed, rollback.Failed);
        if (!rollback.IsSuccess)
        {
            message += "部分模组未能还原，请检查后手动修复。";
        }

        reporter.Report(OperationState.Failed, message, 100);

        var summary = failed.Count == 0
            ? message
            : message + " 涉及模组：" + string.Join("、", failed) + "。";

        return OperationResult<InstallReport>.Fail(summary, detail, retryable: false);
    }

    private OperationResult<InstallReport> Cancelled(InstallerBackendResult result)
        => OperationResult<InstallReport>.Fail(CancelledMessage, result.Detail);

    private static OperationResult<InstallReport> Failure(InstallerBackendResult result, string message)
        => OperationResult<InstallReport>.Fail(
            string.IsNullOrEmpty(result.Message) ? message : message + " " + result.Message,
            result.Detail);

    private static void AddRange(List<string> target, IReadOnlyList<string>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            if (!target.Contains(item))
            {
                target.Add(item);
            }
        }
    }

    private static InstallReport EmptyReport()
        => new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    private bool HasRunningInstall()
        => _operations.Operations.Any(operation => operation.Kind == TaskKind
                                                   && operation.State is OperationState.Pending or OperationState.Running);

    private static string BuildTitle(ChangeSetPreview changeSet)
        => $"安装/卸载模组（{changeSet.Items.Count} 项）";

    /// <summary>进度桥：把后端原始进度折算成 <see cref="OperationProgress"/> 并经调度器封送到 UI 线程。</summary>
    private sealed class ProgressReporter
    {
        private readonly IProgress<OperationProgress>? _sink;
        private readonly IDispatcher _dispatcher;
        private readonly string _kind;
        private readonly string _title;
        private readonly object _gate = new();

        private string? _lastMessage;
        private long _lastTick;
        private double _lastPercent = -1;

        public ProgressReporter(IProgress<OperationProgress>? sink, IDispatcher dispatcher, string kind, string title)
        {
            _sink = sink;
            _dispatcher = dispatcher;
            _kind = kind;
            _title = title;
            BackendProgress = new BackendProgressAdapter(this);
        }

        /// <summary>队列分配的任务 id；进度可能在它回来之前就产生，故用一次性门闩等一拍。</summary>
        private readonly ManualResetEventSlim _idReady = new(false);

        private readonly string _fallbackTaskId = Guid.NewGuid().ToString("N");

        private string? _taskId;

        public IProgress<InstallerBackendProgress> BackendProgress { get; }

        /// <summary>由 <see cref="ApplyAsync"/> 在入队成功后立刻回填队列分配的 id。</summary>
        public void AssignTaskId(string taskId)
        {
            _taskId = taskId;
            _idReady.Set();
        }

        /// <summary>
        /// 取任务 id。入队与首次进度之间是同一个调用栈的相邻两条语句，
        /// 这里最多等 1 秒（超时退回备用 id，绝不无限等）。
        /// </summary>
        private string CurrentTaskId()
        {
            if (_taskId is { } assigned)
            {
                return assigned;
            }

            _idReady.Wait(TimeSpan.FromSeconds(1));
            return _taskId ?? _fallbackTaskId;
        }

        public void Report(string message, double percent, bool bypassThrottle = false)
            => Report(OperationState.Running, message, percent, bypassThrottle);

        public void Report(OperationState state, string message, double percent, bool bypassThrottle = false)
        {
            var now = Environment.TickCount64;
            lock (_gate)
            {
                // 05 篇 §4：100ms 节流 + 同文案不重发；终态与 OneComplete（bypass）例外。
                var terminal = state != OperationState.Running;
                if (!terminal && !bypassThrottle && message == _lastMessage
                    && now - _lastTick < ThrottleMilliseconds)
                {
                    return;
                }

                if (!terminal && !bypassThrottle && message == _lastMessage && percent - _lastPercent < 0.5)
                {
                    return;
                }

                _lastMessage = message;
                _lastTick = now;
                _lastPercent = percent;
            }

            Publish(new OperationProgress(CurrentTaskId(), _kind, _title, message, percent,
                                          CanCancel: state == OperationState.Running, state));
        }

        private void Publish(OperationProgress progress)
        {
            if (_sink is null)
            {
                return;
            }

            // 消息文本在后台线程生成，必须封送后才交给 UI（05 篇 §1）。
            if (_dispatcher.IsOnUiThread)
            {
                _sink.Report(progress);
            }
            else
            {
                _dispatcher.Post(() => _sink.Report(progress));
            }
        }

        /// <summary>后端进度适配器：只做透传，节流与封送由外层统一负责。</summary>
        private sealed class BackendProgressAdapter : IProgress<InstallerBackendProgress>
        {
            private readonly ProgressReporter _owner;

            public BackendProgressAdapter(ProgressReporter owner) => _owner = owner;

            public void Report(InstallerBackendProgress value)
            {
                if (value is null)
                {
                    return;
                }

                _owner.Report(value.Message, value.Percent, value.BypassThrottle);
            }
        }
    }
}
