using System.Text;
using log4net;

namespace CkanUI.Services.History;

/// <summary>
/// 安装历史的落盘原语（12 篇 F-44 / 13 篇 UI-23）。
/// </summary>
/// <remarks>
/// <para>
/// 文件：<c>&lt;GameDir&gt;/CKAN/install_history.jsonl</c>（由 <see cref="InstallHistoryRecorder"/> 拼路径）。
/// 选每实例一份而不是 App 数据目录，是因为"已装集合"天然属于某个实例：跟着实例走，
/// 切换实例时历史自动切换，也不会出现"实例被删了历史还在"的孤儿；代价是删实例会连历史一起删（可接受）。
/// 上游老 GUI 用 <c>&lt;GameDir&gt;/CKAN/history/*.ckan</c>（每行一个注册表快照，带完整元数据），
/// 我们用 JSONL 是为了能原子追加、能容错跳行、也便于人读——两者互不干扰。
/// </para>
/// <para>
/// 写入是"读全量 → 追加 → 超限截断 → 写 .tmp → 覆盖移动"：
/// 崩溃最多丢掉最后一次追加，不会留下半行；截断时保留最新 <see cref="MaxLines"/> 行。
/// 读取时坏行跳过（见 <see cref="InstallHistoryLine.TryParse"/>），一个字节都不改。
/// </para>
/// </remarks>
public sealed class InstallHistoryStore
{
    /// <summary>落盘文件名。</summary>
    public const string FileName = "install_history.jsonl";

    /// <summary>默认容量上限（行）。一次操作两行，2000 行约 1000 次操作。</summary>
    public const int DefaultMaxLines = 2000;

    /// <summary>容量上限的下限：别把上限配成 0 行导致写一次就清空。</summary>
    public const int MinMaxLines = 8;

    private static readonly ILog Log = LogManager.GetLogger(typeof(InstallHistoryStore));
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();

    public InstallHistoryStore(string filePath, int maxLines = DefaultMaxLines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        MaxLines = Math.Max(MinMaxLines, maxLines);
    }

    /// <summary>历史文件的绝对路径。</summary>
    public string FilePath { get; }

    /// <summary>生效的容量上限（行）。</summary>
    public int MaxLines { get; }

    /// <summary>文件是否已存在。</summary>
    public bool Exists => File.Exists(FilePath);

    /// <summary>读全部可解析的行（保持文件顺序）。文件不存在返回空。</summary>
    public IReadOnlyList<InstallHistoryLine> Read()
    {
        lock (_gate)
        {
            return ReadRawLines().Select(TryParseOrNull).OfType<InstallHistoryLine>().ToList();
        }
    }

    /// <summary>
    /// 原子追加若干行，并按容量上限滚动截断（保留最新的行）。
    /// </summary>
    /// <returns>写入后的总行数。</returns>
    /// <exception cref="IOException">写盘失败（磁盘满、被占用）。调用方负责转成可重试的失败结果。</exception>
    public int Append(IReadOnlyList<InstallHistoryLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return CountRawLines();
        }

        lock (_gate)
        {
            var raw = ReadRawLines();
            foreach (var line in lines)
            {
                raw.Add(line.ToJson());
            }

            // 滚动截断：只留最新的 MaxLines 行。
            if (raw.Count > MaxLines)
            {
                raw.RemoveRange(0, raw.Count - MaxLines);
            }

            WriteAtomic(raw);
            return raw.Count;
        }
    }

    /// <summary>
    /// 删除时间戳为 <paramref name="timestamp"/> 的那一次操作（条目行 + 快照行），返回删掉的行数。
    /// </summary>
    /// <remarks>
    /// 无法解析的行原样保留：它们可能是更高版本写的、或人手工改过的，
    /// 静默丢弃等于替用户删掉看不懂的数据。
    /// </remarks>
    /// <exception cref="IOException">写盘失败（磁盘满、被占用）。调用方负责转成可重试的失败结果。</exception>
    public int DeleteAt(DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            var raw = ReadRawLines();
            var kept = new List<string>(raw.Count);
            var removed = 0;
            foreach (var line in raw)
            {
                if (InstallHistoryLine.TryParse(line, out var parsed) && parsed is not null && parsed.At == timestamp)
                {
                    removed++;
                    continue;
                }

                kept.Add(line);
            }

            if (removed == 0)
            {
                return 0;
            }

            WriteAtomic(kept);
            return removed;
        }
    }

    /// <summary>
    /// 清空历史，返回清掉的行数。
    /// </summary>
    /// <remarks>
    /// 写空文件而不是删文件：目录权限更稳（删文件在某些同步盘/杀软下会失败），
    /// 也保留「文件在 = 历史已初始化」的语义。
    /// </remarks>
    /// <exception cref="IOException">写盘失败（磁盘满、被占用）。调用方负责转成可重试的失败结果。</exception>
    public int Clear()
    {
        lock (_gate)
        {
            var raw = ReadRawLines();
            if (raw.Count == 0)
            {
                return 0;
            }

            WriteAtomic([]);
            return raw.Count;
        }
    }

    private int CountRawLines()
    {
        lock (_gate)
        {
            return ReadRawLines().Count;
        }
    }

    private List<string> ReadRawLines()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 读不动就当没有：历史是只读附属品，不该阻断页面。
            Log.Warn($"读取安装历史失败：{FilePath}", ex);
            return [];
        }
    }

    private static InstallHistoryLine? TryParseOrNull(string json)
    {
        if (InstallHistoryLine.TryParse(json, out var line))
        {
            return line;
        }

        Log.Debug($"跳过无法解析的安装历史行：{json}");
        return null;
    }

    private void WriteAtomic(IReadOnlyList<string> lines)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = FilePath + ".tmp";
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }

        try
        {
            File.WriteAllText(temp, builder.ToString(), Utf8NoBom);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // 临时文件删不掉不影响正确性（下次会被覆盖）。
                }
            }
        }
    }
}
