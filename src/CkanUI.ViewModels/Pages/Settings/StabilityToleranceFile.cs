using System.Text.Json;
using System.Text.Json.Nodes;
using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 稳定版容差文件（<c>&lt;实例目录&gt;/CKAN/stability_tolerance.json</c>）的读写（UI-30 的兼容性组 与 F-13 单模组覆盖）。
/// </summary>
/// <remarks>
/// 该文件是**实例级**的，不属于 Core 的 config.json，所以 <c>ISettingsService</c> 读写不到它；
/// 又因为 Core 的 <c>StabilityToleranceConfig</c> 类型只在 Services 层可达（分层铁律），
/// 这里按文件契约直接读写 JSON：
/// 支持 <c>overall_stability_tolerance</c> 与 <c>mod_stability_tolerance</c> 字典的原子读写。
/// 修改某项时必须原样保留另一项及其它字段，否则会把用户的其它配置清空。
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

    /// <summary>验证容差是否为已知合法取值（区分 null/非法输入）。</summary>
    public static bool IsValidTolerance(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var known in KnownTolerances)
        {
            if (string.Equals(known, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

            await AtomicallyWriteAsync(path, root, cancellationToken).ConfigureAwait(false);

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

    /// <summary>按游戏目录保存总体容差（F-13 / UI-30 语义别名）。</summary>
    public Task<OperationResult> SaveOverallAsync(string gameDir, string overall, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameDir);
        return WriteAsync(PathFor(gameDir), overall, cancellationToken);
    }

    /// <summary>
    /// 读取某模组的单模组容差覆盖（F-13 / UI-07 / UI-29）。
    /// 若无单模组覆盖或配置为 null 则返回 null（表示继承全局默认）。
    /// </summary>
    /// <param name="gameDir">游戏实例根目录。</param>
    /// <param name="identifier">模组标识符。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>返回指定的容差字符串（stable/testing/development），若未覆盖或值非法则返回 null。</returns>
    public async Task<OperationResult<string?>> GetModTolerance(string gameDir, string identifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameDir);
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var path = PathFor(gameDir);
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult<string?>.Ok(null);
            }

            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return OperationResult<string?>.Ok(null);
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OperationResult<string?>.Ok(null);
            }

            if (document.RootElement.TryGetProperty(ModKey, out var modDict) &&
                modDict.ValueKind == JsonValueKind.Object &&
                modDict.TryGetProperty(identifier, out var modTolerance) &&
                modTolerance.ValueKind == JsonValueKind.String)
            {
                var val = modTolerance.GetString();
                if (IsValidTolerance(val))
                {
                    return OperationResult<string?>.Ok(NormalizeTolerance(val));
                }
            }

            return OperationResult<string?>.Ok(null);
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

    /// <summary>
    /// 设置或移除某模组的单模组容差覆盖（F-13 / UI-07 / UI-29）。
    /// <paramref name="tolerance"/> 为 null 或空时移除覆盖（继承全局），否则设置指定覆盖。
    /// 原子写入文件并保留其它字段（包括 overall_stability_tolerance 和其它模组的覆盖）。
    /// </summary>
    /// <param name="gameDir">游戏实例根目录。</param>
    /// <param name="identifier">模组标识符。</param>
    /// <param name="tolerance">模组容差（stable/testing/development 或 null 表示移除）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task<OperationResult> SetModTolerance(string gameDir, string identifier, string? tolerance, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameDir);
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        if (tolerance is not null && !IsValidTolerance(tolerance))
        {
            return OperationResult.Fail("稳定版容差取值无效", tolerance);
        }

        var path = PathFor(gameDir);
        try
        {
            var existing = File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : null;

            var root = ParseObject(existing) ?? new JsonObject();

            // 检查或创建 mod_stability_tolerance 字典
            JsonObject modObj;
            if (root.TryGetPropertyValue(ModKey, out var node) && node is JsonObject existingModObj)
            {
                modObj = existingModObj;
            }
            else
            {
                modObj = new JsonObject();
                root[ModKey] = modObj;
            }

            if (tolerance is null)
            {
                modObj.Remove(identifier);
            }
            else
            {
                modObj[identifier] = NormalizeTolerance(tolerance);
            }

            await AtomicallyWriteAsync(path, root, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// 同步读取单模组稳定性容差覆盖（供同步属性或查询场景快速读取，文件不存在或未配置返回 null）。
    /// </summary>
    public string? GetModToleranceSync(string gameDir, string identifier)
    {
        if (string.IsNullOrEmpty(gameDir) || string.IsNullOrEmpty(identifier))
        {
            return null;
        }

        var path = PathFor(gameDir);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty(ModKey, out var modDict) &&
                modDict.ValueKind == JsonValueKind.Object &&
                modDict.TryGetProperty(identifier, out var modTolerance) &&
                modTolerance.ValueKind == JsonValueKind.String)
            {
                var val = modTolerance.GetString();
                if (IsValidTolerance(val))
                {
                    return NormalizeTolerance(val);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>便捷静态方法：读取某模组的单模组容差覆盖（null 表示未覆盖或继承全局）。</summary>
    public static async Task<string?> GetModToleranceAsync(string gameDir, string identifier, CancellationToken cancellationToken = default)
    {
        var result = await new StabilityToleranceFile().GetModTolerance(gameDir, identifier, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>便捷静态方法：设置或移除某模组的单模组容差覆盖（tolerance 为 null 时移除）。</summary>
    public static async Task<bool> SetModToleranceAsync(string gameDir, string identifier, string? tolerance, CancellationToken cancellationToken = default)
    {
        var result = await new StabilityToleranceFile().SetModTolerance(gameDir, identifier, tolerance, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    /// <summary>便捷静态方法：保存实例级总体稳定性容差。</summary>
    public static async Task<bool> SaveOverallToleranceAsync(string gameDir, string overall, CancellationToken cancellationToken = default)
    {
        var result = await new StabilityToleranceFile().SaveOverallAsync(gameDir, overall, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    private static async Task AtomicallyWriteAsync(string path, JsonObject root, CancellationToken cancellationToken)
    {
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
