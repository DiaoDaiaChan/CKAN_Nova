using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Plugin.ModConfig.Tests;

/// <summary>
/// Mod 配置页的测试数据工厂：集中拼装 record，避免每个用例都写一长串位置参数。
/// </summary>
internal static class ModConfigTestData
{
    /// <summary>造一个字段状态。</summary>
    public static ModConfigFieldState Field(
        string key,
        ModConfigFieldType type = ModConfigFieldType.String,
        string? value = null,
        string? label = null,
        string? comment = null,
        string? group = null,
        string? defaultValue = null,
        string? unit = null,
        double? min = null,
        double? max = null,
        IReadOnlyList<string>? values = null,
        ModConfigFieldRisk risk = ModConfigFieldRisk.Low,
        bool requiresRestart = false)
        => new(
            Key: key,
            Label: label ?? key,
            Type: type,
            Value: value,
            Comment: comment,
            Group: group,
            DefaultValue: defaultValue,
            Unit: unit,
            Min: min,
            Max: max,
            Values: values,
            Risk: risk,
            RequiresRestart: requiresRestart);

    /// <summary>造一个可写文件快照（Editable + node 形态）。</summary>
    public static ModConfigFileSnapshot WritableFile(params ModConfigFieldState[] fields)
        => new(
            FilePath: "/game/GameData/Demo/settings.cfg",
            RelativePath: "GameData/Demo/settings.cfg",
            Layout: ModConfigFileLayout.Node,
            Mode: ModConfigWriteMode.Editable,
            State: ModConfigFileState.Editable,
            Fields: fields);

    /// <summary>造一个只读文件快照。</summary>
    public static ModConfigFileSnapshot ReadonlyFile(string reason, params ModConfigFieldState[] fields)
        => new(
            FilePath: "/game/GameData/Demo/padheap.cfg",
            RelativePath: "GameData/Demo/padheap.cfg",
            Layout: ModConfigFileLayout.Custom,
            Mode: ModConfigWriteMode.ReadOnly,
            State: ModConfigFileState.ReadOnly,
            Fields: fields,
            ReadonlyReason: reason);

    /// <summary>造一张 mod 卡片快照。</summary>
    public static ModConfigModSnapshot Mod(
        string adapterId = "demo-mod",
        string name = "Demo Mod",
        string? installedVersion = "1.2.3",
        ModConfigAdapterState state = ModConfigAdapterState.Editable,
        IReadOnlyList<ModConfigFileSnapshot>? files = null,
        int modifiedCount = 0,
        IReadOnlyList<ModConfigDiagnostic>? diagnostics = null,
        string? supportedVersionRangeText = null)
        => new(
            AdapterId: adapterId,
            Name: name,
            InstalledVersion: installedVersion,
            State: state,
            Files: files ?? [],
            ModifiedCount: modifiedCount,
            Diagnostics: diagnostics,
            SupportedVersionRangeText: supportedVersionRangeText);

    /// <summary>造一份实例模型。</summary>
    public static ModConfigInstanceModel Model(
        IReadOnlyList<ModConfigModSnapshot>? mods = null,
        bool isGameRunning = false,
        int adapterlessModCount = 0,
        string instanceName = "默认",
        string gameDir = "C:/game")
        => new(
            InstanceName: instanceName,
            GameDir: gameDir,
            GameVersion: "1.12.5",
            IsGameRunning: isGameRunning,
            Mods: mods ?? [],
            AdapterlessModCount: adapterlessModCount);

    /// <summary>造一个 adapter 声明。</summary>
    public static ModConfigAdapterDescription Adapter(
        string id = "demo-mod",
        string name = "Demo Mod",
        bool isUserProvided = false,
        IReadOnlyList<string>? versionRange = null)
        => new(
            SchemaVersion: 1,
            Id: id,
            Name: name,
            Match: ModConfigMatchDescription.Empty,
            Files:
            [
                new ModConfigFileDescription(
                    Path: "GameData/Demo/settings.cfg",
                    Layout: ModConfigFileLayout.Node,
                    Mode: ModConfigWriteMode.Editable,
                    Index: 0,
                    RootNode: "ROOT"),
            ],
            Fields: [new ModConfigFieldDescription("enabled", ModConfigFieldType.Bool, FileIndex: 0)],
            ModVersionRange: versionRange,
            SourcePath: "adapters/" + id + "/adapter.json",
            IsUserProvided: isUserProvided);
}
