namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>注册表里一个已安装模组的只读投影（不含 CKAN 类型）。</summary>
public sealed record InstalledModuleInfo(
    string Identifier,
    string Version,
    bool IsAutoInstalled,
    string? SourceRepository,
    DateTime? InstallTime)
{
    /// <summary>
    /// 高级搜索事实（12 篇 F-20/F-21）：描述、许可证、语言、五类关系、标签组、缓存态。
    /// 供"仓库里没有、但已安装"的模组行也能被 <c>desc:</c>/`label:` 等条件命中。
    /// </summary>
    public ModSearchFacts Facts { get; init; }
}
