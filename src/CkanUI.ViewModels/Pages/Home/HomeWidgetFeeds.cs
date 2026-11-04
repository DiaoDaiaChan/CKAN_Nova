using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Home;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Home;

/// <summary>一次数据部件读取的结果：标题下的说明、统计值、内容行与页脚。</summary>
public sealed record HomeWidgetFeed(
    string? Subtitle = null,
    IReadOnlyList<PluginHomeWidgetMetric>? Metrics = null,
    IReadOnlyList<PluginHomeWidgetLine>? Lines = null,
    string? Footer = null);

/// <summary>
/// 宿主自带的数据部件（模组更新、安装历史、下载缓存、存档、后台任务）。
/// 与插件部件共用同一套统计/内容行形状，但数据来自宿主自己的服务，因此不画「插件」角标。
/// </summary>
public sealed partial class FeedHomeWidgetViewModel : HomeWidgetViewModel
{
    private readonly Func<CancellationToken, Task<HomeWidgetFeed>> _loader;

    /// <summary>创建一个数据部件。</summary>
    public FeedHomeWidgetViewModel(
        HomePageViewModel page,
        string id,
        string title,
        string glyph,
        string? pageKey,
        Func<CancellationToken, Task<HomeWidgetFeed>> loader)
        : base(id, title, glyph, page)
    {
        PageKey = pageKey;
        _loader = loader;
        StatusText = "正在读取…";
    }

    /// <summary>点「打开」时跳转的页签键；空表示不提供。</summary>
    public string? PageKey { get; }

    /// <summary>统计值。</summary>
    public ObservableCollection<PluginHomeWidgetMetric> Metrics { get; } = new();

    /// <summary>内容行。</summary>
    public ObservableCollection<PluginHomeWidgetLine> Lines { get; } = new();

    /// <summary>副标题。</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>页脚。</summary>
    [ObservableProperty]
    private string _footerText = string.Empty;

    /// <summary>错误文案（空串表示没有错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>是否正在读取。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>读取状态（加载中/没有数据等），有数据后清空。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>有统计值。</summary>
    public bool HasMetrics => Metrics.Count > 0;

    /// <summary>有内容行。</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>有副标题。</summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    /// <summary>有页脚。</summary>
    public bool HasFooter => !string.IsNullOrWhiteSpace(FooterText);

    /// <summary>有错误。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>有可跳转的页签。</summary>
    public bool CanOpen => !string.IsNullOrWhiteSpace(PageKey);

    /// <summary>加载中或没有数据时显示占位区。</summary>
    public bool ShowPlaceholder => IsLoading || (!HasMetrics && !HasLines && (!string.IsNullOrWhiteSpace(StatusText) || HasError));

    /// <summary>读取这块部件的数据。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        BeginLoad();
        try
        {
            Apply(await _loader(cancellationToken).ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    /// <summary>把一次结果写进可绑定字段。</summary>
    public void Apply(HomeWidgetFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        Subtitle = feed.Subtitle ?? string.Empty;
        FooterText = feed.Footer ?? string.Empty;
        ErrorText = string.Empty;
        StatusText = string.Empty;
        IsLoading = false;

        Metrics.Clear();
        foreach (var metric in feed.Metrics ?? Array.Empty<PluginHomeWidgetMetric>())
        {
            if (metric is not null)
            {
                Metrics.Add(metric);
            }
        }

        Lines.Clear();
        foreach (var line in feed.Lines ?? Array.Empty<PluginHomeWidgetLine>())
        {
            if (line is not null)
            {
                Lines.Add(line);
            }
        }

        NotifyDerived();
    }

    /// <summary>读取失败：保留标题，显示可读错误。</summary>
    public void Fail(string message)
    {
        ErrorText = string.IsNullOrWhiteSpace(message) ? "读取失败。" : message;
        StatusText = string.Empty;
        IsLoading = false;
        NotifyDerived();
    }

    /// <summary>标记开始读取。</summary>
    public void BeginLoad()
    {
        IsLoading = true;
        ErrorText = string.Empty;
        StatusText = "正在读取…";
        NotifyDerived();
    }

    /// <summary>正常返回但没有任何数据时的占位文案。</summary>
    public void Empty(string message)
    {
        Metrics.Clear();
        Lines.Clear();
        Subtitle = string.Empty;
        FooterText = string.Empty;
        ErrorText = string.Empty;
        StatusText = message;
        IsLoading = false;
        NotifyDerived();
    }

    partial void OnSubtitleChanged(string value) => OnPropertyChanged(nameof(HasSubtitle));
    partial void OnFooterTextChanged(string value) => OnPropertyChanged(nameof(HasFooter));
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowPlaceholder));
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(ShowPlaceholder));

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(HasMetrics));
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(HasFooter));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
}

