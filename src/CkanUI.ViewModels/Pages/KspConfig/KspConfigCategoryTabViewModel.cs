using CkanUI.Services.Abstractions.KspConfig;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.KspConfig;

/// <summary>
/// KSP 设置分类页签模型。
/// </summary>
public sealed partial class KspConfigCategoryTabViewModel : ObservableObject
{
    public KspConfigCategoryTabViewModel(string title, KspSettingCategory? category, string icon)
    {
        Title = title;
        Category = category;
        Icon = icon;
    }

    public string Title { get; }

    public KspSettingCategory? Category { get; }

    public string Icon { get; }

    [ObservableProperty]
    private int _count;
}
