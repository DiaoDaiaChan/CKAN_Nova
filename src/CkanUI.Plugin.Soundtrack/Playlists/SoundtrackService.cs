using System.Globalization;
using System.Text;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Soundtrack.Playlists;

/// <summary>
/// Soundtrack 播放列表与乐曲库的读写服务（<see cref="ISoundtrackService"/> 的默认实现）。
/// </summary>
/// <remarks>
/// 这是唯一碰玩家游戏目录的地方，因此两条纪律贯穿全文件：
/// 一、读盘不抛异常——游戏正开着、模组目录被删、音频文件权限不足都是常态，问题进 Diagnostics，
/// 界面照样能显示已经读到的部分，而不是弹一个红框什么都看不到。
/// 二、写盘先备份再原子替换——playlists.cfg 是玩家手改过的配置，写坏一次就找不回来；
/// 临时文件同目录落盘 + File.Move(overwrite) 保证不会留下写了一半的文件。
/// </remarks>
public sealed class SoundtrackService : ISoundtrackService
{
    /// <summary>播放列表目录名（相对实例根目录）。</summary>
    public const string PlaylistsDirectoryName = "Playlists";

    /// <summary>播放列表文件名。</summary>
    public const string PlaylistsFileName = "playlists.cfg";

    private const string GameDataDirectoryName = "GameData";
    private const string MusicDirectoryName = "Music";
    private const string PartsDirectoryName = "Parts";
    private const int MaxMusicFiles = 20000;
    private const int MaxMusicDepth = 10;
    private const int MaxBackupSuffix = 1000;

    private static readonly string[] MusicExtensions = { ".ogg", ".wav", ".mp3" };

    // 探测顺序按“玩家最可能装的”排：原版 Soundtrack → SoundtrackEditor → 社区 fork。
    private static readonly string[] ModDirectoryNames = { "Soundtrack", "SoundtrackEditor", "SoundtrackEditorForked" };

    // 游戏进程会以 ReadWrite 打开 playlists.cfg（Soundtrack 运行中会回写），读取时必须让出共享位，
    // 否则用户在游戏旁边点开插件只会看到“文件被占用”。
    private static readonly FileShare ReadShare = FileShare.ReadWrite | FileShare.Delete;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <inheritdoc />
    public SoundtrackDocument Load(string gameDir)
    {
        var document = new SoundtrackDocument();
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            document.Diagnostics.Add("没有选择实例，无法定位播放列表。");
            return document;
        }

        document.GameDir = gameDir;
        try
        {
            document.FilePath = Path.Combine(gameDir, PlaylistsDirectoryName, PlaylistsFileName);
            document.DirectoryExists = Directory.Exists(Path.Combine(gameDir, PlaylistsDirectoryName));
            document.FileExists = File.Exists(document.FilePath);
            document.ModDirectory = ProbeModDirectory(gameDir);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            // 路径本身非法（盘符消失、网络路径断开）不该让整个页面空白，后面的乐曲扫描还能给出一部分信息。
            document.Diagnostics.Add("定位播放列表失败：" + ex.Message);
        }

        if (document.FileExists)
        {
            LoadPlaylists(document);
        }

