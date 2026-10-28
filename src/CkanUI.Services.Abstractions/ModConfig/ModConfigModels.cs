namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>一个字段在磁盘上的当前状态（只读快照，供 Presentation 渲染）。</summary>
public sealed record ModConfigFieldState(
    string Key,
    string Label,
    ModConfigFieldType Type,
    string? Value,
    string? Comment = null,
    string? Group = null,
    string? DefaultValue = null,
    string? Unit = null,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<string>? Values = null,
    ModConfigFieldRisk Risk = ModConfigFieldRisk.Low,
    bool RequiresRestart = false)
{
    /// <summary>渲染时是否需要强调"可能影响存档/性能"。</summary>
    public bool IsRisky => Risk != ModConfigFieldRisk.Low;
}

/// <summary>一个配置文件的读取结果。</summary>
public sealed record ModConfigFileSnapshot(
    string FilePath,
    string RelativePath,
    ModConfigFileLayout Layout,
    ModConfigWriteMode Mode,
    ModConfigFileState State,
    IReadOnlyList<ModConfigFieldState> Fields,
    string? ReadonlyReason = null,
    bool IsTextLossy = false,
    int ParseErrorCount = 0,
    string? DisplayName = null)
{
    /// <summary>是否可写（按模式 + 形态 + 文件状态三者共同判定）。</summary>
    public bool IsWritable
        => State == ModConfigFileState.Editable
            && Mode == ModConfigWriteMode.Editable
            && Layout != ModConfigFileLayout.Custom
            && ParseErrorCount == 0
            && !IsTextLossy;
}

/// <summary>一个已安装 mod 在配置页上的卡片状态。</summary>
public sealed record ModConfigModSnapshot(
    string AdapterId,
    string Name,
    string? InstalledVersion,
    ModConfigAdapterState State,
    IReadOnlyList<ModConfigFileSnapshot> Files,
    int ModifiedCount = 0,
    string? Homepage = null,
    IReadOnlyList<ModConfigDiagnostic>? Diagnostics = null,
    string? SupportedVersionRangeText = null)
{
    /// <summary>该 mod 下所有字段总数。</summary>
    public int FieldCount
    {
        get
        {
            var total = 0;
            foreach (var file in Files)
            {
                total += file.Fields.Count;
            }

            return total;
        }
    }

    /// <summary>是否可编辑。</summary>
    public bool IsEditable => State is ModConfigAdapterState.Editable or ModConfigAdapterState.CreatableByTemplate;
}

/// <summary>一次加载的完整页面模型：实例信息 + 有 adapter 的 mod + 无 adapter 的数量。</summary>
public sealed record ModConfigInstanceModel(
    string InstanceName,
    string GameDir,
    string? GameVersion,
    bool IsGameRunning,
    IReadOnlyList<ModConfigModSnapshot> Mods,
    int AdapterlessModCount = 0,
    IReadOnlyList<ModConfigDiagnostic>? Diagnostics = null)
{
    /// <summary>加载期诊断。</summary>
    public IReadOnlyList<ModConfigDiagnostic> LoadDiagnostics => Diagnostics ?? [];

    /// <summary>按 adapter id 找卡片。</summary>
    public ModConfigModSnapshot? FindMod(string? adapterId)
    {
        if (string.IsNullOrEmpty(adapterId))
        {
            return null;
        }

        foreach (var mod in Mods)
        {
            if (string.Equals(mod.AdapterId, adapterId, StringComparison.Ordinal))
            {
                return mod;
            }
        }

        return null;
    }
}

/// <summary>调用方要求写入的一个字段变更。</summary>
/// <param name="FilePath">目标文件的绝对路径（必须是实例游戏目录内的路径）。</param>
/// <param name="Key">键名。</param>
/// <param name="NewValue">新值原文。</param>
public sealed record ModConfigFieldChange(string FilePath, string Key, string NewValue);

/// <summary>写入请求。</summary>
/// <param name="InstanceName">实例名（诊断用）。</param>
/// <param name="GameDir">实例游戏目录（用于校验路径必须落在目录内）。</param>
/// <param name="AdapterId">adapter id（诊断用）。</param>
/// <param name="Changes">变更列表。</param>
/// <param name="ConfirmedRiskHigh">用户已确认高风险字段。</param>
/// <param name="ConfirmedGameRunning">用户已确认在 KSP 运行中仍然写入。</param>
public sealed record AdapterWriteRequest(
    string InstanceName,
    string GameDir,
    string AdapterId,
    IReadOnlyList<ModConfigFieldChange> Changes,
    bool ConfirmedRiskHigh = false,
    bool ConfirmedGameRunning = false)
{
    /// <summary>是否需要用户二次确认（KSP 正在运行）。</summary>
    public bool NeedsGameRunningConfirmation => !ConfirmedGameRunning;
}

/// <summary>实际落盘的一条变更（写前 old → 写后 new，供差异条与报告使用）。</summary>
public sealed record ModConfigAppliedChange(string FilePath, string Key, string? OldValue, string NewValue);

/// <summary>写入结果。</summary>
/// <param name="Changes">实际生效的变更。</param>
/// <param name="BackupPaths">写前快照路径（回滚用）。</param>
/// <param name="RequiresRestartCount">其中需要重启 KSP 才生效的条数。</param>
public sealed record AdapterWriteResult(
    IReadOnlyList<ModConfigAppliedChange> Changes,
    IReadOnlyList<string> BackupPaths,
    int RequiresRestartCount);
