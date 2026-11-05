using System.Globalization;

namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// 加载期诊断。adapter 是"数据"，坏数据不能让整个功能崩，
/// 所以一律降级成诊断项返回给上层（fail-closed：有 Error 的 adapter 不启用）。
/// </summary>
public sealed record ModConfigDiagnostic(
    ModConfigDiagnosticLevel Level,
    string Code,
    string Message,
    string? Source = null)
{
    /// <summary>是否为错误级。</summary>
    public bool IsError => Level == ModConfigDiagnosticLevel.Error;

    /// <summary>构造错误级诊断。</summary>
    public static ModConfigDiagnostic Error(string code, string message, string? source = null)
        => new(ModConfigDiagnosticLevel.Error, code, message, source);

    /// <summary>构造警告级诊断。</summary>
    public static ModConfigDiagnostic Warning(string code, string message, string? source = null)
        => new(ModConfigDiagnosticLevel.Warning, code, message, source);

    /// <inheritdoc />
    public override string ToString()
        => Source is null ? $"[{Level}] {Code}: {Message}" : $"[{Level}] {Code} @ {Source}: {Message}";
}

/// <summary>mod 的识别规则（15 篇 §4.1 `match`）。</summary>
public sealed record ModConfigMatchDescription(
    IReadOnlyList<string> CkanIdentifiers,
    IReadOnlyList<string> Paths,
    bool CaseInsensitive,
    bool ResolveByInstalledFiles)
{
    /// <summary>空规则（永远不会命中）。</summary>
    public static ModConfigMatchDescription Empty { get; } = new([], [], true, true);
}

/// <summary>adapter 声明的单个配置文件（15 篇 §4.1 `files[]`）。</summary>
/// <param name="Path">相对游戏目录的路径，用 / 分隔。</param>
/// <param name="Layout">文件形态。</param>
/// <param name="Mode">读写模式。</param>
/// <param name="Index">在 files[] 里的下标；fields[].file 用整数时指向它。</param>
/// <param name="RootNode">node 形态的根节点名（可空：自动取第一个节点）。</param>
/// <param name="Id">给 fields[].file 用的字符串标识（可空）。</param>
/// <param name="OnMissing">文件缺失时的策略。</param>
/// <param name="Template">create-from-template 的模板路径（相对 adapter 目录）。</param>
/// <param name="LineEnding">写盘行尾策略。</param>
/// <param name="Separator">键值分隔符，默认 " = "。</param>
/// <param name="ReadonlyReason">只读原因（展示用，必须写清"为什么不能改"）。</param>
/// <param name="Label">文件显示名（可空，回退到文件名）。</param>
public sealed record ModConfigFileDescription(
    string Path,
    ModConfigFileLayout Layout,
    ModConfigWriteMode Mode,
    int Index,
    string? RootNode = null,
    string? Id = null,
    ModConfigMissingFilePolicy OnMissing = ModConfigMissingFilePolicy.Error,
    string? Template = null,
    ModConfigLineEndingPolicy LineEnding = ModConfigLineEndingPolicy.Preserve,
    string? Separator = null,
    string? ReadonlyReason = null,
    string? Label = null)
{
    /// <summary>fields[].file 允许整数或字符串，这里给字符串标识一个稳定回退值。</summary>
    public string EffectiveId
        => string.IsNullOrWhiteSpace(Id) ? Index.ToString(CultureInfo.InvariantCulture) : Id!;

    /// <summary>实际生效的分隔符。</summary>
    public string EffectiveSeparator => string.IsNullOrEmpty(Separator) ? " = " : Separator!;

    /// <summary>
    /// 该文件是否真的可写。custom 形态是 mod 私有语法，解析器不认识它，
    /// 所以即使 adapter 写成 editable 也必须降级为只读（防写坏）。
    /// </summary>
    public bool IsWritable => Mode == ModConfigWriteMode.Editable && Layout != ModConfigFileLayout.Custom;

    /// <summary>显示名。</summary>
    public string DisplayName
        => string.IsNullOrWhiteSpace(Label) ? System.IO.Path.GetFileName(Path) : Label!;
}

