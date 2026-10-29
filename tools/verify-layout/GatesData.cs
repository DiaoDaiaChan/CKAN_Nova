using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CkanUI.Tools.VerifyLayout;

/// <summary>H-06：tests/fixtures 的 sha256 清单（--record 写入、--verify 比对）。</summary>
internal static class H06FixtureHashes
{
    public const string Id = "H-06";

    public const string DefaultManifest = "tools/verify-layout/fixtures.sha256";

    private const string FixturesRoot = "tests/fixtures";

    /// <summary>记录模式：把当前 fixtures 的 sha256 写成清单（相对仓库根的路径，按序数排序）。</summary>
    public static int Record(GateContext context, string manifestRelative)
    {
        var entries = Snapshot(context);
        var builder = new StringBuilder();
        foreach (var (path, hash) in entries)
        {
            builder.Append(hash).Append("  ").Append(path).Append('\n');
        }

        var manifestPath = Path.Combine(context.Repo.Root, manifestRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? context.Repo.Root);
        File.WriteAllText(manifestPath, builder.ToString(), new UTF8Encoding(false));
        return entries.Count;
    }

    /// <summary>校验模式：清单与磁盘逐项比对（改动/缺失/未登记）。</summary>
    public static void Verify(GateContext context, string manifestRelative)
    {
        var manifestPath = Path.Combine(context.Repo.Root, manifestRelative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestPath))
        {
            context.Add(Id, GateLevel.Blocking, $"缺少夹具清单: {manifestRelative}（先跑 --record 生成）");
            return;
        }

        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(manifestPath))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split("  ", 2, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                context.Add(Id, GateLevel.Blocking, $"夹具清单格式错误: {manifestRelative}: {line}");
                continue;
            }

            expected[parts[1]] = parts[0];
        }

        var actual = Snapshot(context);
        foreach (var (path, hash) in actual)
        {
            if (!expected.TryGetValue(path, out var recorded))
            {
                context.Add(Id, GateLevel.Blocking, $"夹具未登记: {path}（新增夹具后需 --record 更新清单）");
                continue;
            }

            if (!string.Equals(recorded, hash, StringComparison.Ordinal))
            {
                context.Add(Id, GateLevel.Blocking, $"夹具被测试改写: {path}");
            }
        }

        var present = actual.Select(pair => pair.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var path in expected.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!present.Contains(path))
            {
                context.Add(Id, GateLevel.Blocking, $"夹具被测试改写: {path}（清单里有，磁盘上没有）");
            }
        }

        context.Note(Id, $"清单 {expected.Count} 项 / 磁盘 {actual.Count} 项");
    }

    private static List<(string Path, string Hash)> Snapshot(GateContext context)
    {
        var fixtures = Path.Combine(context.Repo.Root, FixturesRoot.Replace('/', Path.DirectorySeparatorChar));
        var results = new List<(string Path, string Hash)>();
        if (!Directory.Exists(fixtures))
        {
            return results;
        }

        foreach (var file in Directory.EnumerateFiles(fixtures, "*", SearchOption.AllDirectories))
        {
            var relative = context.Repo.Relative(file);
            if (Noise.IsNoise(relative))
            {
                continue;
            }

            using var stream = File.OpenRead(file);
            results.Add((relative, Convert.ToHexStringLower(SHA256.HashData(stream))));
        }

        results.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return results;
    }
}

/// <summary>H-08：adapters/**/adapter.json 过 JSON Schema（schema 见 15 篇 §4.1）。</summary>
internal static class H08AdapterSchema
{
    public const string Id = "H-08";

