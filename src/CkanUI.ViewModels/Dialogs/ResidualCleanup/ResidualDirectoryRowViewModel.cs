using CommunityToolkit.Mvvm.ComponentModel;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs.ResidualCleanup;

/// <summary>
/// 残留目录列表的一行（06 篇 UI-22）：路径 + 内容预览 + 勾选状态 + 不可删除的原因。
/// </summary>
public sealed partial class ResidualDirectoryRowViewModel : ObservableObject
{
    /// <summary>创建一个行视图模型；可删除的目录默认就是勾选状态（少点一次）。</summary>
    /// <param name="info">服务层给出的目录信息。</param>
    public ResidualDirectoryRowViewModel(ResidualDirectoryInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        _isSelected = info.IsDeletable;
    }

    /// <summary>服务层原始数据。</summary>
    public ResidualDirectoryInfo Info { get; }

    /// <summary>相对 GameData 的目录路径。</summary>
    public string RelativePath => Info.RelativePath;

    /// <summary>目录（含子目录）里的文件数。</summary>
    public int FileCount => Info.FileCount;

    /// <summary>目录（含子目录）的字节数。</summary>
    public long SizeBytes => Info.SizeBytes;

    /// <summary>绝对路径，供「打开目录」使用（由对话框按实例拼出来）。</summary>
    public string AbsolutePath { get; internal set; } = string.Empty;

    /// <summary>是否可以删除。</summary>
    public bool IsDeletable => Info.IsDeletable;

    /// <summary>不可删除的原因；可删除时为 <c>null</c>。</summary>
    public string? BlockReason => Info.BlockReason;

    /// <summary>是否要显示不可删除的提示。</summary>
    public bool HasBlockReason => !IsDeletable;

    /// <summary>格式化后的体积，例如"1.2 MB"。</summary>
    public string SizeText => FormatSize(SizeBytes);

    /// <summary>内容预览第一行：文件数 + 体积。</summary>
    public string ContentText => FileCount == 0 ? "空目录" : $"{FileCount} 个文件 · {SizeText}";

    /// <summary>是否有示例文件名可展示。</summary>
    public bool HasSamples => Info.SampleFiles.Count > 0;

    /// <summary>内容预览第二行：示例文件名。</summary>
    public string SampleText => HasSamples ? "如：" + string.Join("、", Info.SampleFiles) : string.Empty;

    /// <summary>是否勾选（只有可删除的行才允许勾选）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>把字节数格式化成人类可读的字符串（0 → "0 B"）。</summary>
    /// <param name="bytes">字节数。</param>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        var mb = kb / 1024d;
        return mb < 1024 ? $"{mb:0.#} MB" : $"{mb / 1024d:0.##} GB";
    }
}
