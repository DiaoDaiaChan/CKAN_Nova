namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>注册表只读快照（04 篇 §6）：VM 永不持有活的 CkanModule 引用。</summary>
public sealed record RegistrySnapshot(
    string InstanceName,
    string? RegistryVersion,
    IReadOnlyDictionary<string, InstalledModuleInfo> Installed,
    IReadOnlySet<string> Dlls,
    DateTime TakenAt)
{
    public static RegistrySnapshot Empty(string instanceName = "") =>
        new(instanceName, null, new Dictionary<string, InstalledModuleInfo>(), new HashSet<string>(), DateTime.UtcNow);
}
