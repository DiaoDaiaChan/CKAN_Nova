using System.Globalization;
using System.Text;
using System.Text.Json;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.History;

/// <summary>
/// 落盘格式的唯一真源：<c>install_history.jsonl</c> 里的一行。
/// </summary>
/// <remarks>
/// <para>
/// 一次操作写两行：<c>kind=entry</c>（条目：时间戳/操作/结果/变更明细）与
/// <c>kind=snapshot</c>（同一时间戳的已装集合快照，重装的回滚目标）。共用时间戳是为了
/// 让"回滚到某条历史"能直接找到它自己的快照，不依赖行序。
/// </para>
/// <para>
/// 枚举以字符串落盘（<c>"Install"</c> 而不是 <c>0</c>）：文件要能用文本编辑器读、能 grep，
/// 也不怕以后重排枚举成员。字段名 camelCase。
/// </para>
/// </remarks>
public sealed record InstallHistoryLine(
    string Kind,
    DateTimeOffset At,
    string Instance,
    InstallHistoryOperation Operation,
    InstallHistoryOutcome Outcome,
    IReadOnlyList<InstallHistoryChange> Changes,
    IReadOnlyList<InstalledModuleVersion> Modules,
    string? Message)
{
    /// <summary>条目行。</summary>
    public const string EntryKind = "entry";

    /// <summary>快照行。</summary>
    public const string SnapshotKind = "snapshot";

    /// <summary>时间戳的落盘格式（ISO-8601 往返格式，保留偏移量）。</summary>
    public const string TimestampFormat = "O";

    /// <summary>构造一条条目行。</summary>
    public static InstallHistoryLine Entry(
        DateTimeOffset at,
        string instance,
        InstallHistoryOperation operation,
        InstallHistoryOutcome outcome,
        IReadOnlyList<InstallHistoryChange> changes,
        string? message = null)
        => new(
            EntryKind,
            at,
            instance,
            operation,
            outcome,
            changes,
            Array.Empty<InstalledModuleVersion>(),
            message);

    /// <summary>构造一条快照行。</summary>
    public static InstallHistoryLine Snapshot(
        DateTimeOffset at,
        string instance,
        IReadOnlyList<InstalledModuleVersion> modules)
        => new(
            SnapshotKind,
            at,
            instance,
            InstallHistoryOperation.Install,
            InstallHistoryOutcome.Succeeded,
            Array.Empty<InstallHistoryChange>(),
            modules,
            null);

    /// <summary>是否快照行。</summary>
    public bool IsSnapshot => string.Equals(Kind, SnapshotKind, StringComparison.Ordinal);

    /// <summary>序列化成一行 JSON（不含换行）。</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", Kind);
            writer.WriteString("at", At.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteString("instance", Instance);
            writer.WriteString("operation", Operation.ToString());
            writer.WriteString("outcome", Outcome.ToString());

            writer.WriteStartArray("changes");
            foreach (var change in Changes)
            {
                writer.WriteStartObject();
                writer.WriteString("identifier", change.Identifier);
                writer.WriteString("action", change.Action.ToString());
                writer.WriteString("fromVersion", change.FromVersion);
                writer.WriteString("toVersion", change.ToVersion);
                writer.WriteBoolean("succeeded", change.Succeeded);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("modules");
            foreach (var module in Modules)
            {
                writer.WriteStartObject();
                writer.WriteString("identifier", module.Identifier);
                writer.WriteString("version", module.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (Message is not null)
            {
                writer.WriteString("message", Message);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// 解析一行；任何不认识的东西（坏 JSON、未知 kind、缺时间戳、未知枚举）都返回 false，
    /// 由调用方跳过——历史文件被手工编辑过也不能让页面炸掉。
    /// </summary>
    public static bool TryParse(string json, out InstallHistoryLine? line)
    {
        line = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var kind = ReadString(root, "kind");
            if (kind is not (EntryKind or SnapshotKind))
            {
                return false;
            }

            var atText = ReadString(root, "at");
            if (atText is null
                || !DateTimeOffset.TryParse(
                    atText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var at))
            {
                return false;
            }

            if (!TryReadEnum<InstallHistoryOperation>(ReadString(root, "operation"), out var operation)
                || !TryReadEnum<InstallHistoryOutcome>(ReadString(root, "outcome"), out var outcome))
            {
                return false;
            }

            line = new InstallHistoryLine(
                kind,
                at,
                ReadString(root, "instance") ?? string.Empty,
                operation,
                outcome,
                ReadChanges(root),
                ReadModules(root),
                ReadString(root, "message"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // ValueKind 与预期不符（例如 "changes" 是字符串而不是数组）。
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadEnum<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        if (text is not null && Enum.TryParse<TEnum>(text, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    private static IReadOnlyList<InstallHistoryChange> ReadChanges(JsonElement root)
    {
        if (!root.TryGetProperty("changes", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<InstallHistoryChange>();
        }

        var changes = new List<InstallHistoryChange>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var identifier = ReadString(item, "identifier");
            if (string.IsNullOrWhiteSpace(identifier)
                || !TryReadEnum<ChangeAction>(ReadString(item, "action"), out var action))
            {
                continue;
            }

            var succeeded = !item.TryGetProperty("succeeded", out var flag)
                || flag.ValueKind != JsonValueKind.False;
            changes.Add(new InstallHistoryChange(
                identifier,
                action,
                ReadString(item, "fromVersion"),
                ReadString(item, "toVersion"),
                succeeded));
        }

        return changes;
    }

    private static IReadOnlyList<InstalledModuleVersion> ReadModules(JsonElement root)
    {
        if (!root.TryGetProperty("modules", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<InstalledModuleVersion>();
        }

        var modules = new List<InstalledModuleVersion>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var identifier = ReadString(item, "identifier");
            var version = ReadString(item, "version");
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            modules.Add(new InstalledModuleVersion(identifier, version));
        }

        return modules;
    }
}
