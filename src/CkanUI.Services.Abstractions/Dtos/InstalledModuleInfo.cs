namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>注册表里一个已安装模组的只读投影（不含 CKAN 类型）。</summary>
public sealed record InstalledModuleInfo(
    string Identifier,
    string Version,
    bool IsAutoInstalled,
    string? SourceRepository,
    DateTime? InstallTime);
