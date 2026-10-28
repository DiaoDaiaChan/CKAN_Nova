using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>关于框里的一行"标签 / 值"。</summary>
public sealed record AboutField(string Label, string Value);

/// <summary>关于框里的一个外部链接。</summary>
public sealed record AboutLink(string Title, string Url);

/// <summary>关于框要展示的版本与平台事实（由 App 层填充，ViewModels 不认识 CKAN 与 Avalonia）。</summary>
public sealed record AboutInfo(
    string AppName,
    string AppVersion,
    string CoreVersion,
    string RuntimeVersion,
    string OsDescription);

/// <summary>
/// 关于框（12 篇 F-55 / 03 篇 §8）：展示版本矩阵、许可说明与官方链接。
/// 打开外部链接走注入的委托，VM 自己不碰进程与剪贴板（05 篇 §3）。
/// </summary>
public sealed partial class AboutDialogViewModel : DialogViewModelBase
{
    private readonly Func<string, Task<bool>>? _openExternal;

    /// <summary>创建关于框。</summary>
    /// <param name="info">版本与平台事实。</param>
    /// <param name="openExternal">打开外部链接的委托；返回 false 表示打不开。为 null 时点链接只提示。</param>
    public AboutDialogViewModel(AboutInfo info, Func<string, Task<bool>>? openExternal = null)
        : base($"关于 {info.AppName}")
    {
        ArgumentNullException.ThrowIfNull(info);

        Info = info;
        _openExternal = openExternal;
        PrimaryButtonText = "关闭";
        SecondaryButtonText = string.Empty;

        Fields = new AboutField[]
        {
            new("界面版本", info.AppVersion),
            new("核心库版本", info.CoreVersion),
            new("运行时", info.RuntimeVersion),
            new("操作系统", info.OsDescription),
        };
    }

    /// <summary>版本与平台事实。</summary>
    public AboutInfo Info { get; }

    /// <summary>版本矩阵。</summary>
    public IReadOnlyList<AboutField> Fields { get; }

    /// <summary>外部链接（官方仓库、文档、问题反馈）。</summary>
    public IReadOnlyList<AboutLink> Links => DefaultLinks;

    /// <summary>
    /// 许可说明。两段必须分开写：CKAN UI 自身是 GPL-3.0，而内置的 CKAN 核心库沿用上游 MIT
    /// （external/CKAN/LICENSE.md），把两者混成一句是对上游许可的错述。
    /// </summary>
    public string LicenseText =>
        "CKAN UI 以 GPL-3.0 许可发布；内置的 CKAN 核心库沿用上游 MIT 许可，第三方组件清单见发布包内的 THIRD-PARTY-NOTICES.txt。本程序不提供任何担保。";


    /// <summary>默认链接集合（不随实例变化，因此是静态只读）。</summary>
    public static IReadOnlyList<AboutLink> DefaultLinks { get; } = new AboutLink[]
    {
        new("CKAN 官方仓库", "https://github.com/KSP-CKAN/CKAN"),
        new("CKAN 文档", "https://github.com/KSP-CKAN/CKAN/wiki"),
        new("问题反馈", "https://github.com/KSP-CKAN/CKAN/issues"),
    };

    /// <summary>打开一个外部链接；失败时在按钮上方给出可读提示。</summary>
    [RelayCommand]
    private async Task OpenLinkAsync(AboutLink? link)
    {
        if (link is null || string.IsNullOrWhiteSpace(link.Url))
        {
            return;
        }

        if (_openExternal is null)
        {
            ValidationError = "当前环境没有可用的浏览器通道，请手动复制链接。";
            return;
        }

        try
        {
            if (!await _openExternal(link.Url).ConfigureAwait(true))
            {
                ValidationError = $"无法打开链接：{link.Url}";
            }
        }
        catch (Exception ex)
        {
            ValidationError = $"无法打开链接：{ex.Message}";
        }
    }
}
