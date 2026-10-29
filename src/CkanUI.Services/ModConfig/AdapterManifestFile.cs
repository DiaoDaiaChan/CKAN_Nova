using System.Globalization;
using System.Text;
using System.Text.Json;
using CkanUI.Services.Abstractions.ModConfig;
using log4net;

namespace CkanUI.Services.ModConfig;

/// <summary>清单读取结果：<see cref="Manifest"/> 为 null 时 <see cref="Diagnostics"/> 里有一条 warning（清单损坏降级）。</summary>
internal sealed record AdapterManifestRead(AdapterInstallManifest? Manifest, IReadOnlyList<ModConfigDiagnostic> Diagnostics);

/// <summary>
/// adapter 安装清单（installed.json）的读写（ADP-04）。手工用 Utf8JsonWriter 写，字段顺序稳定、便于人读与 diff。
/// </summary>
internal static class AdapterManifestFile
{
    /// <summary>清单诊断码：清单存在但读不懂（当作未安装）。</summary>
    public const string CorruptCode = "adapter-manifest-corrupt";

    /// <summary>清单诊断码：清单里的 id 与所在目录名不一致。</summary>
    public const string IdMismatchCode = "adapter-manifest-id-mismatch";

    private static readonly ILog Log = LogManager.GetLogger(typeof(AdapterManifestFile));

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>json 解析选项：与 adapter.json 一致地容忍注释与尾逗号（用户可能手工改清单）。</summary>
    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32,
    };

    /// <summary>把清单写成 installed.json 的文本。</summary>
    /// <param name="manifest">清单。</param>
    public static string Serialize(AdapterInstallManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("manifestVersion", manifest.ManifestVersion);
            writer.WriteString("id", manifest.Id);
            writer.WriteString("name", manifest.Name);
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);
            WriteOptional(writer, "declaredVersion", manifest.DeclaredVersion);
            writer.WriteString("sourceKind", AdapterPackageWireNames.ToWireName(manifest.SourceKind));
            writer.WriteString("source", manifest.Source);
            WriteOptional(writer, "entryPoint", manifest.EntryPoint);
            writer.WriteString("sha256", manifest.Sha256);
            writer.WriteNumber("sizeBytes", manifest.SizeBytes);
            writer.WriteString("installedAt", AdapterPackageWireNames.FormatTimestamp(manifest.InstalledAt));
            WriteOptional(writer, "sourceEtag", manifest.SourceEtag);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>读一个 adapter 目录里的清单。目录里没有清单时返回空结果（= 手工放进去的 adapter，不算损坏）。</summary>
    /// <param name="adapterDirectory">adapter 目录（&lt;根&gt;/&lt;id&gt;）。</param>
    public static AdapterManifestRead Read(string adapterDirectory)
    {
        var diagnostics = new List<ModConfigDiagnostic>(1);
        var manifestPath = Path.Combine(adapterDirectory, AdapterPackageLayout.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return new AdapterManifestRead(null, diagnostics);
        }

        string text;
        try
        {
            text = File.ReadAllText(manifestPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AdapterManifestRead(null, Corrupt(diagnostics, $"读不了安装清单：{ex.Message}", manifestPath));
        }

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(adapterDirectory));
        return new AdapterManifestRead(Parse(text, manifestPath, folderName, diagnostics), diagnostics);
    }

    private static List<ModConfigDiagnostic> Corrupt(List<ModConfigDiagnostic> diagnostics, string message, string path, string code = CorruptCode)
    {
        diagnostics.Add(ModConfigDiagnostic.Warning(code, $"adapter 安装清单不可用，按未安装处理：{message}", path));
        Log.Warn($"adapter 安装清单不可用，按未安装处理：{message}（{path}）");
        return diagnostics;
    }

    private static AdapterInstallManifest? Parse(string text, string manifestPath, string folderName, List<ModConfigDiagnostic> diagnostics)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, DocumentOptions);
        }
        catch (JsonException ex)
        {
            Corrupt(diagnostics, $"不是合法 json：{ex.Message}", manifestPath);
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Corrupt(diagnostics, "顶层不是对象。", manifestPath);
                return null;
            }

            var id = ReadString(root, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                Corrupt(diagnostics, "缺少 id。", manifestPath);
                return null;
            }

            if (!string.Equals(id.Trim(), folderName, StringComparison.OrdinalIgnoreCase))
            {
                Corrupt(diagnostics, $"清单里的 id「{id}」与目录名「{folderName}」不一致。", manifestPath, IdMismatchCode);
                return null;
            }

            var sha256 = ReadString(root, "sha256");
            if (!IsSha256Hex(sha256))
            {
                Corrupt(diagnostics, "sha256 缺失或不是 64 位十六进制。", manifestPath);
                return null;
            }

            var name = ReadString(root, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                Corrupt(diagnostics, "缺少 name。", manifestPath);
                return null;
            }

            var schemaVersion = ReadInt32(root, "schemaVersion");
            if (schemaVersion is null)
            {
                Corrupt(diagnostics, "缺少 schemaVersion 或不是整数。", manifestPath);
                return null;
            }

            var sizeBytes = ReadInt64(root, "sizeBytes");
            if (sizeBytes is null || sizeBytes < 0)
            {
                Corrupt(diagnostics, "缺少 sizeBytes 或不是非负整数。", manifestPath);
                return null;
            }

            var source = ReadString(root, "source");
            if (string.IsNullOrWhiteSpace(source))
            {
                Corrupt(diagnostics, "缺少 source。", manifestPath);
                return null;
            }

            if (!AdapterPackageWireNames.TryParseSourceKind(ReadString(root, "sourceKind"), out var sourceKind))
            {
                Corrupt(diagnostics, "sourceKind 缺失或不是 local-file/remote-url。", manifestPath);
                return null;
            }

            if (!DateTimeOffset.TryParse(ReadString(root, "installedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var installedAt))
            {
                Corrupt(diagnostics, "installedAt 缺失或不是 ISO-8601 时间。", manifestPath);
                return null;
            }

            return new AdapterInstallManifest(
                id.Trim(),
                name.Trim(),
                schemaVersion.Value,
                source,
                sourceKind,
                sha256!.Trim().ToLowerInvariant(),
                sizeBytes.Value,
                installedAt.ToUniversalTime(),
                DeclaredVersion: ReadString(root, "declaredVersion"),
                EntryPoint: ReadString(root, "entryPoint"),
                SourceEtag: ReadString(root, "sourceEtag"),
                ManifestVersion: ReadInt32(root, "manifestVersion") ?? 1);
        }
    }

    /// <summary>是否 64 位十六进制 sha256。</summary>
    /// <param name="value">待检查文本。</param>
    public static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var ch in value)
        {
            var hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static int? ReadInt32(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : null;

    private static long? ReadInt64(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value) ? value : null;
}