/// <summary>adapter 声明的单个字段（15 篇 §4.1 `fields[]`）。</summary>
public sealed record ModConfigFieldDescription(
    string Key,
    ModConfigFieldType Type,
    int FileIndex,
    string? FileId = null,
    string? DefaultValue = null,
    IReadOnlyList<string>? Values = null,
    double? Min = null,
    double? Max = null,
    string? Unit = null,
    ModConfigFieldRisk Risk = ModConfigFieldRisk.Low,
    bool RequiresRestart = false,
    string? Label = null,
    string? Pattern = null,
    string? NodePath = null)
{
    /// <summary>
    /// 字段显示名。adapter 没写 label 时直接用 key —— 宁可显示英文 key，
    /// 也不要凭空造一个中文名（会误导用户以为官方有此叫法）。
    /// </summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Key : Label!;

    /// <summary>字段所在节点名链。</summary>
    public string? NodePath { get; init; } = NodePath;

    /// <summary>
    /// 该字段在文件里的完整节点名链。flat 布局一律空串（键值对在根层）；
    /// node 布局下 <see cref="NodePath"/> 相对 rootNode 解析，绝对写法以 <c>/</c> 开头。
    /// </summary>
    public string ResolveNodePath(ModConfigFileDescription file) => ResolveNodePath(NodePath, file);

    /// <summary>
    /// 把（可能为空的）nodePath 解析成文件里的完整节点名链。静态版给 adapter 读取器判重用，
    /// 免得"同一个节点写成相对和绝对两种形式"绕过判重。
    /// </summary>
    public static string ResolveNodePath(string? nodePath, ModConfigFileDescription file)
    {
        var root = file.Layout == ModConfigFileLayout.Node ? file.RootNode ?? string.Empty : string.Empty;
        var declared = nodePath?.Trim();
        if (declared is null || declared.Length == 0)
        {
            return root;
        }

        if (declared[0] == '/')
        {
            return declared.Trim('/');
        }

        // 手写 adapter 时很容易把根名也写进 nodePath（"ROOT/CHILD"）。再拼一次 root 会变成
        // "ROOT/ROOT/CHILD"，查找永远落空、字段永远空白——这种静默失败比报错更难查，
        // 所以已经带了根名的名链直接按绝对写法接受。
        if (root.Length > 0
            && (string.Equals(declared, root, StringComparison.Ordinal)
                || declared.StartsWith(root + "/", StringComparison.Ordinal)))
        {
            return declared;
        }

        return root.Length == 0 ? declared : root + "/" + declared;
    }

    /// <summary>是否声明了枚举白名单。</summary>
    public bool HasValues => Values is { Count: > 0 };
}

/// <summary>一个 adapter 的完整声明（加载并校验后的结果）。</summary>
public sealed record ModConfigAdapterDescription(
    int SchemaVersion,
    string Id,
    string Name,
    ModConfigMatchDescription Match,
    IReadOnlyList<ModConfigFileDescription> Files,
    IReadOnlyList<ModConfigFieldDescription> Fields,
    string? Homepage = null,
    string? DocUrl = null,
    IReadOnlyList<string>? ModVersionRange = null,
    bool DescriptionFromComment = true,
    bool GroupFromCommentHeadings = true,
    ModConfigUnlistedPolicy Unlisted = ModConfigUnlistedPolicy.Preserve,
    string? SourcePath = null,
    bool IsUserProvided = false,
    IReadOnlyList<ModConfigDiagnostic>? Diagnostics = null)
{
    /// <summary>版本区间（空表示不限）。</summary>
    public IReadOnlyList<string> VersionConstraints => ModVersionRange ?? [];

    /// <summary>加载期诊断。</summary>
    public IReadOnlyList<ModConfigDiagnostic> LoadDiagnostics => Diagnostics ?? [];

    /// <summary>按 fields[].file 的整数/字符串两种写法查文件。</summary>
    public ModConfigFileDescription? FindFile(int index, string? fileId)
    {
        for (var i = 0; i < Files.Count; i++)
        {
            var file = Files[i];
            if (i == index)
            {
                return file;
            }

            // fields[].file 写字符串时按 files[].id 匹配；没写 id 的文件用下标字符串兜底。
            if (!string.IsNullOrEmpty(fileId)
                && (string.Equals(file.Id, fileId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(file.EffectiveId, fileId, StringComparison.OrdinalIgnoreCase)))
            {
                return file;
            }
        }

        return null;
    }
}
