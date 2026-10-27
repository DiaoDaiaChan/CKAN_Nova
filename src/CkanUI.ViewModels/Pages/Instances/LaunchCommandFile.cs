using System.Text.Json;
using System.Text.Json.Nodes;
using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 启动命令的实例级落盘实现（F-06）：<c>&lt;实例目录&gt;/CKAN/GUIConfig.json</c> 的 <c>CommandLines</c> 键。
/// </summary>
/// <remarks>
/// <para>选这个文件而不是新造一个，是因为上游老 GUI 就把命令列表存在这里
/// （<c>external/CKAN/GUI/Model/GUIConfiguration.cs</c>：<c>List&lt;string&gt; CommandLines</c>，
/// <c>ConfigPath(instance) = &lt;GameDir&gt;/CKAN/GUIConfig.json</c>，Newtonsoft 序列化成 PascalCase 键）。
/// 双方共用一份数据，用户从老 GUI 切过来时命令列表不会丢。</para>
/// <para>写回时必须原样保留文件里的其它字段（窗口尺寸、过滤器、托盘开关…），只改 <c>CommandLines</c>，
/// 否则会把老 GUI 的设置清空；先写临时文件再替换，避免半截 JSON。</para>
/// </remarks>
public sealed class LaunchCommandFile : IInstanceCommandStore
{
    /// <summary>实例内 CKAN 数据目录名。</summary>
    public const string DirectoryName = "CKAN";

    /// <summary>配置文件（老 GUI 与 Core 共用）。</summary>
    public const string FileName = "GUIConfig.json";

    /// <summary>命令列表的 JSON 键（Newtonsoft 默认 PascalCase）。</summary>
    public const string CommandLinesKey = "CommandLines";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>拼出某个实例的 GUIConfig.json 路径。</summary>
    /// <param name="gameDirectory">实例的游戏目录（<c>InstanceInfo.GameDir</c>）。</param>
    public static string PathFor(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameDirectory);
        return Path.Combine(gameDirectory, DirectoryName, FileName);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DefaultsFor(string gameDirectory) =>
        LaunchCommandDefaults.ForThisMachine(gameDirectory);

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>?>> ReadAsync(
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(gameDirectory);
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult<IReadOnlyList<string>?>.Ok(null);
            }

            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return OperationResult<IReadOnlyList<string>?>.Ok(ReadCommands(text));
        }
        catch (JsonException ex)
        {
            return OperationResult<IReadOnlyList<string>?>.Fail("启动命令文件不是合法 JSON", ex.Message);
        }
        catch (IOException ex)
        {
            return OperationResult<IReadOnlyList<string>?>.Fail("读取启动命令失败", ex.Message, retryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<IReadOnlyList<string>?>.Fail("没有读取启动命令的权限", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> WriteAsync(
        string gameDirectory,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var path = PathFor(gameDirectory);
        try
        {
            var existing = File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : null;

            var root = ParseObject(existing) ?? new JsonObject();
            var array = new JsonArray();
            foreach (var command in LaunchCommandLine.NormalizeList(commands))
            {
                array.Add(command);
            }

            root[CommandLinesKey] = array;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp";
            await File
                .WriteAllTextAsync(tempPath, root.ToJsonString(WriteOptions) + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);

            return OperationResult.Ok();
        }
        catch (JsonException ex)
        {
            return OperationResult.Fail("启动命令文件不是合法 JSON，已拒绝覆盖", ex.Message);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail("保存启动命令失败", ex.Message, retryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail("没有保存启动命令的权限", ex.Message, retryable: true);
        }
    }

    private static IReadOnlyList<string>? ReadCommands(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty(CommandLinesKey, out var commands)
            || commands.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<string>();
        foreach (var item in commands.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add(item.GetString() ?? string.Empty);
            }
        }

        var normalized = LaunchCommandLine.NormalizeList(result);
        return normalized.Count == 0 ? null : normalized;
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
