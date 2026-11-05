using System.Globalization;
using Avalonia.Media.Imaging;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.Plugin.SpaceDock;

/// <summary>SpaceDock 图片信息流卡片的显示投影（列表整体替换，图片异步回填）。</summary>
public sealed partial class SpaceDockModItem : ObservableObject
{
    /// <summary>创建卡片。</summary>
    public SpaceDockModItem(SpaceDockModInfo mod, string metaText)
    {
        ArgumentNullException.ThrowIfNull(mod);
        Mod = mod;
        MetaText = metaText;
    }

    /// <summary>原始模组数据。</summary>
    public SpaceDockModInfo Mod { get; }

    /// <summary>模组名。</summary>
    public string Name => Mod.Name;

    /// <summary>作者；SpaceDock 未填时留空，不硬编码中文兜底。</summary>
    public string Author => string.IsNullOrWhiteSpace(Mod.Author) ? string.Empty : Mod.Author;

    /// <summary>一句话描述，作为卡片悬浮提示。</summary>
    public string ShortDescription => Mod.ShortDescription;

    /// <summary>下载量（千分位）。</summary>
    public string DownloadsText => Mod.Downloads.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>卡片底部元信息（已按当前语言格式化）。</summary>
    public string MetaText { get; }

    /// <summary>最新版本摘要；详情面板之外的旧列表仍可使用。</summary>
    public string LatestVersionText => Mod.LatestVersion is { } version
        ? string.IsNullOrWhiteSpace(version.GameVersion)
            ? version.FriendlyVersion
            : $"{version.FriendlyVersion} · KSP {version.GameVersion}"
        : string.Empty;

    /// <summary>卡片左上角的版本徽标。</summary>
    public string VersionBadgeText => Mod.LatestVersion?.FriendlyVersion ?? string.Empty;

    /// <summary>是否有版本徽标。</summary>
    public bool HasVersionBadge => !string.IsNullOrWhiteSpace(VersionBadgeText);

    /// <summary>SpaceDock 详情页链接。</summary>
    public string Url => Mod.Url;

    /// <summary>异步加载的封面图；未加载完成时为 null，卡片显示占位文案。</summary>
    [ObservableProperty]
    private Bitmap? _image;

    /// <summary>换上新的封面图，并释放上一张：整幅解码的位图不 Dispose 会一直占着非托管像素内存。</summary>
    internal void ReplaceImage(Bitmap? image)
    {
        var previous = Image;
        if (ReferenceEquals(previous, image))
        {
            return;
        }

        Image = image;
        previous?.Dispose();
    }

    /// <summary>释放封面图并摘掉绑定，供列表整批重建时调用。</summary>
    internal void ReleaseImage() => ReplaceImage(null);
}
