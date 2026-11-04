using System.Globalization;
using CkanUI.Plugin.ModPackShare.Sources;
using CkanUI.Services.Abstractions.Profiles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.Plugin.ModPackShare.UI;

/// <summary>
/// 左栏配置档列表的一行。<see cref="InstanceProfileInfo"/> 的展示投影：
/// 服务说多少就是多少，缺数据一律用破折号，不拿 0 冒充「没有模组」。
/// </summary>
public sealed class ModPackRowViewModel : ObservableObject
{
    private InstanceProfileInfo _model;
    private bool _isSelected;

    /// <summary>构造一行。</summary>
    public ModPackRowViewModel(InstanceProfileInfo model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>服务返回的原始快照；切换/删除时用它的 Name。</summary>
    public InstanceProfileInfo Model => _model;

    /// <summary>配置档名（也是目录名）。</summary>
    public string Name => _model.Name;

    /// <summary>是否当前生效：它的 GameData/CKAN 正在游戏根目录下。</summary>
    public bool IsActive => _model.IsActive;

    /// <summary>是否被指定为「启动游戏时自动切到这一档」。</summary>
    public bool IsLaunchProfile => _model.IsLaunchProfile;

    /// <summary>占用空间；服务没算出来（0）时给破折号。</summary>
    public string SizeText => _model.SizeBytes > 0
        ? GitHubModPackSource.FormatBytes(_model.SizeBytes)
        : "—";

    /// <summary>模组数；服务无法确定时给破折号而不是 0。</summary>
    public string ModCountText => _model.ModCount > 0 ? $"{_model.ModCount} 个模组" : "模组 —";

    /// <summary>来源（.ckan 路径或 URL）；手工创建的配置档明确写「手工创建」。</summary>
    public string SourceText => string.IsNullOrWhiteSpace(_model.Source) ? "手工创建" : _model.Source!;

    /// <summary>最近一次使用时间；未知给破折号。</summary>
    public string LastUsedText => _model.LastUsedUtc is { } utc
        ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : "—";

    /// <summary>配置档目录（详情区显示完整路径）。</summary>
    public string Directory => _model.Directory;

    /// <summary>一行摘要：文件数 + 大小，列表里不展开。</summary>
    public string DetailText => _model.FileCount > 0
        ? $"{SizeText} · {_model.FileCount} 个文件"
        : $"{SizeText} · 空配置档";

    /// <summary>由页面维护的选中态（只影响行样式）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>刷新列表后更新投影，避免还显示旧的模组数和时间。</summary>
    public void Update(InstanceProfileInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Equals(_model, model))
        {
            return;
        }

        _model = model;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsLaunchProfile));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ModCountText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(LastUsedText));
        OnPropertyChanged(nameof(Directory));
        OnPropertyChanged(nameof(DetailText));
    }
}
