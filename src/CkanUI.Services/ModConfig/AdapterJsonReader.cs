using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>一次 adapter.json 解析的结果：成功时 <paramref name="Adapter"/> 有值，失败时为 null 且诊断里有 Error。</summary>
/// <param name="Adapter">解析出的适配器描述；失败为 null。</param>
/// <param name="Diagnostics">解析与校验过程中发现的问题（Warning 不阻止启用）。</param>
public sealed record AdapterLoadResult(
    ModConfigAdapterDescription? Adapter,
    IReadOnlyList<ModConfigDiagnostic> Diagnostics)
{
    /// <summary>该 adapter 是否可用（失败即整体停用，绝不半可用，见 15 篇 §3）。</summary>
    public bool IsSuccess => Adapter is not null;

    /// <summary>是否有 Error 级诊断。</summary>
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.IsError)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>失败结果的便捷构造。</summary>
    public static AdapterLoadResult Fail(string code, string message, string? source = null)
        => new(null, [ModConfigDiagnostic.Error(code, message, source)]);
}

/// <summary>
/// adapter.json 的读取与 schema 校验（CFG-02）。
/// <para>
/// 设计取舍：用 System.Text.Json 手工校验而不是引入 JSON Schema 库——项目禁止新增 NuGet 依赖，
/// 而且 adapter 结构很小，手写校验能给出比通用 schema 库更具体的中文错误（哪一行哪个字段、期望什么）。
/// <c>docs/design/adapter.schema.json</c> 是给 CI 门禁（tools/verify-layout H-08）和编辑器提示用的同一份约束，
/// 两者必须同步：改这里就要改 schema，反之亦然（测试 AdapterSchemaConsistencyTests 会比对关键约束）。
/// </para>
/// </summary>
public static class AdapterJsonReader
{
    /// <summary>当前支持的 adapter.json schema 版本。</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>adapter id 的合法形态：kebab-case（与 H-08 的 schema pattern 一致）。</summary>
    private static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        // adapter.json 允许 jsonc 注释与尾逗号（§4.1 明确允许，方便写文档式注释）。
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32,
    };

    /// <summary>从文本读取（供测试与导入用）。</summary>
    /// <param name="json">adapter.json 原文。</param>
    /// <param name="sourcePath">来源路径，仅用于诊断信息与相对路径解析。</param>
    /// <param name="isUserProvided">是否来自用户目录（用户 adapter 的信任级别更高，见 §4.4）。</param>
    public static AdapterLoadResult Read(string json, string? sourcePath = null, bool isUserProvided = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AdapterLoadResult.Fail("adapter-empty", "adapter.json 是空的", sourcePath);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return AdapterLoadResult.Fail("adapter-json-invalid", $"adapter.json 不是合法 JSON：{ex.Message}", sourcePath);
        }

        using (document)
        {
            return Read(document.RootElement, sourcePath, isUserProvided);
        }
    }

    /// <summary>从磁盘读取一个 adapter 目录里的 adapter.json。</summary>
    public static AdapterLoadResult ReadFile(string adapterJsonPath, bool isUserProvided = false)
    {
        string text;
        try
        {
            text = File.ReadAllText(adapterJsonPath);
        }
        catch (IOException ex)
        {
            return AdapterLoadResult.Fail("adapter-io", $"读取 adapter.json 失败：{ex.Message}", adapterJsonPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            return AdapterLoadResult.Fail("adapter-io", $"读取 adapter.json 被拒绝：{ex.Message}", adapterJsonPath);
        }

        return Read(text, adapterJsonPath, isUserProvided);
    }

    private static AdapterLoadResult Read(JsonElement root, string? sourcePath, bool isUserProvided)
    {
        var diagnostics = new List<ModConfigDiagnostic>();
        var errors = new List<ModConfigDiagnostic>();

        void Error(string code, string message)
        {
            var diagnostic = ModConfigDiagnostic.Error(code, message, sourcePath);
            diagnostics.Add(diagnostic);
            errors.Add(diagnostic);
        }

        void Warn(string code, string message)
            => diagnostics.Add(ModConfigDiagnostic.Warning(code, message, sourcePath));

        if (root.ValueKind != JsonValueKind.Object)
        {
            Error("adapter-schema", "adapter.json 的根必须是对象");
            return new AdapterLoadResult(null, diagnostics);
        }

        // ---- schemaVersion ----
        var schemaVersion = 0;
        if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement)
            || schemaVersionElement.ValueKind != JsonValueKind.Number
            || !schemaVersionElement.TryGetInt32(out schemaVersion))
        {
            Error("adapter-schema", "缺少 schemaVersion，或它不是整数");
        }
        else if (schemaVersion != SupportedSchemaVersion)
        {
            Error("adapter-schema", $"schemaVersion 为 {schemaVersion}，本版本只支持 {SupportedSchemaVersion}");
        }

        // ---- id / name ----
        var id = ReadRequiredString(root, "id", Error, "adapter-schema");
        if (id is not null && !IdPattern.IsMatch(id))
        {
            Error("adapter-id", $"id「{id}」不合规：只能是小写字母、数字和短的连字符（kebab-case）");
        }

        var name = ReadRequiredString(root, "name", Error, "adapter-schema");

        // ---- 可选元数据 ----
        var homepage = ReadOptionalString(root, "homepage", Warn, "adapter-schema");
        var docUrl = ReadOptionalString(root, "docUrl", Warn, "adapter-schema");
        if (homepage is not null && !IsHttpUrl(homepage))
        {
            Error("adapter-url", $"homepage「{homepage}」不是 http(s) 地址");
        }

        if (docUrl is not null && !IsHttpUrl(docUrl))
        {
            Error("adapter-url", $"docUrl「{docUrl}」不是 http(s) 地址");
        }

        // ---- match ----
        var match = ReadMatch(root, Error, Warn);

        // ---- modVersionRange ----
        var versionRange = ReadVersionRange(root, Error);

        // ---- files ----
        var files = ReadFiles(root, Error, Warn);

        // ---- fields（依赖 files 才能解析 file 引用）----
        var fields = ReadFields(root, files, Error, Warn);

        // ---- 其它开关 ----
        var descriptionFromComment = ReadOptionalBool(root, "descriptionFromComment", true, Warn);
        var groupFromCommentHeadings = ReadGroupFromCommentHeadings(root, true, Warn);
        var unlisted = ModConfigUnlistedPolicy.Preserve;
        if (root.TryGetProperty("unlisted", out var unlistedElement))
        {
            if (unlistedElement.ValueKind != JsonValueKind.String
                || !ModConfigWireNames.TryParseUnlistedPolicy(unlistedElement.GetString(), out unlisted))
            {
                Error("adapter-schema", "unlisted 只能是 preserve、hide 或 mark-unknown");
                unlisted = ModConfigUnlistedPolicy.Preserve;
            }
        }

        WarnUnknownProperties(root, ["schemaVersion", "id", "name", "homepage", "docUrl", "match", "modVersionRange", "files", "fields", "descriptionFromComment", "groups", "unlisted"], Warn);

        if (errors.Count > 0 || id is null || name is null || files.Count == 0)
        {
            // 有任何 Error 就整体停用：宁可少一个 adapter，也不要半可用的错配置去写盘。
            return new AdapterLoadResult(null, diagnostics);
        }

        var adapter = new ModConfigAdapterDescription(
            SchemaVersion: schemaVersion,
            Id: id,
            Name: name,
            Match: match,
            Files: files,
            Fields: fields,
            Homepage: homepage,
            DocUrl: docUrl,
            ModVersionRange: versionRange,
            DescriptionFromComment: descriptionFromComment,
            GroupFromCommentHeadings: groupFromCommentHeadings,
            Unlisted: unlisted,
            SourcePath: sourcePath,
            IsUserProvided: isUserProvided,
            Diagnostics: diagnostics);

        return new AdapterLoadResult(adapter, diagnostics);
    }

    private static ModConfigMatchDescription ReadMatch(JsonElement root, Action<string, string> error, Action<string, string> warn)
    {
        if (!root.TryGetProperty("match", out var matchElement) || matchElement.ValueKind != JsonValueKind.Object)
        {
            error("adapter-match", "缺少 match 对象");
            return ModConfigMatchDescription.Empty;
        }

        var identifiers = ReadStringArray(matchElement, "ckanIdentifiers", error, "adapter-match");
        if (identifiers.Count == 0)
        {
            error("adapter-match", "match.ckanIdentifiers 至少要有 1 个标识符");
        }

        var paths = ReadStringArray(matchElement, "paths", error, "adapter-match");
        var caseInsensitive = ReadOptionalBool(matchElement, "caseInsensitive", true, warn);
        var resolveByInstalledFiles = ReadOptionalBool(matchElement, "resolveByInstalledFiles", false, warn);

        WarnUnknownProperties(matchElement, ["ckanIdentifiers", "paths", "caseInsensitive", "resolveByInstalledFiles"], warn);

        return new ModConfigMatchDescription(identifiers, paths, caseInsensitive, resolveByInstalledFiles);
    }

    private static IReadOnlyList<string> ReadVersionRange(JsonElement root, Action<string, string> error)
    {
        if (!root.TryGetProperty("modVersionRange", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error("adapter-version-range", "modVersionRange 必须是数组，例如 [\">=1.30\", \"<2.0\"]");
            return [];
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error("adapter-version-range", "modVersionRange 的每一项都必须是字符串");
                continue;
            }

            var raw = item.GetString() ?? string.Empty;
            if (!ModConfigVersionRange.TryParseConstraint(raw, out _, out var constraintError))
            {
                // 带上属性名：adapter.json 里同一条消息可能来自多处，用户需要知道是哪个属性写错了。
                error("adapter-version-range", constraintError is null
                    ? $"modVersionRange：版本约束「{raw}」非法"
                    : $"modVersionRange：{constraintError}");
                continue;
            }

            list.Add(raw);
        }

        return list;
    }

    private static IReadOnlyList<ModConfigFileDescription> ReadFiles(
        JsonElement root,
        Action<string, string> error,
        Action<string, string> warn)
    {
        if (!root.TryGetProperty("files", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            error("adapter-schema", "缺少 files 数组");
            return [];
        }

        var files = new List<ModConfigFileDescription>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var item in element.EnumerateArray())
        {
            var position = index;
            index++;

            if (item.ValueKind != JsonValueKind.Object)
            {
                error("adapter-file", $"files[{position}] 必须是对象");
                continue;
            }

            var path = ReadRequiredString(item, "path", (code, message) => error(code, $"files[{position}].{message}"), "adapter-file");
            if (path is null)
            {
                continue;
            }

            if (Path.IsPathRooted(path))
            {
                error("adapter-file", $"files[{position}].path「{path}」必须是相对游戏目录的路径，不能是绝对路径");
                continue;
            }

            var layout = ModConfigFileLayout.Node;
            if (!item.TryGetProperty("layout", out var layoutElement)
                || layoutElement.ValueKind != JsonValueKind.String
                || !ModConfigWireNames.TryParseLayout(layoutElement.GetString(), out layout))
            {
                error("adapter-file", $"files[{position}].layout 只能是 node、flat 或 custom");
                continue;
            }

            var mode = ModConfigWriteMode.Editable;
            if (!item.TryGetProperty("mode", out var modeElement)
                || modeElement.ValueKind != JsonValueKind.String
                || !ModConfigWireNames.TryParseWriteMode(modeElement.GetString(), out mode))
            {
                error("adapter-file", $"files[{position}].mode 只能是 editable 或 readonly");
                continue;
            }

            var rootNode = ReadOptionalString(item, "rootNode", warn, "adapter-file");
            var fileId = ReadOptionalString(item, "id", warn, "adapter-file");
            if (fileId is not null && !seenIds.Add(fileId))
            {
                error("adapter-file", $"files[{position}].id「{fileId}」重复");
            }

            var onMissing = ModConfigMissingFilePolicy.Error;
            if (item.TryGetProperty("onMissing", out var onMissingElement) && onMissingElement.ValueKind != JsonValueKind.Null)
            {
                if (onMissingElement.ValueKind != JsonValueKind.String
                    || !ModConfigWireNames.TryParseMissingPolicy(onMissingElement.GetString(), out onMissing))
                {
                    error("adapter-file", $"files[{position}].onMissing 只能是 create-from-template、error 或 readonly");
                    onMissing = ModConfigMissingFilePolicy.Error;
                }
            }

            var template = ReadOptionalString(item, "template", warn, "adapter-file");
            if (onMissing == ModConfigMissingFilePolicy.CreateFromTemplate && template is null)
            {
                error("adapter-file", $"files[{position}] 声明了 onMissing=create-from-template，但没有给 template");
            }

            var lineEnding = ModConfigLineEndingPolicy.Preserve;
            if (item.TryGetProperty("lineEnding", out var lineEndingElement) && lineEndingElement.ValueKind != JsonValueKind.Null)
            {
                if (lineEndingElement.ValueKind != JsonValueKind.String
                    || !ModConfigWireNames.TryParseLineEnding(lineEndingElement.GetString(), out lineEnding))
                {
                    error("adapter-file", $"files[{position}].lineEnding 只能是 preserve、crlf 或 lf");
                    lineEnding = ModConfigLineEndingPolicy.Preserve;
                }
            }

            var separator = ReadOptionalString(item, "separator", warn, "adapter-file");
            if (separator is not null && separator.Length == 0)
            {
                error("adapter-file", $"files[{position}].separator 不能是空字符串");
            }

            var readonlyReason = ReadOptionalString(item, "readonlyReason", warn, "adapter-file");
            var label = ReadOptionalString(item, "label", warn, "adapter-file");

            if (layout == ModConfigFileLayout.Custom && mode == ModConfigWriteMode.Editable)
            {
                // custom 布局意味着"我们不认识的语法"，没有解析器就不可能安全写回。
                error("adapter-file", $"files[{position}] 是 custom 布局，必须声明 mode=readonly（否则写盘会破坏文件）");
                continue;
            }

            if (layout == ModConfigFileLayout.Node && rootNode is null)
            {
                warn("adapter-file", $"files[{position}] 是 node 布局但没写 rootNode，将按整个文件搜索键名");
            }

            WarnUnknownProperties(item, ["path", "layout", "rootNode", "id", "mode", "onMissing", "template", "lineEnding", "separator", "readonlyReason", "label"], warn);

            files.Add(new ModConfigFileDescription(
                Path: path,
                Layout: layout,
                Mode: mode,
                Index: position,
                RootNode: rootNode,
                Id: fileId,
                OnMissing: onMissing,
                Template: template,
                LineEnding: lineEnding,
                Separator: separator,
                ReadonlyReason: readonlyReason,
                Label: label));
        }

        if (files.Count == 0)
        {
            error("adapter-schema", "files 至少要声明 1 个文件");
        }

        return files;
    }

    private static IReadOnlyList<ModConfigFieldDescription> ReadFields(
        JsonElement root,
        IReadOnlyList<ModConfigFileDescription> files,
        Action<string, string> error,
        Action<string, string> warn)
    {
        if (!root.TryGetProperty("fields", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error("adapter-schema", "fields 必须是数组");
            return [];
        }

        var fields = new List<ModConfigFieldDescription>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var item in element.EnumerateArray())
        {
            var position = index;
            index++;

            if (item.ValueKind != JsonValueKind.Object)
            {
                error("adapter-field", $"fields[{position}] 必须是对象");
                continue;
            }

            var key = ReadRequiredString(item, "key", (code, message) => error(code, $"fields[{position}].{message}"), "adapter-field");
            if (key is null)
            {
                continue;
            }

            var type = ModConfigFieldType.String;
            if (!item.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !ModConfigWireNames.TryParseFieldType(typeElement.GetString(), out type))
            {
                // 诊断里带上实际写的内容：光列合法值，用户还得自己回头找是哪个拼错了。
                var actualType = item.TryGetProperty("type", out var badType) && badType.ValueKind == JsonValueKind.String
                    ? badType.GetString()
                    : badType.ValueKind == JsonValueKind.Undefined ? "缺失" : badType.GetRawText();
                error("adapter-field", $"fields[{position}].type 不是已知的字段类型（bool/int/float/enum/string/path/color/keybinding/list/readonly）：实际是「{actualType}」");
                continue;
            }

            // file 既可以是整数下标，也可以是字符串 id。
            var fileIndex = -1;
            string? fileId = null;
            if (!item.TryGetProperty("file", out var fileElement))
            {
                error("adapter-field", $"fields[{position}] 缺少 file（文件下标或文件 id）");
                continue;
            }

            if (fileElement.ValueKind == JsonValueKind.Number)
            {
                if (!fileElement.TryGetInt32(out var declaredIndex))
                {
                    error("adapter-field", $"fields[{position}].file 不是整数");
                    continue;
                }

                if (declaredIndex < 0 || declaredIndex >= files.Count)
                {
                    error("adapter-field", $"fields[{position}].file = {declaredIndex} 超出 files 下标范围（0..{files.Count - 1}）");
                    continue;
                }

                fileIndex = declaredIndex;
            }
            else if (fileElement.ValueKind == JsonValueKind.String)
            {
                fileId = fileElement.GetString();
                var matched = -1;
                for (var i = 0; i < files.Count; i++)
                {
                    if (string.Equals(files[i].EffectiveId, fileId, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = i;
                        break;
                    }
                }

                if (matched < 0)
                {
                    error("adapter-field", $"fields[{position}].file = 「{fileId}」找不到对应文件 id");
                    continue;
                }

                fileIndex = matched;
            }
            else
            {
                error("adapter-field", $"fields[{position}].file 必须是整数下标或字符串 id");
                continue;
            }

            var scopedKey = fileIndex.ToString(CultureInfo.InvariantCulture) + "/" + key;
            if (!seenKeys.Add(scopedKey))
            {
                error("adapter-field", $"fields[{position}] 的 key「{key}」在同一个文件里重复声明");
                continue;
            }

            var values = ReadStringArray(item, "values", error, "adapter-field");
            double? min = ReadOptionalNumber(item, "min", warn);
            double? max = ReadOptionalNumber(item, "max", warn);
            if (min is not null && max is not null && min > max)
            {
                error("adapter-field", $"fields[{position}] 的 min({min}) 大于 max({max})");
            }

            var risk = ModConfigFieldRisk.Low;
            if (item.TryGetProperty("risk", out var riskElement) && riskElement.ValueKind != JsonValueKind.Null)
            {
                if (riskElement.ValueKind != JsonValueKind.String
                    || !ModConfigWireNames.TryParseRisk(riskElement.GetString(), out risk))
                {
                    error("adapter-field", $"fields[{position}].risk 只能是 low、medium 或 high");
                    risk = ModConfigFieldRisk.Low;
                }
            }

            var pattern = ReadOptionalString(item, "pattern", warn, "adapter-field");
            if (pattern is not null)
            {
                try
                {
                    _ = new Regex(pattern, RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    error("adapter-field", $"fields[{position}].pattern 不是合法正则：{ex.Message}");
                }
            }

            if (type == ModConfigFieldType.Enum && values.Count == 0)
            {
                error("adapter-field", $"fields[{position}] 是 enum 类型但没有给 values 白名单");
            }

            WarnUnknownProperties(item, ["file", "key", "type", "default", "values", "min", "max", "unit", "risk", "requiresRestart", "label", "pattern"], warn);

            fields.Add(new ModConfigFieldDescription(
                Key: key,
                Type: type,
                FileIndex: fileIndex,
                FileId: fileId,
                DefaultValue: ReadDefaultValue(item),
                Values: values,
                Min: min,
                Max: max,
                Unit: ReadOptionalString(item, "unit", warn, "adapter-field"),
                Risk: risk,
                RequiresRestart: ReadOptionalBool(item, "requiresRestart", false, warn),
                Label: ReadOptionalString(item, "label", warn, "adapter-field"),
                Pattern: pattern));
        }

        return fields;
    }

    /// <summary>default 允许是字符串、数字或布尔，统一降级成原文（写回时按字段类型再格式化）。</summary>
    private static string? ReadDefaultValue(JsonElement item)
    {
        if (!item.TryGetProperty("default", out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static bool ReadGroupFromCommentHeadings(JsonElement root, bool fallback, Action<string, string> warn)
    {
        if (!root.TryGetProperty("groups", out var groupsElement) || groupsElement.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        if (groupsElement.ValueKind != JsonValueKind.Object)
        {
            warn("adapter-schema", "groups 必须是对象");
            return fallback;
        }

        WarnUnknownProperties(groupsElement, ["fromCommentHeadings"], warn);
        return ReadOptionalBool(groupsElement, "fromCommentHeadings", fallback, warn);
    }

    private static string? ReadRequiredString(
        JsonElement element,
        string property,
        Action<string, string> error,
        string code)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            error(code, $"缺少字符串属性 {property}");
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            error(code, $"属性 {property} 不能是空字符串");
            return null;
        }

        return text;
    }

    private static string? ReadOptionalString(
        JsonElement element,
        string property,
        Action<string, string> warn,
        string code)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            warn(code, $"属性 {property} 应该是字符串，已忽略");
            return null;
        }

        return value.GetString();
    }

    private static bool ReadOptionalBool(JsonElement element, string property, bool fallback, Action<string, string> warn)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => WarnAndFallback(property, fallback, warn),
        };
    }

    private static bool WarnAndFallback(string property, bool fallback, Action<string, string> warn)
    {
        warn("adapter-schema", $"属性 {property} 应该是布尔值，已按默认值处理");
        return fallback;
    }

    private static double? ReadOptionalNumber(JsonElement element, string property, Action<string, string> warn)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
        {
            warn("adapter-schema", $"属性 {property} 应该是数字，已忽略");
            return null;
        }

        return number;
    }

    private static IReadOnlyList<string> ReadStringArray(
        JsonElement element,
        string property,
        Action<string, string> error,
        string code)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            error(code, $"属性 {property} 必须是字符串数组");
            return [];
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error(code, $"属性 {property} 里的每一项都必须是字符串");
                continue;
            }

            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                list.Add(text);
            }
        }

        return list;
    }

    /// <summary>未知属性只记警告：用户 adapter 的拼写错误（比如 writemode 写成小写）需要提示，但不该整份停用。</summary>
    private static void WarnUnknownProperties(JsonElement element, IReadOnlyList<string> known, Action<string, string> warn)
    {
        foreach (var property in element.EnumerateObject())
        {
            var found = false;
            foreach (var name in known)
            {
                if (string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                warn("adapter-unknown-property", $"未知属性「{property.Name}」已忽略（可能是拼写错误）");
            }
        }
    }

    private static bool IsHttpUrl(string text)
        => text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
