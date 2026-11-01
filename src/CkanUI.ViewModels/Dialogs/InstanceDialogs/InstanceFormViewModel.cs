using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Dialogs.InstanceDialogs;

/// <summary>
/// 实例表单对话框视图模型（03 篇 §8 D-06 / 06 篇 UI-32）：新增、克隆、重命名三种模式共用一套字段与校验。
/// </summary>
/// <remarks>
/// <para>提交动作由页面接管：表单只负责「收集 + 校验 + 发出请求」，真正的服务调用留在
/// <c>InstancesPageViewModel</c>，这样同一份表单既能以内联面板呈现（M1 现状），
/// 也能在通用模态宿主就绪后原样塞进对话框窗口（不重复实现）。</para>
/// <para>校验规则全部在 <see cref="InstanceFormValidator"/> 里，本类只做状态与文案。</para>
/// </remarks>
public sealed partial class InstanceFormViewModel : ObservableObject
{
    private readonly List<string> _existingNames = new();

    /// <summary>创建表单。<paramref name="sourceName"/> 为克隆/重命名的源实例名。</summary>
    public InstanceFormViewModel(InstanceFormMode mode, string? sourceName = null, string? sourcePath = null)
    {
        Mode = mode;
        SourceName = sourceName;
        SourcePath = sourcePath;
        if (mode == InstanceFormMode.Clone && !string.IsNullOrWhiteSpace(sourceName))
        {
            Name = $"{sourceName} - 副本";
        }
    }

    /// <summary>表单模式。</summary>
    public InstanceFormMode Mode { get; }

    /// <summary>克隆/重命名的源实例名；新增模式为 null。</summary>
    public string? SourceName { get; }

    /// <summary>源实例的游戏目录（克隆时用于推导目标目录并提示）。</summary>
    public string? SourcePath { get; }

    /// <summary>对话框标题。</summary>
    public string Title => Mode switch
    {
        InstanceFormMode.Add => "添加游戏实例",
        InstanceFormMode.Clone => "克隆游戏实例",
        _ => "重命名游戏实例",
    };

    /// <summary>主按钮文案。</summary>
    public string PrimaryButtonText => Mode switch
    {
        InstanceFormMode.Add => "添加",
        InstanceFormMode.Clone => "开始克隆",
        _ => "重命名",
    };

    /// <summary>是否显示游戏目录输入（克隆/重命名的目录由服务层推导，不暴露输入）。</summary>
    public bool ShowsPath => Mode == InstanceFormMode.Add;

    /// <summary>是否显示源实例信息行。</summary>
    public bool ShowsSource => Mode != InstanceFormMode.Add;

    /// <summary>源实例信息文案。</summary>
    public string SourceSummary => Mode == InstanceFormMode.Add
        ? string.Empty
        : string.IsNullOrWhiteSpace(SourcePath)
            ? $"源实例：{SourceName}"
            : $"源实例：{SourceName}（{SourcePath}）";

    /// <summary>模式说明（告诉用户这个动作会/不会碰磁盘文件）。</summary>
    public string HintText => Mode switch
    {
        InstanceFormMode.Add => "只登记为游戏实例，不复制不修改文件。",
        InstanceFormMode.Clone => "会把整个游戏目录复制到同级新目录。",
        _ => "只改实例记录里的名称，不动游戏目录。",
    };

    /// <summary>实例名称（新增时默认取目录名，克隆时默认「源名 - 副本」）。</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>游戏目录（仅新增模式可见）。</summary>
    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>校验错误文案；非空时主按钮不提交。</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>是否有校验错误。</summary>
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    /// <summary>提交请求（页面接管真正的服务调用）。</summary>
    public Func<Task>? SubmitRequested { get; set; }

    /// <summary>取消请求。</summary>
    public Action? CancelRequested { get; set; }

    /// <summary>注入已注册实例名，用于重名检查。</summary>
    public void SetExistingNames(IEnumerable<string>? names)
    {
        _existingNames.Clear();
        if (names is null)
        {
            return;
        }

        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _existingNames.Add(name);
            }
        }
    }

    /// <summary>执行校验并把结果显示到 <see cref="ValidationError"/>；返回是否通过。</summary>
    public bool Validate()
    {
        var error = Mode == InstanceFormMode.Add
            ? InstanceFormValidator.ValidatePath(Path) ?? InstanceFormValidator.ValidateName(Name, _existingNames)
            : InstanceFormValidator.ValidateName(Name, _existingNames, SourceName);

        ValidationError = error;
        return error is null;
    }

    /// <summary>主按钮：先校验，通过后把提交请求交给页面。</summary>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!Validate())
        {
            return;
        }

        if (SubmitRequested is { } handler)
        {
            await handler();
        }
    }

    /// <summary>取消按钮。</summary>
    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }

    /// <summary>名称变更后清掉上一次的错误提示（输入即视为"已修改"）。</summary>
    partial void OnNameChanged(string value) => ValidationError = null;

    /// <summary>路径变更后清掉上一次的错误提示。</summary>
    partial void OnPathChanged(string value) => ValidationError = null;

    partial void OnValidationErrorChanged(string? value) => OnPropertyChanged(nameof(HasValidationError));
}
