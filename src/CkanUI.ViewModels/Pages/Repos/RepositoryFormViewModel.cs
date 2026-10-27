using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Repos;

/// <summary>新增仓库的内联表单（03 篇 §6 V-04 的「添加仓库」）。</summary>
public sealed partial class RepositoryFormViewModel : ObservableObject
{
    /// <summary>仓库名，用户可见。保存前会做 Trim。</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>仓库地址（http/https）。保存前会做 Trim。</summary>
    [ObservableProperty]
    private string _uri = string.Empty;

    /// <summary>前端校验提示（例如「请填写仓库名称」），保存成功或重置后清空。</summary>
    [ObservableProperty]
    private string? _validationText;

    /// <summary>是否有校验提示。</summary>
    public bool HasValidation => !string.IsNullOrEmpty(ValidationText);

    /// <summary>两个输入框都为空（视图据此禁用「添加」按钮）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Uri);

    /// <summary>转成服务层请求（只做 Trim，非法值交给服务层判定，保持唯一权威）。</summary>
    public RepositoryEditRequest ToRequest() => new(Name?.Trim(), Uri?.Trim());

    /// <summary>清空输入与提示（取消或保存成功后调用）。</summary>
    public void Reset()
    {
        Name = string.Empty;
        Uri = string.Empty;
        ValidationText = null;
    }

    partial void OnValidationTextChanged(string? value) => OnPropertyChanged(nameof(HasValidation));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnUriChanged(string value) => OnPropertyChanged(nameof(IsEmpty));
}
