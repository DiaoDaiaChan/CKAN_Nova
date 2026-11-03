using System.Text.Json;
using log4net;

namespace CkanUI.Services.Plugins;

/// <summary>
/// <see cref="IPluginActivationStore"/> 的 JSON 文件实现（12 篇 F-17）。
/// </summary>
/// <remarks>
/// <para>文件形态：<c>{ "插件Id": { "activated": true, "lastActivatedUtc": "2025-01-01T00:00:00+00:00" } }</c>，
/// 默认落在 <c>%LocalAppData%/CKAN/plugin-activation.json</c>。</para>
/// <para>为什么单独存一个文件而不是塞进设置：插件目录本来就归插件系统管，激活状态和它同生命周期；
/// 混进 UI 设置会让「重装插件」和「恢复界面布局」互相污染。</para>
/// <para>写盘用「临时文件 + <see cref="File.Move(string, string, bool)"/> 覆盖」：进程在写一半时被杀掉，
/// 最多丢掉最后一次状态变化，不会留下半截 JSON 让下次启动整表读空（同 <c>PlayTimeService</c>）。</para>
/// <para>整表只读一次并缓存在内存里：发现阶段会为每个插件问一次，每次都读盘既慢，又会把「文件正被改到一半」
/// 变成随机的扫描结果。</para>
/// </remarks>
public sealed class PluginActivationStore : IPluginActivationStore
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PluginActivationStore));

    private const string FileName = "plugin-activation.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;

    private Dictionary<string, PluginActivationEntry>? _entries;

    /// <summary>用默认路径（<c>%LocalAppData%/CKAN/plugin-activation.json</c>）构造。</summary>
    public PluginActivationStore()
        : this(DefaultPath)
    {
    }

    /// <summary>用指定文件路径构造（测试与便携模式用）。</summary>
    /// <param name="path">JSON 文件路径；父目录不存在时会自动创建。</param>
    public PluginActivationStore(string path)
        : this(path, null)
    {
    }

    /// <summary>用指定文件路径与时钟构造（时钟可替换，便于断言 <c>lastActivatedUtc</c>）。</summary>
    /// <param name="path">JSON 文件路径。</param>
    /// <param name="clock">取当前 UTC 时间的函数；<c>null</c> 用 <see cref="DateTimeOffset.UtcNow"/>。</param>
    public PluginActivationStore(string path, Func<DateTimeOffset>? clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>默认文件路径：<c>%LocalAppData%/CKAN/plugin-activation.json</c>。</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKAN",
        FileName);

    /// <summary>实际使用的文件路径（排障与测试断言用）。</summary>
    public string FilePath => _path;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PluginActivationEntry> Load()
    {
        lock (_gate)
        {
            return new Dictionary<string, PluginActivationEntry>(Entries(), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public bool IsActivated(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return false;
        }

        lock (_gate)
        {
            return Entries().TryGetValue(pluginId.Trim(), out var entry) && entry.Activated;
        }
    }

    /// <inheritdoc />
    public void Save(string pluginId, bool activated)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            Log.Warn("忽略空插件 Id 的激活状态写入。");
            return;
        }

        var id = pluginId.Trim();
        lock (_gate)
        {
            var entries = Entries();
            var lastActivated = activated
                ? _clock()
                : entries.TryGetValue(id, out var existing) ? existing.LastActivatedUtc : null;

            // 先改内存再落盘：即使写盘失败，本次会话的状态查询也必须与新状态一致，
            // 否则「刚激活、马上发现」会读回旧值，出现自己打自己脸的行为。
            entries[id] = new PluginActivationEntry { Activated = activated, LastActivatedUtc = lastActivated };
            WriteEntries(entries);
        }
    }

    private Dictionary<string, PluginActivationEntry> Entries() => _entries ??= ReadEntries();

    private Dictionary<string, PluginActivationEntry> ReadEntries()
    {
        var entries = new Dictionary<string, PluginActivationEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_path))
            {
                return entries;
            }

            var text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return entries;
            }

            var stored = JsonSerializer.Deserialize<Dictionary<string, PluginActivationEntry>>(text, SerializerOptions);
            if (stored is null)
            {
                return entries;
            }

            foreach (var pair in stored)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                {
                    continue;
                }

                entries[pair.Key.Trim()] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            // 文件坏了 / 被别的进程占着 / 路径非法：当空表处理，绝不把异常抛给启动路径。
            Log.Warn("读取插件激活状态失败（按空表处理）：" + _path, ex);
            return new Dictionary<string, PluginActivationEntry>(StringComparer.OrdinalIgnoreCase);
        }

        return entries;
    }

    private void WriteEntries(Dictionary<string, PluginActivationEntry> entries)
    {
        var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 按 Id 排序写：文件进版本库/进用户备份后 diff 可读，人工排障时也稳定。
            var ordered = new SortedDictionary<string, PluginActivationEntry>(entries, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(temp, JsonSerializer.Serialize(ordered, SerializerOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // 写不进去（只读盘、目录被占、路径是文件）：激活/停用本身已经生效，下次启动恢复不了而已。
            Log.Warn("写入插件激活状态失败（激活/停用仍然生效，只是下次启动不会恢复）：" + _path, ex);
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
            Log.Warn("清理插件激活状态临时文件失败：" + path, ex);
        }
    }
}
