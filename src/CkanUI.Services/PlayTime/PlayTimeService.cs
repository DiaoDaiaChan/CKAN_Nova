using System.Text.Json;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.PlayTime;

/// <summary>以原子 JSON 文件保存实例累计游戏时长。</summary>
public sealed class PlayTimeService : IPlayTimeService
{
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>默认保存至 %LocalAppData%/CKAN/play-time.json。</summary>
    public PlayTimeService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CKAN", "play-time.json")) { }

    /// <summary>注入路径，供隔离测试使用。</summary>
    public PlayTimeService(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    /// <inheritdoc />
    public IReadOnlyList<PlayTimeEntry> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return Array.Empty<PlayTimeEntry>();
                var entries = JsonSerializer.Deserialize<List<PlayTimeEntry>>(File.ReadAllText(_path));
                return entries ?? new List<PlayTimeEntry>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Array.Empty<PlayTimeEntry>();
            }
        }
    }

    /// <inheritdoc />
    public bool Save(string instanceName, double hours)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        if (!double.IsFinite(hours) || hours < 0) return false;
        lock (_gate)
        {
            var values = Load().ToDictionary(x => x.InstanceName, StringComparer.Ordinal);
            values[instanceName] = new PlayTimeEntry(instanceName, Math.Round(hours, 0, MidpointRounding.AwayFromZero));
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory)) return false;
            var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(temp, JsonSerializer.Serialize(values.Values.OrderBy(x => x.InstanceName, StringComparer.Ordinal)));
                File.Move(temp, _path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch (IOException) { /* 临时文件清理失败不改变原子替换结果。 */ }
                    catch (UnauthorizedAccessException) { /* 临时文件清理失败不改变原子替换结果。 */ }
                }
            }
        }
    }
}
