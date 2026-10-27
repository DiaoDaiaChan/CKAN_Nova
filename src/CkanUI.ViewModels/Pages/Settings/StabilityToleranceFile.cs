using System.Text.Json;
using System.Text.Json.Nodes;
using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 稳定版容差文件（<c>&lt;实例目录&gt;/CKAN/stability_tolerance.json</c>）的读写（UI-30 的兼容性组）。
/// </summary>
/// <remarks>
/// 该文件是**实例级**的，不属于 Core 的 config.json，所以 <c>ISettingsService</c> 读写不到它；
/// 又因为 Core 的 <c>StabilityToleranceConfig</c> 类型只在 Services 层可达（分层铁律），
/// 这里按文件契约直接读写 JSON：只改 <c>overall_stability_tolerance</c>，
/// 必须原样保留 <c>mod_stability_tolerance</c> 等其它字段，否则会把用户按 mod 设的容差清空。
/// 纯 IO、不碰 Avalonia，可 headless 单测。
/// </remarks>
public sealed class StabilityToleranceFile
{
    /// <summary>实例内 CKAN 数据目录名。</summary>
    public const string DirectoryName = "CKAN";

    /// <summary>容差文件名。</summary>
    public const string FileName = "stability_tolerance.json";

    /// <summary>总体容差的 JSON 键。</summary>
    public const string OverallKey = "overall_stability_tolerance";

    /// <summary>按 mod 覆盖容差的 JSON 键（写回时必须保留）。</summary>
    public const string ModKey = "mod_stability_tolerance";

    /// <summary>文件缺省时的总体容差（与 Core 的 <c>ReleaseStatus.stable</c> 对应）。</summary>
    public const string DefaultTolerance = "stable";

    /// <summary>可用的总体容差取值（与 Core 的 ReleaseStatus 成员一致）。</summary>
    public static readonly IReadOnlyList<string> KnownTolerances = new[] { "stable", "testing", "development" };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>拼出某个实例的容差文件路径。</summary>
    /// <param name="gameDirectory">实例的游戏目录（<c>InstanceInfo.GameDir</c>）。</param>
    public static string PathFor(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameDirectory);
        return Path.Combine(gameDirectory, DirectoryName, FileName);
    }

    /// <summary>把任意字符串规整成合法容差取值（未知值按缺省 stable 处理，避免把脏值写回）。</summary>
    /// <param name="value">原始取值。</param>
    public static string NormalizeTolerance(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultTolerance;
        }

        foreach (var known in KnownTolerances)
        {
            if (string.Equals(known, value, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return DefaultTolerance;
    }

    /// <summary>读取总体容差；文件不存在或字段缺失都按缺省 stable 返回（不报错）。</summary>
    /// <param name="path">容差文件路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task<OperationResult<string?>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            if (!File.Exists(path))
            {
                return OperationResult<string?>.Ok(DefaultTolerance);
            }

            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return OperationResult<string?>.Ok(ReadOverall(text));
        }
        catch (JsonException ex)
        {
            return OperationResult<string?>.Fail("稳定版容差文件不是合法 JSON", ex.Message);
        }
        catch (IOException ex)
        {
            return OperationResult<string?>.Fail("读取稳定版容差文件失败", ex.Message, retryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<string?>.Fail("没有读取稳定版容差文件的权限", ex.Message, retryable: true);
        }
    }

    /// <summary>写入总体容差；保留文件里的其它字段（尤其 <c>mod_stability_tolerance</c>）。</summary>
    /// <param name="path">容差文件路径。</param>
    /// <param name="overall">总体容差取值（stable / testing / development）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task<OperationResult> WriteAsync(string path, string overall, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var normalized = NormalizeTolerance(overall);
        if (!string.Equals(normalized, overall, StringComparison.Ordinal))
        {
            return OperationResult.Fail("稳定版容差取值无效", overall);
        }

        try
        {
            var existing = File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : null;

            var root = ParseObject(existing) ?? new JsonObject();
            root[OverallKey] = normalized;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 先写临时文件再替换，避免写到一半断电留下半截 JSON（Core 读它会静默失败）。
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, root.ToJsonString(WriteOptions) + Environment.NewLine, cancellationToken)
                      .ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);

            return OperationResult.Ok();
        }
        catch (JsonException ex)
        {
            return OperationResult.Fail("稳定版容差文件不是合法 JSON，已拒绝覆盖", ex.Message);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail("写入稳定版容差文件失败", ex.Message, retryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail("没有写入稳定版容差文件的权限", ex.Message, retryable: true);
        }
    }

    private static string ReadOverall(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultTolerance;
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return DefaultTolerance;
        }

        return document.RootElement.TryGetProperty(OverallKey, out var overall)
               && overall.ValueKind == JsonValueKind.String
            ? NormalizeTolerance(overall.GetString())
            : DefaultTolerance;
    }

    private static JsonObject? ParseObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return JsonNode.Parse(text) as JsonObject;
    }
}
