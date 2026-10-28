using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.ModPacks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// modpack 编辑器页面（UI-24 的编辑部分）：读 / 改 / 校验 / 存一个 <c>.ckan</c> 文件。
/// </summary>
/// <remarks>
/// 分层口径：本页只认 <see cref="IModPackService"/> 与 <see cref="ModPackEditorDocument"/>，
/// 不引用任何 CKAN 类型（.ckan 的字段名与合法性全由服务层决定），因此可以脱离 Core 单测。
/// 保存前一定先跑 <see cref="IModPackService.ValidateEditor"/>：有 Error 级问题就逐条列中文原因并拒绝写盘。
/// </remarks>
public sealed partial class ModPackEditorPageViewModel : PageViewModel, IUnsavedChangesAware
{
    private readonly IModPackService _service;
    private bool _suppressDirty;

    /// <summary>设计时 / ViewLocator 用的无参构造：服务不可用时页面仍然能渲染，只是操作一律失败。</summary>
    public ModPackEditorPageViewModel()
        : this(new UnavailableModPackService())
    {
    }

    /// <summary>用导入导出服务构造页面。</summary>
    /// <param name="service">modpack 服务（编辑器能力是 <see cref="IModPackService"/> 上的 DIM 成员）。</param>
    public ModPackEditorPageViewModel(IModPackService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        Authors.CollectionChanged += OnCollectionChanged;
        Licenses.CollectionChanged += OnCollectionChanged;
        Relations.CollectionChanged += OnCollectionChanged;
        Relations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyRelations));

        ApplyDocument(new ModPackEditorDocument
        {
            Kind = "metapackage",
            ReleaseStatus = "stable",
        });
        IsDirty = false;
        SetStatus("填写表单后点「校验」，或直接「保存」。");
    }

    /// <inheritdoc />
    public override string Title => ModPackEditorText.PageTitle;

    /// <summary>目标 <c>.ckan</c> 文件路径。</summary>
    [ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>模组标识（小写）。</summary>
    [ObservableProperty]
    private string _identifier = string.Empty;

    /// <summary>显示名。</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>摘要。</summary>
    [ObservableProperty]
    private string _abstract = string.Empty;

    /// <summary>详细描述。</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>版本号。</summary>
    [ObservableProperty]
    private string _version = string.Empty;

    /// <summary>发布状态：stable / testing / development。</summary>
    [ObservableProperty]
    private string _releaseStatus = "stable";

    /// <summary>包类型：metapackage / package / dlc。</summary>
    [ObservableProperty]
    private string _kind = "metapackage";

    /// <summary>目标游戏版本。</summary>
    [ObservableProperty]
    private string _kspVersion = string.Empty;

    /// <summary>游戏版本下限。</summary>
    [ObservableProperty]
    private string _kspVersionMin = string.Empty;

    /// <summary>游戏版本上限。</summary>
    [ObservableProperty]
    private string _kspVersionMax = string.Empty;

    /// <summary>元数据规范版本（留空表示保存时由服务层自动推导）。</summary>
    [ObservableProperty]
    private string _specVersion = string.Empty;

    /// <summary>下载地址（kind=package 时必填）。</summary>
    [ObservableProperty]
    private string _downloadUrl = string.Empty;

    /// <summary>待添加的作者。</summary>
    [ObservableProperty]
    private string _newAuthor = string.Empty;

    /// <summary>待添加的许可证。</summary>
    [ObservableProperty]
    private string _newLicense = string.Empty;

    /// <summary>状态栏文字。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误栏文字；空串表示没有错误。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>是否正在读写磁盘。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>忙碌提示文字。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>表单是否有未保存的改动。</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>外壳底部「未保存」提示条：模组包表单还有没保存的改动。</summary>
    bool IUnsavedChangesAware.HasUnsavedChanges => IsDirty;

    /// <summary>作者行。</summary>
    public ObservableCollection<ModPackEditorRowViewModel> Authors { get; } = new();

    /// <summary>许可证行。</summary>
    public ObservableCollection<ModPackEditorRowViewModel> Licenses { get; } = new();

    /// <summary>关系行（依赖 / 推荐 / 建议 / 冲突 / 支持）。</summary>
    public ObservableCollection<ModPackRelationRowViewModel> Relations { get; } = new();

    /// <summary>最近一次校验的问题（中文、已排序：错误在前）。</summary>
    public ObservableCollection<string> Issues { get; } = new();

    /// <summary>关系表为空时显示空状态。</summary>
    public bool ShowEmptyRelations => Relations.Count == 0;

    /// <summary>错误栏是否可见。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>问题列表是否可见。</summary>
    public bool HasIssues => Issues.Count > 0;

    /// <summary>问题数量文案。</summary>
    public string IssueSummary => Issues.Count == 0 ? string.Empty : "共 " + Issues.Count + " 条问题";

    /// <summary>发布状态下拉项。</summary>
    public IReadOnlyList<string> ReleaseStatusOptions => ModPackEditorText.ReleaseStatusOptions;

    /// <summary>包类型下拉项。</summary>
    public IReadOnlyList<string> KindOptions => ModPackEditorText.KindOptions;

    /// <summary>关系类型下拉项。</summary>
    public IReadOnlyList<string> RelationTypeOptions => ModPackEditorText.RelationTypeOptions;

    /// <summary>语义模式下拉项。</summary>
    public IReadOnlyList<string> ModeOptions => ModPackEditorText.ModeOptions;

    /// <summary>把当前表单投影成 DTO（保存与校验都用它，保证两者看的是同一份数据）。</summary>
    public ModPackEditorDocument ToDocument() => new()
    {
        FilePath = (FilePath ?? string.Empty).Trim(),
        Identifier = (Identifier ?? string.Empty).Trim(),
        Name = (Name ?? string.Empty).Trim(),
        Abstract = (Abstract ?? string.Empty).Trim(),
        Description = Description ?? string.Empty,
        Authors = Authors.Select(row => row.ToValue()).Where(value => value.Length > 0).ToList(),
        Version = (Version ?? string.Empty).Trim(),
        Licenses = Licenses.Select(row => row.ToValue()).Where(value => value.Length > 0).ToList(),
        ReleaseStatus = (ReleaseStatus ?? string.Empty).Trim(),
        Kind = (Kind ?? string.Empty).Trim(),
        KspVersion = (KspVersion ?? string.Empty).Trim(),
        KspVersionMin = (KspVersionMin ?? string.Empty).Trim(),
        KspVersionMax = (KspVersionMax ?? string.Empty).Trim(),
        SpecVersion = (SpecVersion ?? string.Empty).Trim(),
        DownloadUrl = (DownloadUrl ?? string.Empty).Trim(),
        Relations = Relations.Select(row => row.ToRelation()).ToList(),
    };

    /// <summary>用 DTO 覆盖表单（加载 / 新建都走这里）。</summary>
    public void ApplyDocument(ModPackEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _suppressDirty = true;
        try
        {
            FilePath = document.FilePath ?? string.Empty;
            Identifier = document.Identifier ?? string.Empty;
            Name = document.Name ?? string.Empty;
            Abstract = document.Abstract ?? string.Empty;
            Description = document.Description ?? string.Empty;
            Version = document.Version ?? string.Empty;
            ReleaseStatus = string.IsNullOrWhiteSpace(document.ReleaseStatus) ? "stable" : document.ReleaseStatus;
            Kind = string.IsNullOrWhiteSpace(document.Kind) ? "metapackage" : document.Kind;
            KspVersion = document.KspVersion ?? string.Empty;
            KspVersionMin = document.KspVersionMin ?? string.Empty;
            KspVersionMax = document.KspVersionMax ?? string.Empty;
            SpecVersion = document.SpecVersion ?? string.Empty;
            DownloadUrl = document.DownloadUrl ?? string.Empty;

            Authors.Clear();
            foreach (var row in ModPackEditorRowViewModel.FromValues(document.Authors, "作者"))
            {
                Authors.Add(row);
            }

            Licenses.Clear();
            foreach (var row in ModPackEditorRowViewModel.FromValues(document.Licenses, "许可证"))
            {
                Licenses.Add(row);
            }

            Relations.Clear();
            foreach (var relation in document.Relations ?? Array.Empty<ModPackRelation>())
            {
                if (relation is not null)
                {
                    Relations.Add(new ModPackRelationRowViewModel(relation));
                }
            }
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    /// <summary>把校验结果搬到界面上（错误在前，逐条中文）。</summary>
    public void ApplyIssues(IReadOnlyList<ModPackEditorIssue> issues)
    {
        Issues.Clear();
        if (issues is not null)
        {
            foreach (var issue in issues)
            {
                Issues.Add(ModPackEditorText.DescribeIssue(issue));
            }
        }

        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssueSummary));
    }

    /// <summary>读一个 <c>.ckan</c> 文件填进表单。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _service.LoadEditorAsync(FilePath ?? string.Empty, CancellationToken.None);
            if (!result.IsSuccess || result.Value is null)
            {
                SetError(ModPacksErrorText.Compose(result.Message, "读取 .ckan 失败。", result.Retryable)
                         + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : Environment.NewLine + result.Detail));
                return;
            }

            ApplyDocument(result.Value);
            ApplyIssues(_service.ValidateEditor(result.Value));
            IsDirty = false;
            SetStatus("已读取：" + result.Value.FilePath);
        }, "读取 .ckan 失败", "正在读取…");
    }

    /// <summary>先校验，再写盘；有 Error 级问题就拒绝并逐条列出原因。</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            var document = ToDocument();
            var issues = _service.ValidateEditor(document);
            ApplyIssues(issues);

            var errors = issues.Where(issue => issue.Severity == ModPackEditorIssueSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                SetError("校验未通过，没有保存。请按下面列出的原因逐条修正。");
                return;
            }

            var result = await _service.SaveEditorAsync(document, CancellationToken.None);
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
            {
                SetError(ModPacksErrorText.Compose(result.Message, "保存 .ckan 失败。", result.Retryable)
                         + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : Environment.NewLine + result.Detail));
                return;
            }

            FilePath = result.Value!;
            IsDirty = false;
            SetStatus("已保存：" + result.Value);
        }, "保存 .ckan 失败", "正在保存…");
    }

    /// <summary>只校验不保存。</summary>
    [RelayCommand]
    private void Validate()
    {
        var issues = _service.ValidateEditor(ToDocument());
        ApplyIssues(issues);
        var errors = issues.Count(issue => issue.Severity == ModPackEditorIssueSeverity.Error);
        SetStatus(errors == 0
            ? "校验通过，可以保存。"
            : "校验发现 " + errors + " 个错误，修好后才能保存。");
        if (errors == 0)
        {
            SetError(string.Empty);
        }
    }

    /// <summary>清空表单，准备新建一个 modpack。</summary>
    [RelayCommand]
    private void NewDocument()
    {
        ApplyDocument(new ModPackEditorDocument
        {
            Kind = "metapackage",
            ReleaseStatus = "stable",
            Version = "1.0.0",
        });
        ApplyIssues(Array.Empty<ModPackEditorIssue>());
        SetError(string.Empty);
        SetStatus("已清空表单，填写标识与名称后保存。");
        IsDirty = false;
    }

    /// <summary>追加一条关系行。</summary>
    [RelayCommand]
    private void AddRelation()
    {
        Relations.Add(ModPackRelationRowViewModel.CreateEmpty());
        SetStatus("已添加一条依赖关系，填写模组标识。");
    }

    /// <summary>删除一条关系行。</summary>
    /// <param name="row">要删除的行。</param>
    [RelayCommand]
    private void RemoveRelation(ModPackRelationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Relations.Remove(row);
    }

    /// <summary>把"待添加的作者"加入作者列表。</summary>
    [RelayCommand]
    private void AddAuthor()
    {
        var value = (NewAuthor ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return;
        }

        Authors.Add(new ModPackEditorRowViewModel(value, "作者"));
        NewAuthor = string.Empty;
    }

    /// <summary>删除一个作者。</summary>
    /// <param name="row">要删除的行。</param>
    [RelayCommand]
    private void RemoveAuthor(ModPackEditorRowViewModel? row)
    {
        if (row is not null)
        {
            Authors.Remove(row);
        }
    }

    /// <summary>把"待添加的许可证"加入许可证列表。</summary>
    [RelayCommand]
    private void AddLicense()
    {
        var value = (NewLicense ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return;
        }

        Licenses.Add(new ModPackEditorRowViewModel(value, "许可证"));
        NewLicense = string.Empty;
    }

    /// <summary>删除一个许可证。</summary>
    /// <param name="row">要删除的行。</param>
    [RelayCommand]
    private void RemoveLicense(ModPackEditorRowViewModel? row)
    {
        if (row is not null)
        {
            Licenses.Remove(row);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(ErrorText))
        {
            OnPropertyChanged(nameof(HasError));
            return;
        }

        if (IsInfrastructureProperty(e.PropertyName))
        {
            return;
        }

        MarkDirty();
    }

    private static bool IsInfrastructureProperty(string? name) => name is
        nameof(IsDirty)
        or nameof(IsBusy)
        or nameof(BusyText)
        or nameof(StatusText)
        or nameof(ErrorText)
        or nameof(HasError)
        or nameof(HasIssues)
        or nameof(IssueSummary)
        or nameof(ShowEmptyRelations)
        or nameof(NewAuthor)
        or nameof(NewLicense);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (_suppressDirty)
        {
            return;
        }

        IsDirty = true;
    }

    private void SetStatus(string text) => StatusText = text;

    private void SetError(string text) => ErrorText = text;

    private async Task RunAsync(Func<Task> body, string context, string busyText)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = busyText;
        SetError(string.Empty);
        try
        {
            await body();
        }
        catch (Exception exception)
        {
            SetError(ModPacksErrorText.FromException(exception, context));
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>无参构造用的兜底服务：任何操作都明确失败，不假装成功。</summary>
    private sealed class UnavailableModPackService : IModPackService
    {
        public Task<OperationResult<ModPackEditorDocument>> LoadEditorAsync(string path, CancellationToken ct = default)
            => Task.FromResult(OperationResult<ModPackEditorDocument>.Fail("编辑器服务不可用。"));

        public Task<OperationResult<string>> SaveEditorAsync(ModPackEditorDocument document, CancellationToken ct = default)
            => Task.FromResult(OperationResult<string>.Fail("编辑器服务不可用。"));

        public IReadOnlyList<ModPackEditorIssue> ValidateEditor(ModPackEditorDocument document)
            => Array.Empty<ModPackEditorIssue>();
    }
}
