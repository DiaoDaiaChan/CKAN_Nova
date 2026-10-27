using System;
using System.Collections.Generic;
using System.Threading;

using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Installers;

/// <summary>
/// CKAN 触达面（06 篇 INT-08）：把 ModuleInstaller / RelationshipResolver /
/// NetAsyncModulesDownloader 包在接口后面。这样 <see cref="InstallerService"/> 的编排、
/// 回滚、进度与取消逻辑可以脱离网络与真实游戏目录单测。
/// </summary>
/// <remarks>
/// 所有方法都是同步阻塞的：Core 的安装入口没有异步版本，实现必须自己在线程池上跑，
/// 编排层负责用 Task.Run 把它搬离 UI 线程（05 篇 §2）。
/// </remarks>
public interface IInstallerBackend
{
    /// <summary>当前注册表里已安装的标识符快照（用于计算「本次新增」与回滚范围）。</summary>
    IReadOnlyList<string> InstalledIdentifiers();

    /// <summary>
    /// 依赖求解（02 篇 §7/§8）：用 StabilityToleranceConfig + RelationshipResolverOptions
    /// 解析变更集，产出可执行计划。失败时 <paramref name="plan"/> 为 null。
    /// </summary>
    InstallerBackendResult Resolve(ChangeSetPreview changeSet, CancellationToken ct, out InstallerPlan? plan);

    /// <summary>执行卸载（在安装之前，与上游 MainInstall 顺序一致）。</summary>
    InstallerBackendResult Remove(InstallerPlan plan,
                                  IProgress<InstallerBackendProgress>? progress,
                                  CancellationToken ct);

    /// <summary>执行安装/升级（ConfirmPrompt:false，确认由 UI 先行完成）。</summary>
    InstallerBackendResult Install(InstallerPlan plan,
                                   IProgress<InstallerBackendProgress>? progress,
                                   CancellationToken ct);

    /// <summary>
    /// 回滚：清理本次新增的半成品（<paramref name="cleanup"/>），把已卸载的装回去（<paramref name="restore"/>）。
    /// 回滚必须尽力做完，因此不接受取消令牌（05 篇 §9：取消后 30s 内界面可操作、不留半装文件）。
    /// </summary>
    InstallerBackendResult Rollback(IReadOnlyList<string> restore,
                                    IReadOnlyList<string> cleanup,
                                    IProgress<InstallerBackendProgress>? progress);
}
