using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>导出表单：目标路径、是否锁定版本、是否打包下载包、是否用 recommends 语义。</summary>
public sealed partial class ExportFormViewModel : ObservableObject
{
    /// <summary>目标路径输入框的提示文案。</summary>
    public const string PathHint = "例如：D:\\CKAN\\我的模组列表.ckan（省略 .ckan 后缀会自动补上）";

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    private bool _withVersions = true;

    [ObservableProperty]
    private bool _bundleDownloads;

    [ObservableProperty]
    private bool _useRecommends;

    [ObservableProperty]
    private string? _validationText;

    /// <summary>目标路径是否为空（空则不能导出）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(TargetPath);

    /// <summary>是否有表单校验错误可显示。</summary>
    public bool HasValidation => !string.IsNullOrWhiteSpace(ValidationText);

    /// <summary>是否把下载包一起打包成 zip。</summary>
    public bool HasBundleDownloads => BundleDownloads;

    /// <summary>按当前表单生成导出请求。</summary>
    /// <returns>导出请求。</returns>
    public ModPackExportRequest ToRequest() => new(TargetPath.Trim(), WithVersions, BundleDownloads, UseRecommends);

    /// <summary>清空表单并恢复默认选项。</summary>
    public void Reset()
    {
        TargetPath = string.Empty;
        WithVersions = true;
        BundleDownloads = false;
        UseRecommends = false;
        ValidationText = null;
    }

    partial void OnTargetPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ValidationText = null;
        }
    }

    partial void OnBundleDownloadsChanged(bool value) => OnPropertyChanged(nameof(HasBundleDownloads));

    partial void OnValidationTextChanged(string? value) => OnPropertyChanged(nameof(HasValidation));
}