    /// <summary>
    /// schema 的唯一真源。旧路径 tools/verify-layout/adapter.schema.json 是旧项目版（缺
    /// custom / id / lineEnding / separator / readonlyReason / label / default 等字段），已删除；
    /// 这里不再保留兜底路径——缺文件即阻断，避免门禁拿过期 schema 静默放行。
    /// </summary>
    private const string DesignSchema = "docs/design/adapter.schema.json";

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static void Run(GateContext context)
    {
        var schemaPath = Path.Combine(context.Repo.Root, DesignSchema.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(schemaPath))
        {
            context.Add(
                Id,
                GateLevel.Blocking,
                $"缺少 adapter schema: {DesignSchema}（H-08 唯一真源；旧路径 tools/verify-layout/adapter.schema.json 已删除，不存在兜底）");
            return;
        }

        var adapters = context.EnumerateWorkingTreeFiles("adapters")
            .Where(path => path.EndsWith("/adapter.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (adapters.Count == 0)
        {
            context.Note(Id, "adapters/ 下暂无 adapter.json");
            return;
        }

        foreach (var adapter in adapters)
        {
            var full = Path.Combine(context.Repo.Root, adapter.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                using var instance = JsonDocument.Parse(File.ReadAllText(full), ParseOptions);
                foreach (var error in JsonSchemaSubset.Validate(schemaPath, instance.RootElement))
                {
                    context.Add(Id, GateLevel.Blocking, $"adapter 校验失败: {adapter}: {error}");
                }
            }
            catch (JsonException ex)
            {
                context.Add(Id, GateLevel.Blocking, $"adapter 校验失败: {adapter}: JSON 语法错误 {ex.Message}");
            }
        }

        context.Note(Id, $"schema = {DesignSchema}，adapter {adapters.Count} 个");
    }
}

/// <summary>H-11：--prune 按 21 篇 §9 清理临时区（artifacts 7 天 / spikes 14 天 / docs/_drafts 30 天）。</summary>
internal static class H11PruneTemporaryAreas
{
    public const string Id = "H-11";

    private static readonly (string Directory, int RetentionDays)[] Targets =
    [
        ("artifacts", 7),
        ("spikes", 14),
        ("docs/_drafts", 30),
    ];

    public static void Run(GateContext context, bool dryRun)
    {
        var pruned = 0;
        foreach (var (directory, retentionDays) in Targets)
        {
            var absolute = Path.Combine(context.Repo.Root, directory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absolute))
            {
                continue;
            }

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var child in Directory.EnumerateFileSystemEntries(absolute).OrderBy(p => p, StringComparer.Ordinal))
            {
                // 这里枚举的正是临时区的直接子项，不能套 Noise 的前缀规则（那会把 artifacts/x 也判成噪声）。
                // 只放过用来占位的版本库文件。
                var childName = Path.GetFileName(child);
                if (childName is ".gitkeep" or ".gitignore" or ".gitattributes")
                {
                    continue;
                }

                DateTime newest;
                try
                {
                    newest = NewestWriteTimeUtc(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 读不到时间戳（权限/占用）就跳过，交给下一轮，不能因此中断整轮清理。
                    context.Add(Id, GateLevel.Warning, $"跳过（无法读取时间戳）: {context.Repo.Relative(child)}: {ex.Message}");
                    continue;
                }

                if (newest >= cutoff)
                {
                    continue;
                }

                var relative = context.Repo.Relative(child);
                pruned++;
                if (dryRun)
                {
                    context.Add(Id, GateLevel.Warning, $"将清理（dry-run）: {relative}（最后写入 {newest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}，超 {retentionDays} 天）");
                    continue;
                }

                try
                {
                    if (Directory.Exists(child))
                    {
                        Directory.Delete(child, recursive: true);
                    }
                    else
                    {
                        File.Delete(child);
                    }

                    context.Add(Id, GateLevel.Warning, $"已清理: {relative}（超 {retentionDays} 天）");
                }
                catch (IOException ex)
                {
                    context.Add(Id, GateLevel.Warning, $"清理失败: {relative}: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    context.Add(Id, GateLevel.Warning, $"清理失败: {relative}: {ex.Message}");
                }
            }
        }

        context.Note(Id, $"已清理: {pruned} 项{(dryRun ? "（dry-run，未真正删除）" : string.Empty)}");
    }

    private static DateTime NewestWriteTimeUtc(string path)
    {
        if (File.Exists(path))
        {
            return File.GetLastWriteTimeUtc(path);
        }

        var newest = Directory.GetLastWriteTimeUtc(path);
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var stamp = File.GetLastWriteTimeUtc(file);
            if (stamp > newest)
            {
                newest = stamp;
            }
        }

        return newest;
    }
}
