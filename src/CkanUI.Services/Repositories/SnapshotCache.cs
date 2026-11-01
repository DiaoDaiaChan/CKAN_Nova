using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CkanUI.Services.Abstractions.Dtos;
using log4net;

namespace CkanUI.Services.Repositories;

/// <summary>
/// 模组列表投影的磁盘缓存（docs/ops/perf-baseline.md §17）。
/// 36 MB 的仓库索引解析成 CKAN 对象图要 3–5 秒、分配约 1.8 GB，是"模组列表什么时候出来"的固定地板；
/// 而投影出来的行只由几个文件的内容决定，所以把行缓存在磁盘上，指纹一致时直接出列表，不解析。
/// </summary>
/// <remarks>
/// 只做加速、不做权威：读失败、写失败、结构不匹配、指纹不一致，一律安静地退回真实解析路径，
/// 绝不因为缓存出问题而让用户看不到列表。指纹覆盖实例目录、仓库清单、每个索引文件与
/// registry.json 的大小和修改时间，以及缓存结构版本 —— 任何一项变化都视为失效。
/// </remarks>
public sealed class SnapshotCache
{
    /// <summary>缓存结构版本：行投影的字段含义变化时递增，旧文件自然失效。</summary>
    public const int SchemaVersion = 1;

    private static readonly ILog Log = LogManager.GetLogger(typeof(SnapshotCache));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _directory;

    public SnapshotCache(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory();
    }

    /// <summary>默认落盘位置：%LOCALAPPDATA%/CKAN/ckanui-cache（与 CKAN 自己的 repos 缓存同级）。</summary>
    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKAN",
        "ckanui-cache");

    /// <summary>
    /// 输入指纹：给定的每一段按顺序拼接后取 SHA-256。段里出现文件的时间戳与大小，
    /// 所以索引文件、注册表任何重写都会改变指纹。
    /// </summary>
    public static string KeyFrom(IEnumerable<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    /// <summary>把文件的大小与修改时间拼成指纹片段；文件不存在时用固定标记（而不是抛异常）。</summary>
    public static string StampFile(string path)
    {
        var info = new FileInfo(path);
        var measured = info.Exists
            ? string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}")
            : "missing";
        return string.Create(CultureInfo.InvariantCulture, $"{path}|{measured}");
    }

    /// <summary>读出与指纹匹配的行投影；没有、损坏、版本不符都返回 <c>null</c>。</summary>
    public IReadOnlyList<ModRowData>? TryLoad(string key)
    {
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            var payload = JsonSerializer.Deserialize<CachePayload>(stream, JsonOptions);
            if (payload is null || payload.SchemaVersion != SchemaVersion
                || !string.Equals(payload.Key, key, StringComparison.Ordinal)
                || payload.Rows is null)
            {
                return null;
            }
            return payload.Rows;
        }
        catch (IOException ex)
        {
            Log.Debug("读取模组快照缓存失败，退回解析。", ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Debug("读取模组快照缓存被拒绝，退回解析。", ex);
            return null;
        }
        catch (JsonException ex)
        {
            Log.Debug("模组快照缓存内容损坏，退回解析。", ex);
            return null;
        }
    }

    /// <summary>写入一份行投影。任何 IO 失败都只记调试日志，不影响调用方。</summary>
    public void Save(string key, IReadOnlyList<ModRowData> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return;

        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(key);
            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, new CachePayload
                {
                    SchemaVersion = SchemaVersion,
                    Key = key,
                    Rows = rows,
                }, JsonOptions);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Debug("写入模组快照缓存失败。", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Debug("写入模组快照缓存被拒绝。", ex);
        }
    }

    private string PathFor(string key)
    {
        // 只取前 16 位当文件名，够区分（SHA-256 前缀）；不足 16 位时原样使用，避免越界。
        var stem = key.Length <= 16 ? key : key[..16];
        return Path.Combine(_directory, "mods-" + stem.ToLowerInvariant() + ".json");
    }

    private sealed class CachePayload
    {
        public int SchemaVersion { get; set; }

        public string? Key { get; set; }

        public IReadOnlyList<ModRowData>? Rows { get; set; }
    }
}
