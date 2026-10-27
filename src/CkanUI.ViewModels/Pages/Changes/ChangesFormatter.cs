using System.Globalization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Mods;

namespace CkanUI.ViewModels.Pages.Changes;

/// <summary>
/// 变更集页的全部用户可见文案与数字/体积格式化（03 篇 §4 / §11：文案集中在 VM 侧，XAML 里不写字面串）。
/// </summary>
/// <remarks>
/// 体积格式化复用 <see cref="ModsFormatter.Bytes"/>，保证两页对同一数字给出同一个写法（03 篇 §11「格式化集中一处」）。
/// </remarks>
public static class ChangesFormatter
{
    /// <summary>空变更集（避免各处 <c>new ChangeSetPreview(...)</c> 重复）。</summary>
    public static ChangeSetPreview EmptyPreview { get; } = new(
        Array.Empty<ChangeItemDto>(),
        Array.Empty<string>(),
        0,
        Array.Empty<string>());

    /// <summary>动作文案（03 篇 §4 明细表"动作"列）。</summary>
    public static string ActionText(ChangeAction action) => action switch
    {
        ChangeAction.Install => "安装",
        ChangeAction.Upgrade => "升级",
        ChangeAction.Downgrade => "降级",
        ChangeAction.Remove => "卸载",
        ChangeAction.Replace => "替换",
        _ => "变更",
    };

    /// <summary>来源文案（完整，用于读屏与提示）。</summary>
    public static string OriginText(ChangeOrigin origin) => origin switch
    {
        ChangeOrigin.UserRequested => "用户选择",
        ChangeOrigin.Dependency => "依赖",
        ChangeOrigin.Recommendation => "推荐",
        ChangeOrigin.AutoInstalled => "自动安装",
        _ => "未知来源",
    };

    /// <summary>来源徽章文案（短，用于表格里的胶囊）。</summary>
    public static string OriginBadgeText(ChangeOrigin origin) => origin switch
    {
        ChangeOrigin.UserRequested => "用户",
        ChangeOrigin.Dependency => "依赖",
        ChangeOrigin.Recommendation => "推荐",
        ChangeOrigin.AutoInstalled => "自动",
        _ => "未知",
    };

    /// <summary>千分位计数。</summary>
    public static string Count(long value) => ModsFormatter.Count(value);

    /// <summary>体积格式化（null / 非正值显示"—"）。</summary>
    public static string Bytes(long? bytes) => ModsFormatter.Bytes(bytes);

    /// <summary>下载体积文案：0 字节明确写成"无需下载"，不留空。</summary>
    public static string DownloadText(long estimatedBytes) =>
        estimatedBytes <= 0 ? "无需下载" : $"预计下载 {ModsFormatter.Bytes(estimatedBytes)}";

    /// <summary>各动作的条目数。</summary>
    public readonly record struct Counts(int Install, int Upgrade, int Downgrade, int Remove, int Replace, int Total);

    /// <summary>统计一份预览里各动作的条目数。</summary>
    public static Counts Count(ChangeSetPreview? preview)
    {
        if (preview is not { Items.Count: > 0 } source)
        {
            return default;
        }

        var install = 0;
        var upgrade = 0;
        var downgrade = 0;
        var remove = 0;
        var replace = 0;
        foreach (var item in source.Items)
        {
            switch (item.Action)
            {
                case ChangeAction.Install:
                    install++;
                    break;
                case ChangeAction.Upgrade:
                    upgrade++;
                    break;
                case ChangeAction.Downgrade:
                    downgrade++;
                    break;
                case ChangeAction.Remove:
                    remove++;
                    break;
                case ChangeAction.Replace:
                    replace++;
                    break;
                default:
                    break;
            }
        }

        return new Counts(install, upgrade, downgrade, remove, replace, source.Items.Count);
    }

    /// <summary>摘要文案（03 篇 §4）："将安装 3 个、升级 1 个、降级 0 个、卸载 0 个；预计下载 45.2 MB"。</summary>
    public static string Summary(ChangeSetPreview? preview)
    {
        if (preview is not { Items.Count: > 0 })
        {
            return "还没有待应用的变更";
        }

        var counts = Count(preview);
        var text = string.Create(
            CultureInfo.CurrentCulture,
            $"将安装 {counts.Install} 个、升级 {counts.Upgrade} 个、降级 {counts.Downgrade} 个、卸载 {counts.Remove} 个");

        if (counts.Replace > 0)
        {
            text += string.Create(CultureInfo.CurrentCulture, $"、替换 {counts.Replace} 个");
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{text}；{DownloadText(preview.EstimatedDownloadBytes)}");
    }

    /// <summary>执行流状态徽章文案。</summary>
    public static string StateBadge(ChangeExecutionState state) => state switch
    {
        ChangeExecutionState.Idle => "待计算",
        ChangeExecutionState.Computing => "计算中",
        ChangeExecutionState.Ready => "就绪",
        ChangeExecutionState.Applying => "执行中",
        ChangeExecutionState.Done => "已完成",
        ChangeExecutionState.Failed => "失败",
        ChangeExecutionState.Cancelled => "已取消",
        _ => "未知",
    };

    /// <summary>状态说明（03 篇 §9：状态必须同时有文字，不能只靠颜色）。</summary>
    public static string StateText(ChangeExecutionState state, double percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return state switch
        {
            ChangeExecutionState.Idle => "还没有待应用的变更",
            ChangeExecutionState.Computing => "正在计算变更集（含依赖解析）…",
            ChangeExecutionState.Ready => "变更集已就绪，点「开始」执行。",
            ChangeExecutionState.Applying => string.Create(
                CultureInfo.CurrentCulture,
                $"正在应用变更… 已完成 {clamped:0}%"),
            ChangeExecutionState.Done => "变更已全部应用完成。",
            ChangeExecutionState.Failed => "应用变更失败，请查看下方明细与错误栏。",
            ChangeExecutionState.Cancelled => "已取消：Core 已回滚本次事务，游戏目录保持原样。",
            _ => "状态未知",
        };
    }

    /// <summary>执行结果汇总（03 篇 §4.1 完成态：成功 n / 失败 m / 跳过 k）。</summary>
    public static string InstallResult(InstallReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var head = report.IsFullSuccess
            ? "全部成功"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"部分失败：成功 {report.Installed.Count + report.Removed.Count} / 失败 {report.Failed.Count} / 提示 {report.Warnings.Count}");
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{head}（安装 {report.Installed.Count}、移除 {report.Removed.Count}）");
    }

    /// <summary>把一组字符串拼成多行明细（用于冲突、警告、失败清单）。</summary>
    public static string Lines(IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, values);
    }
}