        ScanMusicLibrary(gameDir, document);
        ResolveTracks(document);
        return document;
    }

    /// <inheritdoc />
    public OperationResult<SoundtrackSaveOutcome> Save(SoundtrackDocument document)
    {
        if (document is null)
        {
            // 契约要求保存路径不抛：界面传进来空文档时也要给出可读原因，而不是让异常冒到 UI 线程。
            return OperationResult<SoundtrackSaveOutcome>.Fail("没有可保存的播放列表文档。");
        }

        if (string.IsNullOrWhiteSpace(document.GameDir))
        {
            return OperationResult<SoundtrackSaveOutcome>.Fail("没有选择实例，无法保存播放列表。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var trackCount = 0;
        for (var index = 0; index < document.Playlists.Count; index++)
        {
            var playlist = document.Playlists[index];
            var name = playlist.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                return OperationResult<SoundtrackSaveOutcome>.Fail(
                    $"第 {(index + 1).ToString(CultureInfo.InvariantCulture)} 个播放列表没有名字。没有名字的列表无法被 playNext/playAfter 引用，保存后游戏里会指向空的列表。");
            }

            if (!names.Add(name))
            {
                return OperationResult<SoundtrackSaveOutcome>.Fail(
                    $"播放列表名字“{name}”重复。Soundtrack 只按名字区分列表，重名会让引用指向不确定的那一个。");
            }

            trackCount += playlist.Tracks.Count;
        }

        string fullTarget;
        try
        {
            var target = string.IsNullOrWhiteSpace(document.FilePath)
                ? Path.Combine(document.GameDir, PlaylistsDirectoryName, PlaylistsFileName)
                : document.FilePath;

            // 界面上手填的路径可能是相对的；相对路径必须以实例根为基准，否则会按进程当前目录解析，
            // 悄悄写到宿主自己的工作目录里去。
            if (!Path.IsPathRooted(target))
            {
                target = Path.Combine(document.GameDir, target);
            }

            fullTarget = Path.GetFullPath(target);
            if (!IsUnderDirectory(fullTarget, document.GameDir))
            {
                return OperationResult<SoundtrackSaveOutcome>.Fail(
                    "目标文件不在实例目录内，已拒绝保存。",
                    "解析后的路径：" + fullTarget);
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return OperationResult<SoundtrackSaveOutcome>.Fail("播放列表保存路径无效：" + ex.Message);
        }

        var tempPath = fullTarget + ".tmp-" + Guid.NewGuid().ToString("N");
        string? backupPath = null;
        try
        {
            var directory = Path.GetDirectoryName(fullTarget);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(fullTarget))
            {
                backupPath = NextBackupPath(fullTarget);
                File.Copy(fullTarget, backupPath, overwrite: false);
            }

            var text = SoundtrackPlaylistFormat.Format(document.Playlists, document.NewLine);
            File.WriteAllText(tempPath, text, Utf8NoBom);
            File.Move(tempPath, fullTarget, overwrite: true);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return OperationResult<SoundtrackSaveOutcome>.Fail(
                "保存播放列表失败：" + ex.Message,
                ex.ToString());
        }
        finally
        {
            TryDelete(tempPath);
        }

        return OperationResult<SoundtrackSaveOutcome>.Ok(
            new SoundtrackSaveOutcome(fullTarget, backupPath, document.Playlists.Count, trackCount));
    }

    /// <inheritdoc />
    public void ResolveTracks(SoundtrackDocument document)
    {
        if (document is null)
        {
            return;
        }

        try
        {
            // 精确匹配优先、忽略大小写兜底：Windows 上文件名大小写不敏感，但 playlists.cfg 里的写法
            // 常常和磁盘上的实际文件名对不上（玩家手改、跨平台拷贝），只做 Ordinal 会误报“文件丢失”。
            var exact = new Dictionary<string, SoundtrackMusicFile>(StringComparer.Ordinal);
            var relaxed = new Dictionary<string, SoundtrackMusicFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in document.MusicLibrary)
            {
                if (file is null || string.IsNullOrEmpty(file.Name))
                {
                    continue;
                }

                exact.TryAdd(file.Name, file);
                relaxed.TryAdd(file.Name, file);
            }

            foreach (var playlist in document.Playlists)
            {
                foreach (var track in playlist.Tracks)
                {
                    SoundtrackMusicFile? match = null;
                    if (!string.IsNullOrEmpty(track.Name))
                    {
                        if (!exact.TryGetValue(track.Name, out match) && !relaxed.TryGetValue(track.Name, out match))
                        {
                            match = null;
                        }
                    }

                    // 每次解析都从 null 开始：换实例后旧的路径必须失效，否则界面会把 A 实例的文件显示成 B 实例的。
                    track.FilePath = match?.FullPath;
                    track.SizeBytes = match?.SizeBytes;
                }
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            document.Diagnostics.Add("匹配曲目文件失败：" + ex.Message);
        }
    }

    private static void LoadPlaylists(SoundtrackDocument document)
    {
        try
        {
            var text = ReadShared(document.FilePath);
            document.NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var parsed = SoundtrackPlaylistFormat.Parse(text);
            document.Playlists.AddRange(parsed.Playlists);
            document.Diagnostics.AddRange(parsed.Diagnostics);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            document.Diagnostics.Add("读取 playlists.cfg 失败：" + ex.Message);
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, ReadShare);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string? ProbeModDirectory(string gameDir)
    {
        foreach (var name in ModDirectoryNames)
        {
            var candidate = Path.Combine(gameDir, GameDataDirectoryName, name);
            try
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                // Directory.Exists 正常不抛；网络路径异常时继续探下一个，别因为一个候选目录坏掉就判定“没装”。
            }
        }

        return null;
    }

    private static void ScanMusicLibrary(string gameDir, SoundtrackDocument document)
    {
        var files = new List<SoundtrackMusicFile>();
        var seen = new HashSet<string>(PathComparer);
        var truncated = false;

        void ScanRoot(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            var pending = new Stack<(string Directory, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0)
            {
                var (directory, depth) = pending.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(directory);
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    document.Diagnostics.Add("跳过无法读取的目录 " + directory + "：" + ex.Message);
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (Directory.Exists(entry))
                    {
                        // 深度限 10 层：KSP 的 GameData 里存在 mod 自带的深层测试资源，
                        // 无上限递归会为了几个音效文件把整个实例走一遍。
                        if (depth >= MaxMusicDepth || IsPartsDirectory(entry, gameDir))
                        {
                            continue;
                        }

                        pending.Push((entry, depth + 1));
                        continue;
                    }

                    if (!IsMusicFile(entry) || !seen.Add(entry))
                    {
                        continue;
                    }

                    if (files.Count >= MaxMusicFiles)
                    {
                        truncated = true;
                        return;
                    }

                    long size;
                    try
                    {
                        size = new FileInfo(entry).Length;
                    }
                    catch (Exception ex) when (IsRecoverable(ex))
                    {
                        document.Diagnostics.Add("跳过无法读取的音频文件 " + entry + "：" + ex.Message);
                        continue;
                    }

                    files.Add(new SoundtrackMusicFile(
                        Path.GetFileNameWithoutExtension(entry),
                        entry,
                        size,
                        Path.GetExtension(entry).ToLowerInvariant()));
                }
            }
        }

        ScanRoot(Path.Combine(gameDir, MusicDirectoryName));
        ScanRoot(Path.Combine(gameDir, GameDataDirectoryName));

        if (truncated)
        {
            document.Diagnostics.Add(
                $"乐曲库超过 {MaxMusicFiles.ToString(CultureInfo.InvariantCulture)} 个文件，只收录了前 {files.Count.ToString(CultureInfo.InvariantCulture)} 个；超出的曲目会显示为“文件未找到”。");
        }

        files.Sort(static (left, right) =>
        {
            var byName = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return byName != 0 ? byName : StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
        });

        document.MusicLibrary.AddRange(files);
    }

    private static bool IsMusicFile(string path)
    {
        var extension = Path.GetExtension(path);
        foreach (var candidate in MusicExtensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断目录是否位于实例的 GameData/Parts 之下。
    /// </summary>
    /// <remarks>
    /// 用“相对路径里有没有名为 Parts 的一段”而不是整串子串匹配：实例目录本身可能就叫 Parts
    /// （例如 D:\Games\Parts\KSP），子串匹配会把整个实例的 GameData 都当成零件目录跳过。
    /// </remarks>
    private static bool IsPartsDirectory(string directory, string gameDir)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(gameDir, directory);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return false;
        }

        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(segment, PartsDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(fullDirectory, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static string NextBackupPath(string filePath)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = filePath + ".bak-" + stamp;
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        // 同一秒内连续保存两次是可能的（用户连点、自动保存），备份不能互相覆盖，否则“上一次”就没了。
        for (var suffix = 1; suffix <= MaxBackupSuffix; suffix++)
        {
            var numbered = candidate + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            if (!File.Exists(numbered))
            {
                return numbered;
            }
        }

        throw new IOException("同一秒内的备份文件超过 " + MaxBackupSuffix.ToString(CultureInfo.InvariantCulture) + " 份，已停止保存以免覆盖旧备份。");
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
        catch (Exception ex) when (IsRecoverable(ex))
        {
            // 临时文件删不掉只会在游戏目录里多一个 .tmp-xxx；为了它把已经成功的保存报成失败更糟。
        }
    }

    /// <summary>
    /// 只放行“业务上可能发生”的异常；OutOfMemory 这类进程级故障继续上抛，别伪装成读盘失败。
    /// </summary>
    private static bool IsRecoverable(Exception exception) => exception is not OutOfMemoryException;
}
