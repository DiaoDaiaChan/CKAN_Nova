using System;
using System.Collections.Generic;
using System.Linq;

using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>
/// 导出格式选项（用于 ComboBox 绑定）。
/// </summary>
public sealed class ExportFormatOption
{
    public ExportListFormat Format { get; }
    public string DisplayName { get; }
    public string Extension { get; }
    public string DefaultExtension => Extension;

    public ExportFormatOption(ExportListFormat format, string displayName, string extension)
    {
        Format = format;
        DisplayName = displayName;
        Extension = extension;
    }

    public override string ToString() => DisplayName;
}

/// <summary>导出表单：目标路径、导出格式、是否锁定版本、是否打包下载包、是否用 recommends 语义（F-39 / F-45 / UI-24）。</summary>
public sealed partial class ExportFormViewModel : ObservableObject
{
    public ExportFormViewModel()
    {
        FormatOptions = new List<ExportFormatOption>
        {
            new(ExportListFormat.Ckan, ".ckan 元数据包（可回导完整配置）", ".ckan"),
            new(ExportListFormat.Markdown, "Markdown 表格（.md，适合发布文档）", ".md"),
            new(ExportListFormat.PlainText, "纯文本列表（.txt，每行一个模组）", ".txt"),
            new(ExportListFormat.BbCode, "BBCode 列表（.txt，适合官方论坛贴）", ".txt"),
            new(ExportListFormat.Csv, "CSV 逗号分隔表（.csv，适合 Excel）", ".csv"),
            new(ExportListFormat.Tsv, "TSV 制表符分隔表（.tsv，适合数据处理）", ".tsv"),
        };
        _selectedFormatOption = FormatOptions[0];
    }

    /// <summary>所有支持的导出格式选项列表。</summary>
    public IReadOnlyList<ExportFormatOption> FormatOptions { get; }

    [ObservableProperty]
    private ExportFormatOption _selectedFormatOption;

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

    /// <summary>当前选中的导出格式枚举。</summary>
    public ExportListFormat Format => SelectedFormat;

    public ExportListFormat SelectedFormat
    {
        get => SelectedFormatOption.Format;
        set
        {
            var match = FormatOptions.FirstOrDefault(opt => opt.Format == value);
            if (match is not null)
            {
                SelectedFormatOption = match;
            }
        }
    }

    /// <summary>当前是否为 .ckan 格式导出。</summary>
    public bool IsCkanFormat => SelectedFormat == ExportListFormat.Ckan;

    /// <summary>是否允许配置 .ckan 专有选项（WithVersions / BundleDownloads / UseRecommends）。非 ckan 格式时禁用。</summary>
    public bool CanConfigureModpackOptions => IsCkanFormat;

    /// <summary>目标输出文件路径占位提示。</summary>
    public string PathHint => SelectedFormat switch
    {
        ExportListFormat.Markdown => @"例如：D:CKAN已安装模组.md",
        ExportListFormat.PlainText => @"例如：D:CKAN已安装模组.txt",
        ExportListFormat.BbCode => @"例如：D:CKAN论坛模组贴.txt",
        ExportListFormat.Csv => @"例如：D:CKAN模组清单.csv",
        ExportListFormat.Tsv => @"例如：D:CKAN模组清单.tsv",
        _ => @"例如：D:CKAN我的模组列表.ckan（省略 .ckan 后缀会自动补上）"
    };

    /// <summary>目标路径是否为空（空则不能导出）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(TargetPath);

    /// <summary>是否有表单校验错误可显示。</summary>
    public bool HasValidation => !string.IsNullOrWhiteSpace(ValidationText);

    /// <summary>是否把下载包一起打包成 zip。</summary>
    public bool HasBundleDownloads => BundleDownloads;

    /// <summary>按当前表单生成导出请求。</summary>
    /// <returns>导出请求。</returns>
    public ModPackExportRequest ToRequest() => new(
        TargetPath: TargetPath.Trim(),
        WithVersions: IsCkanFormat && WithVersions,
        BundleDownloads: IsCkanFormat && BundleDownloads,
        UseRecommends: IsCkanFormat && UseRecommends,
        Format: SelectedFormat);

    /// <summary>清空表单并恢复默认选项。</summary>
    public void Reset()
    {
        SelectedFormatOption = FormatOptions[0];
        TargetPath = string.Empty;
        WithVersions = true;
        BundleDownloads = false;
        UseRecommends = false;
        ValidationText = null;
    }

    partial void OnSelectedFormatOptionChanged(ExportFormatOption value)
    {
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(IsCkanFormat));
        OnPropertyChanged(nameof(CanConfigureModpackOptions));
        OnPropertyChanged(nameof(PathHint));
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
