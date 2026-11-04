using System.Text.Json;
using System.Text.Json.Serialization;

namespace CkanUI.Services.Profiles;

/// <summary>
/// 配置档清单（<c>&lt;GameDir&gt;/CKANProfiles/profiles.json</c>）：记当前生效档、启动档与每个档的元数据。
/// </summary>
/// <remarks>
/// <para>
/// 清单是「哪些配置档存在」的唯一权威；配置档目录本身只是数据。读取口径是**永不抛**：
/// 文件缺失、JSON 损坏、字段类型不对都回退成空清单（<see cref="ActiveProfile"/> = null），
/// 因为一个坏文件不应该让整个实例页打不开。
/// </para>
/// <para>
/// 写入走「同目录临时文件 + <see cref="File.Move(string,string,bool)"/> 覆盖」：断电或崩溃时
/// 要么是旧清单、要么是新清单，不会留下半截 JSON。UTF-8 无 BOM（<see cref="JsonSerializer.SerializeToUtf8Bytes"/> 不写 BOM）。
/// </para>
/// </remarks>
public sealed class ProfileManifest
{
    /// <summary>配置档根目录名（位于游戏根目录下）。</summary>
    public const string DirectoryName = "CKANProfiles";

    /// <summary>清单文件名。</summary>
    public const string FileName = "profiles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>当前生效的配置档；null 表示还没有接管过（live GameData 不属于任何档）。</summary>
    public string? ActiveProfile { get; set; }

    /// <summary>启动游戏时自动切到的配置档；null 表示不自动切换。</summary>
    public string? LaunchProfile { get; set; }

    /// <summary>全部配置档（顺序不保证，读取方按名字排序）。</summary>
    public List<ProfileManifestEntry> Profiles { get; set; } = new();

    /// <summary>清单文件的绝对路径。</summary>
    public static string ResolvePath(string gameDir) => Path.Combine(gameDir, DirectoryName, FileName);

    /// <summary>某个配置档目录的绝对路径。</summary>
    public static string ResolveProfileDirectory(string gameDir, string profileName) =>
        Path.Combine(gameDir, DirectoryName, profileName);

    /// <summary>按名字（忽略大小写）找配置档条目；找不到返回 null。</summary>
    public ProfileManifestEntry? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Profiles.FirstOrDefault(entry =>
            !string.IsNullOrWhiteSpace(entry.Name)
            && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>读取清单；任何读取/解析失败都返回空清单，不抛。</summary>
    public static ProfileManifest Load(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return new ProfileManifest();
        }

        var path = ResolvePath(gameDir);
        try
        {
            if (!File.Exists(path))
            {
                return new ProfileManifest();
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                return new ProfileManifest();
            }

            var manifest = JsonSerializer.Deserialize<ProfileManifest>(bytes, JsonOptions);
            if (manifest is null)
            {
                return new ProfileManifest();
            }

            manifest.Profiles ??= new List<ProfileManifestEntry>();
            manifest.Profiles.RemoveAll(entry => entry is null);
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            return new ProfileManifest();
        }
    }

    /// <summary>原子写入清单（同目录临时文件 + 覆盖移动）。</summary>
    /// <exception cref="IOException">目录不可写或移动失败。</exception>
    /// <exception cref="UnauthorizedAccessException">没有写权限。</exception>
    public void Save(string gameDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);

        var path = ResolvePath(gameDir);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        var temp = Path.Combine(directory, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件删不掉不影响结论：下次写入会用新的随机名。
        }
    }
}

/// <summary>清单里的一个配置档条目。</summary>
public sealed class ProfileManifestEntry
{
    /// <summary>配置档名（目录名，也是界面显示名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>创建时间（UTC）。</summary>
    public DateTime? CreatedUtc { get; set; }

    /// <summary>最后一次切换进来的时间（UTC）。</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>导入来源（.ckan 路径或 URL）；手工创建为 null。</summary>
    public string? Source { get; set; }

    /// <summary>模组数（.ckan 的 depends + recommends）；未知为 0。</summary>
    public int ModCount { get; set; }
}