/// <summary>把宿主数据折成主页部件要显示的形状；纯函数，便于直接测文案与边界。</summary>
public static class HomeWidgetFeeds
{
    /// <summary>模组更新：已装多少、可更新多少、兼容更新多少，以及前几条更新。</summary>
    public static HomeWidgetFeed ModUpdates(RepositorySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Available.Count == 0)
        {
            return new HomeWidgetFeed(Footer: "模组仓库还没加载。打开模组库刷新后这里会显示可更新的模组。");
        }

        var installed = snapshot.Available.Where(mod => mod.IsInstalled).ToArray();
        var updates = installed.Where(mod => mod.HasUpdate).ToArray();
        var compatible = updates.Count(mod => mod.IsCompatible);
        var metrics = new[]
        {
            new PluginHomeWidgetMetric("已安装", installed.Length.ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("可更新", updates.Length.ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("兼容更新", compatible.ToString(CultureInfo.CurrentCulture)),
        };

        var lines = updates
            .OrderByDescending(mod => mod.IsCompatible)
            .ThenByDescending(mod => mod.DownloadCount)
            .Take(5)
            .Select(mod => new PluginHomeWidgetLine(
                mod.Name,
                string.IsNullOrWhiteSpace(mod.InstalledVersion)
                    ? "已安装版本未知"
                    : $"{mod.InstalledVersion} → {mod.LatestVersion ?? "最新版未知"}",
                mod.IsCompatible ? "可更新" : "不兼容"))
            .ToArray();

        var footer = snapshot.UpdatedAt is { } updated
            ? (snapshot.IsFromCache ? "仓库来自本地缓存 · " : "仓库更新于 ") + updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "仓库更新时间未知";
        var subtitle = updates.Length == 0
            ? "所有已安装模组都是最新版本。"
            : $"有 {updates.Length} 个模组可以更新，其中 {compatible} 个与当前游戏版本兼容。";
        return new HomeWidgetFeed(subtitle, metrics, lines, footer);
    }

    /// <summary>安装历史：最近几条操作，按时间倒序。</summary>
    public static HomeWidgetFeed InstallHistory(IReadOnlyList<InstallHistoryEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return new HomeWidgetFeed(Footer: "还没有安装或卸载记录。");
        }

        var lines = entries
            .Take(5)
            .Select(entry => new PluginHomeWidgetLine(
                $"{OperationText(entry.Operation)} · {entry.ChangeCount} 个模组",
                entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
                    + (string.IsNullOrWhiteSpace(entry.InstanceName) ? string.Empty : " · " + entry.InstanceName),
                OutcomeText(entry.Outcome)))
            .ToArray();

        var metrics = new[]
        {
            new PluginHomeWidgetMetric("最近记录", entries.Count.ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("成功", entries.Count(entry => entry.Outcome == InstallHistoryOutcome.Succeeded).ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("失败/取消", entries.Count(entry => entry.Outcome is InstallHistoryOutcome.Failed or InstallHistoryOutcome.PartiallyFailed or InstallHistoryOutcome.Cancelled).ToString(CultureInfo.CurrentCulture)),
        };
        return new HomeWidgetFeed("最近 5 次安装、升级或卸载。", metrics, lines, "完整记录在「安装历史」页。");
    }

    /// <summary>下载缓存：条目数、占用与上限。</summary>
    public static HomeWidgetFeed Cache(CacheSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new HomeWidgetFeed(Footer: "下载缓存服务还没有返回数据。");
        }

        var limit = snapshot.HasLimit
            ? FormatBytes(snapshot.SizeLimitBytes!.Value)
            : "不限";
        var metrics = new[]
        {
            new PluginHomeWidgetMetric("缓存条目", snapshot.EntryCount.ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("占用", FormatBytes(snapshot.TotalBytes)),
            new PluginHomeWidgetMetric("上限", limit),
        };
        var subtitle = snapshot.HasLimit
            ? $"已用 {snapshot.UsagePercent.ToString("0.#", CultureInfo.CurrentCulture)}%，还剩 {FormatBytes(snapshot.RemainingBytes ?? 0)}。"
            : "没有设置容量上限，缓存会一直增长。";
        return new HomeWidgetFeed(
            subtitle,
            metrics,
            Array.Empty<PluginHomeWidgetLine>(),
            string.IsNullOrWhiteSpace(snapshot.CacheDirectory) ? null : "目录：" + snapshot.CacheDirectory);
    }

    /// <summary>存档：存档数、备份数与最近几个存档。</summary>
    public static HomeWidgetFeed Saves(IReadOnlyList<SaveGameInfo>? saves)
    {
        if (saves is null || saves.Count == 0)
        {
            return new HomeWidgetFeed(Footer: "这个实例还没有存档。");
        }

        var backups = saves.Count(save => save.IsBackup);
        var metrics = new[]
        {
            new PluginHomeWidgetMetric("存档", (saves.Count - backups).ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("备份", backups.ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("写前快照", saves.Sum(save => save.SnapshotCount).ToString(CultureInfo.CurrentCulture)),
        };
        var lines = saves
            .OrderByDescending(save => save.LastWriteTime)
            .Take(5)
            .Select(save => new PluginHomeWidgetLine(
                string.IsNullOrWhiteSpace(save.Title) ? save.SlotName : save.Title!,
                BuildSaveDetail(save),
                save.IsBackup ? "备份" : null))
            .ToArray();
        return new HomeWidgetFeed("按最后写入时间排序。", metrics, lines, "完整列表在「存档」页。");
    }

    /// <summary>后台任务：进行中、排队与最近状态。</summary>
    public static HomeWidgetFeed Operations(IReadOnlyList<OperationProgress>? operations)
    {
        if (operations is null || operations.Count == 0)
        {
            return new HomeWidgetFeed(Footer: "当前没有后台任务。");
        }

        var running = operations.Count(operation => operation.State == OperationState.Running);
        var pending = operations.Count(operation => operation.State == OperationState.Pending);
        var metrics = new[]
        {
            new PluginHomeWidgetMetric("进行中", running.ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("排队", pending.ToString(CultureInfo.CurrentCulture)),
            new PluginHomeWidgetMetric("总数", operations.Count.ToString(CultureInfo.CurrentCulture)),
        };
        var lines = operations
            .OrderBy(operation => operation.State == OperationState.Running ? 0 : 1)
            .Take(5)
            .Select(operation => new PluginHomeWidgetLine(
                operation.Title,
                string.IsNullOrWhiteSpace(operation.Message) ? operation.Kind : operation.Message!,
                operation.State switch
                {
                    OperationState.Running => operation.Percent.ToString("0", CultureInfo.CurrentCulture) + "%",
                    OperationState.Pending => "排队",
                    OperationState.Succeeded => "完成",
                    OperationState.Failed => "失败",
                    OperationState.Cancelled => "取消",
                    _ => null,
                }))
            .ToArray();
        return new HomeWidgetFeed("与底栏的后台任务指示器同源。", metrics, lines, null);
    }

    private static string BuildSaveDetail(SaveGameInfo save)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(save.Mode))
        {
            parts.Add(save.Mode!);
        }

        if (!string.IsNullOrWhiteSpace(save.Scene))
        {
            parts.Add(save.Scene!);
        }

        if (save.VesselCount is { } vessels)
        {
            parts.Add($"{vessels} 艘飞船");
        }

        parts.Add(save.LastWriteTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
        return string.Join(" · ", parts);
    }

    private static string OperationText(InstallHistoryOperation operation) => operation switch
    {
        InstallHistoryOperation.Install => "安装",
        InstallHistoryOperation.Upgrade => "升级",
        InstallHistoryOperation.Downgrade => "降级",
        InstallHistoryOperation.Remove => "卸载",
        InstallHistoryOperation.Replace => "替换",
        InstallHistoryOperation.Restore => "重装",
        _ => operation.ToString(),
    };

    private static string OutcomeText(InstallHistoryOutcome outcome) => outcome switch
    {
        InstallHistoryOutcome.Succeeded => "成功",
        InstallHistoryOutcome.PartiallyFailed => "部分失败",
        InstallHistoryOutcome.Failed => "失败",
        InstallHistoryOutcome.Cancelled => "取消",
        _ => outcome.ToString(),
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.CurrentCulture) + " B";
        }

        if (bytes < 1024L * 1024L)
        {
            return (bytes / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " KB";
        }

        if (bytes < 1024L * 1024L * 1024L)
        {
            return (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.CurrentCulture) + " MB";
        }

        return (bytes / (1024d * 1024d * 1024d)).ToString("0.##", CultureInfo.CurrentCulture) + " GB";
    }
}
