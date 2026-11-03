using System.Globalization;
using System.Text.Json;
using CkanUI.Services.Abstractions;
using CkanUI.Sfs;

namespace CkanUI.Plugin.Rp1.Career;

/// <summary>RP-1 生涯日志的默认服务：列 &lt;gameDir&gt;/saves 下的 persistent.sfs，读一份并构建时间线。</summary>
public sealed class Rp1CareerService : IRp1CareerService
{
    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<Rp1SaveSummary>>> ListSavesAsync(
        string gameDir,
        CancellationToken cancellationToken = default)
        => Task.Run(() => ListSaves(gameDir, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<Rp1CareerTimeline>> LoadAsync(
        string gameDir,
        string saveName,
        string? instanceName = null,
        string? gameVersion = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Load(gameDir, saveName, instanceName, gameVersion, cancellationToken), cancellationToken);

    private static OperationResult<IReadOnlyList<Rp1SaveSummary>> ListSaves(string gameDir, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            return OperationResult<IReadOnlyList<Rp1SaveSummary>>.Fail("当前实例没有可用的游戏目录。");
        }

        var saves = Path.Combine(gameDir, "saves");
        if (!Directory.Exists(saves))
        {
            return OperationResult<IReadOnlyList<Rp1SaveSummary>>.Ok([]);
        }

        try
        {
            var result = new List<Rp1SaveSummary>();
            foreach (var directory in Directory.EnumerateDirectories(saves))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var persistent = Path.Combine(directory, "persistent.sfs");
                if (!File.Exists(persistent))
                {
                    continue;
                }

                var info = new FileInfo(persistent);
                result.Add(new Rp1SaveSummary(
                    Path.GetFileName(directory),
                    directory,
                    persistent,
                    info.LastWriteTimeUtc,
                    info.Length));
            }

            result.Sort((left, right) => right.LastWriteUtc.CompareTo(left.LastWriteUtc));
            return OperationResult<IReadOnlyList<Rp1SaveSummary>>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<Rp1SaveSummary>>.Fail("列存档失败：" + ex.Message, saves);
        }
    }

    private static OperationResult<Rp1CareerTimeline> Load(
        string gameDir,
        string saveName,
        string? instanceName,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            return OperationResult<Rp1CareerTimeline>.Fail("当前实例没有可用的游戏目录。");
        }

        if (string.IsNullOrWhiteSpace(saveName) || saveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return OperationResult<Rp1CareerTimeline>.Fail("存档名不合法。");
        }

        var savesRoot = Path.GetFullPath(Path.Combine(gameDir, "saves"));
        var saveDirectory = Path.GetFullPath(Path.Combine(savesRoot, saveName));
        if (!saveDirectory.StartsWith(savesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<Rp1CareerTimeline>.Fail("存档名不合法。");
        }

        var persistent = Path.Combine(saveDirectory, "persistent.sfs");
        if (!File.Exists(persistent))
        {
            return OperationResult<Rp1CareerTimeline>.Fail("这个存档里没有 persistent.sfs。", persistent);
        }

        var scenario = Rp1SaveReader.Read(persistent, cancellationToken);
        if (!scenario.IsSuccess || scenario.Value is null)
        {
            return OperationResult<Rp1CareerTimeline>.Fail(scenario.Message ?? "读取存档失败。", scenario.Detail, scenario.Retryable);
        }

        var catalog = Rp1DefinitionCatalog.Load(gameDir);
        var version = ReadRp1Version(gameDir);
        var timeline = Rp1TimelineBuilder.Build(
            gameDir,
            saveName,
            persistent,
            scenario.Value,
            catalog,
            version,
            gameVersion);
        return OperationResult<Rp1CareerTimeline>.Ok(timeline);
    }

    private static string? ReadRp1Version(string gameDir)
    {
        var path = Path.Combine(gameDir, "GameData", "RP-1", "RP-1.version");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("VERSION", out var version) || version.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var key in new[] { "MAJOR", "MINOR", "PATCH", "BUILD" })
            {
                if (version.TryGetProperty(key, out var part))
                {
                    parts.Add(part.ValueKind == JsonValueKind.String ? part.GetString() ?? "0" : part.GetRawText());
                }
            }

            return parts.Count == 0 ? null : string.Join('.', parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
