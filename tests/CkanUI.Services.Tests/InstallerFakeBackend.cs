using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Installers;

namespace CkanUI.Services.Tests;

/// <summary>
/// 可控的假安装后端：记录调用顺序，并允许在解析/卸载/安装/回滚任一阶段注入失败、
/// 取消或阻塞，用于覆盖 <see cref="InstallerService"/> 的编排分支。全程不触网、不碰磁盘。
/// </summary>
internal sealed class InstallerFakeBackend : IInstallerBackend
{
    /// <summary>当前「已安装」的模组（回滚断言就看它）。</summary>
    public List<string> Installed { get; } = new();

    /// <summary>被调用过的方法名序列。</summary>
    public List<string> Calls { get; } = new();

    /// <summary>最近一次回滚收到的「要装回」清单。</summary>
    public List<string> LastRollbackRestore { get; } = new();

    /// <summary>最近一次回滚收到的「要清掉」清单。</summary>
    public List<string> LastRollbackCleanup { get; } = new();

    public int RollbackCalls { get; private set; }

    /// <summary>卸载/安装已被调用（用于测试里等后台线程走到那一步）。</summary>
    public ManualResetEventSlim RemoveStarted { get; } = new(false);

    public ManualResetEventSlim InstallStarted { get; } = new(false);

    /// <summary>非空时卸载/安装会阻塞在这里，用来制造「任务进行中」的时序。</summary>
    public ManualResetEventSlim? RemoveGate { get; set; }

    public ManualResetEventSlim? InstallGate { get; set; }

    public Func<ChangeSetPreview, CancellationToken, (InstallerBackendResult Result, InstallerPlan? Plan)>? ResolveHandler { get; set; }

    public Func<InstallerPlan, IProgress<InstallerBackendProgress>?, CancellationToken, InstallerBackendResult>? RemoveHandler { get; set; }

    public Func<InstallerPlan, IProgress<InstallerBackendProgress>?, CancellationToken, InstallerBackendResult>? InstallHandler { get; set; }

    public InstallerBackendResult? RollbackResult { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<string> InstalledIdentifiers()
    {
        Calls.Add("snapshot");
        lock (Installed)
        {
            return Installed.ToArray();
        }
    }

    /// <inheritdoc />
    public InstallerBackendResult Resolve(ChangeSetPreview changeSet, CancellationToken ct, out InstallerPlan? plan)
    {
        Calls.Add("resolve");
        if (ResolveHandler is { } handler)
        {
            var (result, resolved) = handler(changeSet, ct);
            plan = resolved;
            return result;
        }

        var toRemove = changeSet.Items.Where(item => item.Action == ChangeAction.Remove)
                                      .Select(item => item.Identifier).ToArray();
        var toInstall = changeSet.Items.Where(item => item.Action != ChangeAction.Remove)
                                       .Select(item => item.Identifier).ToArray();
        plan = new InstallerPlan(changeSet.Items, toRemove, toInstall, changeSet.Warnings,
                                 changeSet.EstimatedDownloadBytes);
        return InstallerBackendResult.Ok(toInstall);
    }

    /// <inheritdoc />
    public InstallerBackendResult Remove(InstallerPlan plan,
                                         IProgress<InstallerBackendProgress>? progress,
                                         CancellationToken ct)
    {
        Calls.Add("remove");
        RemoveStarted.Set();
        RemoveGate?.Wait();
        if (RemoveHandler is { } handler)
        {
            return handler(plan, progress, ct);
        }

        if (ct.IsCancellationRequested)
        {
            return InstallerBackendResult.Cancelled();
        }

        progress?.Report(new InstallerBackendProgress("正在卸载…", 20));
        lock (Installed)
        {
            foreach (var identifier in plan.ToRemove)
            {
                Installed.Remove(identifier);
            }
        }

        return InstallerBackendResult.Ok(plan.ToRemove);
    }

    /// <inheritdoc />
    public InstallerBackendResult Install(InstallerPlan plan,
                                          IProgress<InstallerBackendProgress>? progress,
                                          CancellationToken ct)
    {
        Calls.Add("install");
        InstallStarted.Set();
        InstallGate?.Wait();
        if (InstallHandler is { } handler)
        {
            return handler(plan, progress, ct);
        }

        if (ct.IsCancellationRequested)
        {
            return InstallerBackendResult.Cancelled();
        }

        progress?.Report(new InstallerBackendProgress("正在安装…", 50));
        lock (Installed)
        {
            Installed.AddRange(plan.ToInstall);
        }

        return InstallerBackendResult.Ok(plan.ToInstall);
    }

    /// <inheritdoc />
    public InstallerBackendResult Rollback(IReadOnlyList<string> restore,
                                           IReadOnlyList<string> cleanup,
                                           IProgress<InstallerBackendProgress>? progress)
    {
        Calls.Add("rollback");
        RollbackCalls++;
        LastRollbackRestore.Clear();
        LastRollbackRestore.AddRange(restore);
        LastRollbackCleanup.Clear();
        LastRollbackCleanup.AddRange(cleanup);

        lock (Installed)
        {
            foreach (var identifier in cleanup)
            {
                Installed.Remove(identifier);
            }

            foreach (var identifier in restore)
            {
                if (!Installed.Contains(identifier))
                {
                    Installed.Add(identifier);
                }
            }
        }

        progress?.Report(new InstallerBackendProgress("正在回滚…", 95));
        return RollbackResult ?? InstallerBackendResult.Ok();
    }
}
