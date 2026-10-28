using System;
using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.Services.Abstractions.Update;

/// <summary>
/// 自动更新安装端口（12 篇 F-52）：下载 → 校验 → 解包 → 备份 → 原地替换，失败自动回滚。
/// <para>
/// 实现约定：<b>永不抛异常</b>，一切失败都通过 <see cref="OperationResult{T}"/> 的中文文案返回；
/// 下载复用已注册的 <c>IDownloadService</c> 端口（不允许新建 HttpClient）。
/// </para>
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>更新包与备份的存放目录（%LocalAppData%\CKAN\updates）。</summary>
    string UpdatesDirectory { get; }

    /// <summary>当前程序目录（替换目标）。</summary>
    string ProgramDirectory { get; }

    /// <summary>
    /// 执行一次安装。界面通过 <paramref name="progress"/> 拿到阶段与百分比。
    /// </summary>
    /// <param name="plan">安装输入。</param>
    /// <param name="progress">进度回调，可为空。</param>
    /// <param name="ct">取消令牌；取消时未完成的替换会回滚。</param>
    Task<OperationResult<UpdateInstallResult>> InstallAsync(
        UpdateInstallPlan plan,
        IProgress<UpdateInstallProgress>? progress = null,
        CancellationToken ct = default);
}
