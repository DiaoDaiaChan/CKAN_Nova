using System.Text.Json;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ModPacks;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="IModPackStore"/> 的假实现：用最小 JSON 读写模拟 .ckan 的 depends/recommends 语义，
/// 让导入导出的往返可以在没有游戏目录、没有网络、没有 CKAN 序列化器的环境下被测。
/// 序列化细节由上游负责（见 CkanModPackStore 的注释），这里只保证字段名与语义一致。
/// </summary>
internal sealed class FakeModPackStore : IModPackStore
{
    public bool HasActiveInstance { get; set; } = true;

    public string? InstanceName { get; set; } = "测试实例";

    /// <summary>当前实例的已装模组。</summary>
    public List<InstalledModuleInfo> Installed { get; } = new();

    /// <summary>其它实例的已装模组（实例名 → 列表）。</summary>
    public Dictionary<string, List<InstalledModuleInfo>> Instances { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>仓库索引：标识 → 可用性。</summary>
    public Dictionary<string, ModPackAvailability> Availability { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>缓存里的下载包：<c>标识-版本</c> → 文件路径。</summary>
    public Dictionary<string, string> Archives { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>这些文件名在解析时抛异常（模拟损坏的元数据包）。</summary>
    public HashSet<string> BrokenFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool LastUseRecommends { get; private set; }

    public bool LastWithVersions { get; private set; } = true;

    /// <summary>读取已装模组时抛出的异常（模拟注册表被锁）。</summary>
    public Exception? InstalledFailure { get; set; }

    public string? Json { get; set; }

    public void SeedInstalled(string identifier, string version, bool autoInstalled = false)
        => Installed.Add(new InstalledModuleInfo(identifier, version, autoInstalled, null, DateTime.UtcNow));

    public void SeedInstance(string instanceName, params (string Identifier, string Version)[] modules)
        => Instances[instanceName] = modules
            .Select(pair => new InstalledModuleInfo(pair.Identifier, pair.Version, false, null, DateTime.UtcNow))
            .ToList();

    public void SeedAvailable(string identifier, string latest = "1.0.0", bool requestedExists = true)
        => Availability[identifier] = new ModPackAvailability(true, latest, requestedExists);

    public void SeedArchive(string identifier, string version, string filePath)
    {
        Archives[$"{identifier}-{version}"] = filePath;
        File.WriteAllText(filePath, "fake zip bytes");
    }

    public IReadOnlyList<InstalledModuleInfo> InstalledModules()
    {
        if (InstalledFailure is not null)
        {
            throw InstalledFailure;
        }

        return Installed.ToList();
    }

    public IReadOnlyList<InstalledModuleInfo> InstalledModulesIn(string instanceName)
    {
        if (!Instances.TryGetValue(instanceName, out var modules))
        {
            throw new InvalidOperationException($"找不到游戏实例“{instanceName}”。");
        }

        return modules.ToList();
    }

    public string BuildModPackJson(bool useRecommends, bool withVersions)
    {
        LastUseRecommends = useRecommends;
        LastWithVersions = withVersions;

        if (Json is not null)
        {
            return Json;
        }

        var relationships = Installed
            .Where(module => !module.IsAutoInstalled)
            .OrderBy(module => module.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(module => withVersions
                ? new Dictionary<string, string> { ["name"] = module.Identifier, ["version"] = module.Version }
                : new Dictionary<string, string> { ["name"] = module.Identifier })
            .ToList();

        var document = new Dictionary<string, object>
        {
            ["spec_version"] = "v1.6",
            ["identifier"] = "TestModpack",
            ["name"] = InstanceName ?? "modpack",
            ["version"] = "2024.01.01.00.00.00",
            ["kind"] = "metapackage",
            [useRecommends ? "recommends" : "depends"] = relationships,
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    public ModPackFile LoadModPackFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (BrokenFiles.Contains(fileName))
        {
            throw new InvalidOperationException("缺少 identifier 字段。");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
        var version = root.TryGetProperty("version", out var versionElement) ? versionElement.GetString() ?? string.Empty : string.Empty;

        var entries = new List<ModPackReference>();
        foreach (var key in new[] { "depends", "recommends" })
        {
            if (!root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in array.EnumerateArray())
            {
                var identifier = item.TryGetProperty("name", out var identifierElement) ? identifierElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    continue;
                }

                var requested = item.TryGetProperty("version", out var requestedElement) ? requestedElement.GetString() : null;
                entries.Add(new ModPackReference(identifier, requested, IsOptional: key == "recommends"));
            }
        }

        return new ModPackFile(path, name, version, entries);
    }

    public ModPackAvailability Lookup(string identifier, string? version)
        => Availability.TryGetValue(identifier, out var availability) ? availability : ModPackAvailability.NotFound;

    public string? CachedArchive(string identifier, string version)
        => Archives.TryGetValue($"{identifier}-{version}", out var path) ? path : null;
}
