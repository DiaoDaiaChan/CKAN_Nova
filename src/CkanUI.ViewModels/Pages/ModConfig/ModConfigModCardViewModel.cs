using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>左侧 mod 配置卡片（图标/名字/已装版本/状态徽标/N 项已改）。</summary>
public sealed class ModConfigModCardViewModel
{
    /// <summary>构造卡片。</summary>
    /// <param name="snapshot">服务层给出的 mod 快照。</param>
    public ModConfigModCardViewModel(ModConfigModSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        AdapterId = snapshot.AdapterId;
        Name = snapshot.Name;
        Homepage = snapshot.Homepage ?? string.Empty;
        VersionText = string.IsNullOrWhiteSpace(snapshot.InstalledVersion) ? "版本未知" : "v" + snapshot.InstalledVersion;
        StateText = DescribeState(snapshot.State);
        BadgeText = DescribeBadge(snapshot.State);
        ModifiedText = snapshot.ModifiedCount > 0 ? snapshot.ModifiedCount + " 项已改" : string.Empty;
        SupportedRangeText = snapshot.SupportedVersionRangeText is { Length: > 0 } range ? "适配器支持 " + range : string.Empty;
        WarningText = BuildWarning(snapshot);
    }

    /// <summary>原始快照。</summary>
    public ModConfigModSnapshot Snapshot { get; }

    /// <summary>adapter id。</summary>
    public string AdapterId { get; }

    /// <summary>mod 名（来自 adapter）。</summary>
    public string Name { get; }

    /// <summary>已装版本（"v1.31.0" 或 "版本未知"）。</summary>
    public string VersionText { get; }

    /// <summary>状态文案。</summary>
    public string StateText { get; }

    /// <summary>状态徽标（短文案）。</summary>
    public string BadgeText { get; }

    /// <summary>已改条数文案（0 项时为空串）。</summary>
    public string ModifiedText { get; }

    /// <summary>是否有已改项。</summary>
    public bool HasModified => Snapshot.ModifiedCount > 0;

    /// <summary>适配器支持的版本区间文案。</summary>
    public string SupportedRangeText { get; }

    /// <summary>首页链接（可为空串）。</summary>
    public string Homepage { get; }

    /// <summary>加载期/状态诊断里最需要用户知道的那条。</summary>
    public string WarningText { get; }

    /// <summary>是否有告警文案。</summary>
    public bool HasWarning => WarningText.Length > 0;

    /// <summary>标题文案："KSP Community Fixes v1.31.0"。</summary>
    public string HeaderText => VersionText.Length > 0 ? Name + " " + VersionText : Name;

    /// <summary>该 mod 当前是否可编辑。</summary>
    public bool IsEditable => Snapshot.IsEditable;

    private static string DescribeState(ModConfigAdapterState state) => state switch
    {
        ModConfigAdapterState.Editable => "可编辑",
        ModConfigAdapterState.CreatableByTemplate => "可按模板创建",
        ModConfigAdapterState.ReadOnlyVersion => "版本超出适配器范围（只读）",
        ModConfigAdapterState.ReadOnlyRaw => "只读（原始视图）",
        ModConfigAdapterState.NotInstalled => "未安装",
        _ => "未知状态",
    };

    private static string DescribeBadge(ModConfigAdapterState state) => state switch
    {
        ModConfigAdapterState.Editable => "已适配",
        ModConfigAdapterState.CreatableByTemplate => "待创建",
        ModConfigAdapterState.ReadOnlyVersion => "只读",
        ModConfigAdapterState.ReadOnlyRaw => "只读",
        ModConfigAdapterState.NotInstalled => "未安装",
        _ => string.Empty,
    };

    private static string BuildWarning(ModConfigModSnapshot snapshot)
    {
        if (snapshot.Diagnostics is not { Count: > 0 } diagnostics)
        {
            return string.Empty;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.IsError)
            {
                return diagnostic.Message;
            }
        }

        return diagnostics[0].Message;
    }
}
