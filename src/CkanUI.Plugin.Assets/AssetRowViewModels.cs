using System.Globalization;
using CkanUI.Services.Abstractions.Assets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Assets;

/// <summary>列表里的格式化工具：字节数、时间、缺失值占位。</summary>
public static class AssetFormat
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

/// <summary>加载图页目录清单里的一行。</summary>
/// <remarks>清单以前直接显示绝对路径，240 宽的侧栏只露出「D:/SteamLibrary/...」，
/// 用户看不出这是哪个目录，更看不出它是模组要塞还是自己放图的地方，所以这里拆出目录名 + 来源。</remarks>
public sealed class LoadingImageFolderRowViewModel
{
    /// <summary>构造一行。</summary>
    /// <param name="folderPath">目录绝对路径。</param>
    /// <param name="info">目录来源信息；自定义目录传 null。</param>
    public LoadingImageFolderRowViewModel(string folderPath, LoadingImageFolderInfo? info = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        FolderPath = folderPath;
        Provider = info?.Provider;
        IsRecursive = info?.Recursive ?? false;
    }

    /// <summary>目录绝对路径。</summary>
    public string FolderPath { get; }

    /// <summary>目录名（用于列表显示；根目录回落到完整路径）。</summary>
    public string Label
    {
        get
        {
            var trimmed = FolderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    /// <summary>来源模组名；自定义目录为空。</summary>
    public string? Provider { get; }

    /// <summary>是否认出来源。</summary>
    public bool HasProvider => !string.IsNullOrEmpty(Provider);

    /// <summary>来源徽章文本。</summary>
    public string ProviderText => Provider ?? string.Empty;

    /// <summary>是否连子目录一起扫（LoadingScreenManager 的 ignoreSubfolders = False）。</summary>
    public bool IsRecursive { get; }
}

/// <summary>加载图页的一行。</summary>
public sealed class LoadingImageRowViewModel
{
    private readonly string _disableLabel;
    private readonly string _enableLabel;

    /// <summary>构造一行。</summary>
    /// <param name="asset">服务层返回的图片。</param>
    /// <param name="toggle">启停回调，由页面接到服务层。</param>
    /// <param name="disableLabel">「停用」按钮文案。</param>
    /// <param name="enableLabel">「启用」按钮文案。</param>
    public LoadingImageRowViewModel(
        LoadingImageAsset asset,
        Func<LoadingImageRowViewModel, Task>? toggle,
        string disableLabel,
        string enableLabel)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Asset = asset;
        _disableLabel = disableLabel;
        _enableLabel = enableLabel;
        ToggleCommand = new AsyncRelayCommand(() => toggle?.Invoke(this) ?? Task.CompletedTask);
    }

    /// <summary>原始数据。</summary>
    public LoadingImageAsset Asset { get; }

    /// <summary>游戏是否还会读这张图。</summary>
    public bool IsEnabled => Asset.IsEnabled;

    /// <summary>是否已停用（卡片上盖一层遮罩）。</summary>
    public bool IsDisabled => !Asset.IsEnabled;

    /// <summary>启停按钮文案：启用中显示「停用」，已停用显示「启用」。</summary>
    public string ToggleText => Asset.IsEnabled ? _disableLabel : _enableLabel;

    /// <summary>启停命令。</summary>
    public IAsyncRelayCommand ToggleCommand { get; }

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

    /// <summary>来源模组名（自定义目录为空串）。</summary>
    public string ProviderText => Asset.Provider ?? string.Empty;

    /// <summary>是否有来源模组。</summary>
    public bool HasProvider => !string.IsNullOrEmpty(Asset.Provider);

    /// <summary>格式徽章文本，例如 PNG / DDS。</summary>
    public string FormatText => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
}
