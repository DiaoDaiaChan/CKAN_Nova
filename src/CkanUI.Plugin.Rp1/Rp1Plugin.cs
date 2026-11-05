using System.Globalization;
using CkanUI.Plugin.Rp1.Career;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Home;

namespace CkanUI.Plugin.Rp1;

/// <summary>「RP-1 生涯日志」页签插件入口：读取 RP-1 存档并展示时间线。</summary>
public sealed class Rp1Plugin : ICkanUiPlugin
{
    /// <summary>插件唯一标识。</summary>
    public const string PluginId = "com.ckanui.rp1";

    /// <summary>页签导航键。</summary>
    public const string PageKey = "rp1-career";

    /// <summary>插件版本。</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>页面视图模型的完整类型名（宿主按名字跨 ALC 解析）。</summary>
    public const string ViewModelTypeName = "CkanUI.Plugin.Rp1.UI.Rp1CareerPageViewModel";

    private static readonly PluginPageDescriptor[] PageList =
    {
        new(
            PageKey,
            "RP-1 生涯日志",
            ViewModelTypeName,
            Glyph: "History",
            Order: 40),
    };

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public string Name => "RP-1 生涯日志";

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    public string Author => "CKAN UI";

    /// <inheritdoc />
    public string Description => "读取 RP-1 存档，把合同、里程碑、项目、人员与预算周期整理成可按类别筛选的生涯时间线。";

    private static readonly PluginHomeWidgetDescriptor[] WidgetList =
    {
        new(
            CareerWidgetKey,
            "生涯日志",
            "RP-1 的当前日期、资金与最近几条大事件。",
            Glyph: "History",
            PageKey: PageKey),
    };

    /// <summary>主页部件键。</summary>
    public const string CareerWidgetKey = "career";

    /// <inheritdoc />
    public IReadOnlyList<PluginPageDescriptor> Pages => PageList;

    /// <inheritdoc />
    public IReadOnlyList<PluginHomeWidgetDescriptor> HomeWidgets => WidgetList;

    /// <inheritdoc />
    public string MinimumHostVersion => string.Empty;

    /// <inheritdoc />
    /// <remarks>插件不持有进程内资源：存档在页面打开时读取。</remarks>
    public void Initialize()
    {
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="Initialize"/> 对称：停用再启用必须可重入。</remarks>
    public void Deinitialize()
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<PluginHomeWidgetSnapshot>> GetHomeWidgetAsync(
        string key,
        PluginHomeWidgetContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(key, CareerWidgetKey, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Fail("这个插件里没有名为「" + key + "」的主页部件。", retryable: false);
        }

        if (string.IsNullOrWhiteSpace(context.GameDir))
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Ok(new PluginHomeWidgetSnapshot(
                "生涯日志",
                Error: "还没有选中实例：先到「实例」选一个游戏目录。"));
        }

        try
        {
            var service = new Rp1CareerService();
            var saves = await service.ListSavesAsync(context.GameDir!, cancellationToken).ConfigureAwait(false);
            if (!saves.IsSuccess)
            {
                return OperationResult<PluginHomeWidgetSnapshot>.Ok(new PluginHomeWidgetSnapshot(
                    "生涯日志",
                    Error: saves.Message ?? "读取存档列表失败。"));
            }

            var latest = saves.Value?.FirstOrDefault();
            if (latest is null)
            {
                return OperationResult<PluginHomeWidgetSnapshot>.Ok(new PluginHomeWidgetSnapshot(
                    "生涯日志",
                    Error: "这个实例的 saves 目录里还没有 persistent.sfs。"));
            }

            var loaded = await service.LoadAsync(
                context.GameDir!,
                latest.Name,
                context.InstanceName,
                context.GameVersion,
                cancellationToken).ConfigureAwait(false);
            if (!loaded.IsSuccess || loaded.Value is null)
            {
                return OperationResult<PluginHomeWidgetSnapshot>.Ok(new PluginHomeWidgetSnapshot(
                    "生涯日志",
                    Subtitle: latest.Name,
                    Error: loaded.Message ?? "读取存档失败。"));
            }

            var timeline = loaded.Value;
            var metrics = new List<PluginHomeWidgetMetric>
            {
                new("当前日期", timeline.CurrentDateText),
                new("资金", Format(timeline.CurrentFunds, "N0")),
                new("科技", Format(timeline.CurrentScience, "N1")),
                new("声望", Format(timeline.CurrentReputation, "N0")),
                new("合同", timeline.ContractCount.ToString(CultureInfo.InvariantCulture)),
                new("里程碑", timeline.MilestoneCount.ToString(CultureInfo.InvariantCulture)),
            };

            var lines = timeline.Events
                .Where(item => item.Kind != Rp1EventKind.Finance)
                .TakeLast(5)
                .Select(item => new PluginHomeWidgetLine(
                    item.Title,
                    string.IsNullOrWhiteSpace(item.Subtitle) ? item.DateText : item.DateText + " · " + item.Subtitle,
                    item.BadgeText))
                .ToArray();

            var subtitle = timeline.Rp1Installed
                ? latest.Name + " · RP-1 " + (timeline.Rp1Version ?? "版本未知")
                : latest.Name + " · 没有检测到 RP-1";

            return OperationResult<PluginHomeWidgetSnapshot>.Ok(new PluginHomeWidgetSnapshot(
                "生涯日志",
                Subtitle: subtitle,
                Metrics: metrics,
                Lines: lines,
                Footer: "存档更新于 " + latest.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Fail("已取消。", retryable: true);
        }
        catch (Exception exception)
        {
            return OperationResult<PluginHomeWidgetSnapshot>.Ok(new PluginHomeWidgetSnapshot(
                "生涯日志",
                Error: "读取生涯数据失败：" + exception.Message));
        }
    }

    private static string Format(double? value, string format)
        => value is null ? "N/A" : value.Value.ToString(format, CultureInfo.InvariantCulture);
}