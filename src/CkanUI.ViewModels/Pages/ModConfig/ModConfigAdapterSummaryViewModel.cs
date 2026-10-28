using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>适配器清单行（"当前加载了哪些 adapter、它是内置还是用户自己放的"）。</summary>
public sealed class ModConfigAdapterSummaryViewModel
{
    /// <summary>构造一行。</summary>
    /// <param name="adapter">adapter 描述。</param>
    public ModConfigAdapterSummaryViewModel(ModConfigAdapterDescription adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        Id = adapter.Id;
        Name = adapter.Name;
        SourceText = adapter.IsUserProvided ? "用户目录" : "内置";
        RangeText = adapter.ModVersionRange is { Count: > 0 } range ? string.Join(", ", range) : "不限";
        FileCountText = adapter.Files.Count + " 个配置文件";
        FieldCountText = adapter.Fields.Count + " 个字段";
        Homepage = adapter.Homepage ?? string.Empty;
    }

    /// <summary>adapter id。</summary>
    public string Id { get; }

    /// <summary>adapter 名。</summary>
    public string Name { get; }

    /// <summary>来源（内置/用户目录）。</summary>
    public string SourceText { get; }

    /// <summary>支持版本区间。</summary>
    public string RangeText { get; }

    /// <summary>声明文件数文案。</summary>
    public string FileCountText { get; }

    /// <summary>声明字段数文案。</summary>
    public string FieldCountText { get; }

    /// <summary>首页。</summary>
    public string Homepage { get; }
}
