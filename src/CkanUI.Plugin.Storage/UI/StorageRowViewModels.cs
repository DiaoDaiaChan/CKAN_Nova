using CkanUI.Services.Abstractions.Profiles;

using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.Plugin.Storage.UI;

/// <summary>实例列表里的一行；只把服务返回的字节数翻译成人能读的文字，不做二次统计。</summary>
public sealed partial class InstanceStorageRowViewModel : ObservableObject
{
    private const string NotAvailable = "—";

    /// <summary>用一条服务快照构造行。</summary>
    /// <param name="row">服务返回的实例占用。</param>
    public InstanceStorageRowViewModel(InstanceStorageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Model = row;
        Profiles = row.Profiles
            .Select(profile => new ProfileStorageRowViewModel(profile))
            .ToArray();
        HasProfiles = Profiles.Count > 0;
        ProfilesText = BuildProfilesText(row.Profiles);
    }

    /// <summary>原始快照：页面展示都从这里来，测试也能直接核对数字。</summary>
    public InstanceStorageRow Model { get; }

    /// <summary>实例名。</summary>
    public string Name => Model.InstanceName;

    /// <summary>游戏根目录。</summary>
    public string GameDir => Model.GameDir;

    /// <summary>三项合计。</summary>
    public string TotalText => StoragePageViewModel.BytesText(Model.TotalBytes);

    /// <summary>当前生效的 GameData 体积。</summary>
    public string GameDataText => StoragePageViewModel.BytesText(Model.LiveGameDataBytes);

    /// <summary>当前生效的 CKAN 目录体积。</summary>
    public string CkanText => StoragePageViewModel.BytesText(Model.CkanBytes);

    /// <summary>各配置档合计体积。</summary>
    public string ProfileText => StoragePageViewModel.BytesText(Model.ProfileBytes);

    /// <summary>所在卷剩余空间；服务取不到时显示「—」而不是 0。</summary>
    public string FreeText => StoragePageViewModel.BytesText(Model.FreeBytes);

    /// <summary>配置档数量（服务口径：不含当前生效那一档的虚拟条目）。</summary>
    public int ProfileCount => Model.ProfileCount;

    /// <summary>一行列出各配置档名与体积，当前生效那档标「使用中」。</summary>
    public string ProfilesText { get; }

    /// <summary>这个实例有没有配置档目录内容。</summary>
    public bool HasProfiles { get; }

    /// <summary>配置档明细（右侧面板用）。</summary>
    public IReadOnlyList<ProfileStorageRowViewModel> Profiles { get; }

    /// <summary>列表选中态；由页面维护，行本身不决定谁能被选中。</summary>
    [ObservableProperty]
    private bool _isSelected;

    private static string BuildProfilesText(IReadOnlyList<ProfileStorageRow> profiles)
    {
        if (profiles.Count == 0)
        {
            return "无配置档";
        }

        return string.Join(
            "；",
            profiles.Select(profile =>
                profile.IsActive
                    ? $"{profile.ProfileName} {StoragePageViewModel.BytesText(profile.Bytes)}（使用中）"
                    : $"{profile.ProfileName} {StoragePageViewModel.BytesText(profile.Bytes)}"));
    }
}

/// <summary>单个配置档的体积行。</summary>
public sealed class ProfileStorageRowViewModel
{
    /// <summary>用服务返回的配置档占用构造。</summary>
    /// <param name="row">配置档占用。</param>
    public ProfileStorageRowViewModel(ProfileStorageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Name = row.ProfileName;
        IsActive = row.IsActive;
        FileCount = row.FileCount;
        SizeText = StoragePageViewModel.BytesText(row.Bytes);
        DetailText = row.FileCount == 1 ? "1 个文件" : $"{row.FileCount} 个文件";
    }

    /// <summary>配置档名。</summary>
    public string Name { get; }

    /// <summary>是否当前生效（CKAN 正在用这一档）。</summary>
    public bool IsActive { get; }

    /// <summary>文件数。</summary>
    public int FileCount { get; }

    /// <summary>体积文字。</summary>
    public string SizeText { get; }

    /// <summary>文件数文字。</summary>
    public string DetailText { get; }
}
