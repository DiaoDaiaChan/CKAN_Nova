namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>游戏实例的只读投影（不含 CKAN 类型，便于 headless 测试）。</summary>
public sealed record InstanceInfo(
    string Name,
    string GameDir,
    string? GameVersion,
    bool IsDefault,
    bool IsLocked,
    int? ModuleCount)
{
    public string DisplayName => string.IsNullOrEmpty(GameVersion) ? Name : $"{Name} ({GameVersion})";
}
