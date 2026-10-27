using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 兼容版本条目视图模型。
/// </summary>
public sealed partial class CompatibleVersionItemViewModel : ObservableObject
{
    public CompatibleVersionItemViewModel(
        string versionString,
        bool isSelected,
        bool isRealVersion,
        bool isWildcard)
    {
        VersionString = versionString;
        _isSelected = isSelected;
        IsRealVersion = isRealVersion;
        IsWildcard = isWildcard;
    }

    /// <summary>版本字符串（例如 "1.12.5" 或 "1.12"）。</summary>
    public string VersionString { get; }

    /// <summary>是否为真实游戏原生版本（原生版本不可取消勾选）。</summary>
    public bool IsRealVersion { get; }

    /// <summary>是否为通配符/主要版本范围。</summary>
    public bool IsWildcard { get; }

    /// <summary>是否被用户勾选为兼容版本。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>版本显示说明标签。</summary>
    public string TagText => IsRealVersion ? "原生版本" : (IsWildcard ? "主版本通配" : "已知版本");

    /// <summary>原生版本只读锁定，不可在界面上取消选择。</summary>
    public bool IsReadOnly => IsRealVersion;
}
