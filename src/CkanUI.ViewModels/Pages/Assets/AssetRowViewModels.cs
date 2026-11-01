using System.Globalization;
using CkanUI.Services.Abstractions.Assets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Assets;

/// <summary>列表里的格式化工具：字节数、时间、缺失值占位。</summary>
internal static class AssetFormat
{
    /// <summary>缺失值占位符。</summary>
    public const string Dash = "—";

    /// <summary>把字节数格式化成 1.2 MB。</summary>
    /// <param name="bytes">字节数。</param>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return Dash;
        }

        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }

    /// <summary>把时间格式化成 yyyy-MM-dd HH:mm。</summary>
    /// <param name="value">时间。</param>
    public static string When(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>空字符串显示成占位符。</summary>
    /// <param name="value">原文。</param>
    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? Dash : value;
}

/// <summary>飞船页的一行。</summary>
public sealed class CraftRowViewModel
{
    /// <summary>构造一行。</summary>
    /// <param name="asset">服务层返回的载具。</param>
    /// <param name="kindLabel">分类译文。</param>
    public CraftRowViewModel(CraftAsset asset, string kindLabel)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Asset = asset;
        KindLabel = kindLabel;
    }

    /// <summary>原始数据，命令里回传给服务层。</summary>
    public CraftAsset Asset { get; }

    /// <summary>载具名。</summary>
    public string Name => Asset.Name;

    /// <summary>分类译文。</summary>
    public string KindLabel { get; }

    /// <summary>版本号文本。</summary>
    public string VersionText => AssetFormat.OrDash(Asset.Version);

    /// <summary>零件数文本。</summary>
    public string PartCountText => Asset.PartCount is { } parts
        ? parts.ToString(CultureInfo.CurrentCulture)
        : AssetFormat.Dash;

    /// <summary>级数文本。</summary>
    public string StageCountText => Asset.StageCount is { } stages
        ? stages.ToString(CultureInfo.CurrentCulture)
        : AssetFormat.Dash;

    /// <summary>文件大小文本。</summary>
    public string SizeText => AssetFormat.Bytes(Asset.SizeBytes);

    /// <summary>修改时间文本。</summary>
    public string ModifiedText => AssetFormat.When(Asset.ModifiedAt);

    /// <summary>.craft 绝对路径。</summary>
    public string FilePath => Asset.FilePath;

    /// <summary>所在目录。</summary>
    public string FolderPath => Path.GetDirectoryName(Asset.FilePath) ?? Asset.FilePath;

    /// <summary>缩略图路径。</summary>
    public string? ThumbnailPath => Asset.ThumbnailPath;

    /// <summary>是否有缩略图。</summary>
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(Asset.ThumbnailPath);
}

/// <summary>旗帜页的一行。</summary>
public sealed partial class FlagRowViewModel : ObservableObject
{
    /// <summary>构造一行。</summary>
    /// <param name="asset">服务层返回的旗帜。</param>
    /// <param name="isDefault">是否当前默认旗帜。</param>
    public FlagRowViewModel(FlagAsset asset, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Asset = asset;
        _isDefault = isDefault;
    }

    /// <summary>原始数据。</summary>
    public FlagAsset Asset { get; }

    /// <summary>旗帜名。</summary>
    public string Name => Asset.Name;

    /// <summary>来源包名。</summary>
    public string PackText => AssetFormat.OrDash(Asset.PackName);

    /// <summary>KSP 口径的配置值。</summary>
    public string ConfigPath => Asset.ConfigPath;

    /// <summary>文件大小文本。</summary>
    public string SizeText => AssetFormat.Bytes(Asset.SizeBytes);

    /// <summary>图片绝对路径。</summary>
    public string FilePath => Asset.FilePath;

    /// <summary>是否当前默认旗帜。</summary>
    [ObservableProperty]
    private bool _isDefault;
}

/// <summary>加载图页的一行。</summary>
public sealed class LoadingImageRowViewModel
{
    /// <summary>构造一行。</summary>
    /// <param name="asset">服务层返回的图片。</param>
    public LoadingImageRowViewModel(LoadingImageAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Asset = asset;
    }

    /// <summary>原始数据。</summary>
    public LoadingImageAsset Asset { get; }

    /// <summary>文件名。</summary>
    public string FileName => Asset.FileName;

    /// <summary>所在目录。</summary>
    public string Folder => Asset.Folder;

    /// <summary>文件大小文本。</summary>
    public string SizeText => AssetFormat.Bytes(Asset.SizeBytes);

    /// <summary>修改时间文本。</summary>
    public string ModifiedText => AssetFormat.When(Asset.ModifiedAt);

    /// <summary>图片绝对路径。</summary>
    public string FilePath => Asset.FilePath;
}
