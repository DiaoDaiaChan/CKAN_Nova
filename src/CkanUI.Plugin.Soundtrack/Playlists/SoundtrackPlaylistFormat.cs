using System.Globalization;
using System.Text;

namespace CkanUI.Plugin.Soundtrack.Playlists;

/// <summary>
/// Playlists/playlists.cfg 的 ConfigNode 文本解析与规范化写出。
/// </summary>
/// <remarks>
/// 这个文件不属于本插件：Soundtrack 4.x 与 SoundtrackEditor 都在读它（SoundtrackEditor 的 Persistor.cs 用
/// KSPUtil.ApplicationRootPath + "Playlists/playlists.cfg"），用户也可能手工编辑或装完模组就被游戏改写。
/// 所以解析必须比“能读样例”更宽：引号、内联大括号、重复键、不认识的键与子节点都要接住——任何一处被丢掉，
/// 玩家保存一次就永久损失配置，而且游戏不会报错，只会“音乐不对”。
/// 写出一律规范化（键顺序、Tab 缩进、布尔 True/False），保证同一份内存模型写两次得到同一份文本，
/// 避免用户每保存一次就产生一个无意义的 diff。
/// </remarks>
public static class SoundtrackPlaylistFormat
{
    /// <summary>顶层节点名；Soundtrack 只识别这个。</summary>
    public const string PlaylistNodeName = "playlist";

    /// <summary>曲目列表子节点名。</summary>
    public const string TracksNodeName = "tracks";

    /// <summary>播放条件子节点名。</summary>
    public const string PlayWhenNodeName = "playWhen";

    private const string TrackKey = "track";
    private const char Indent = '\t';

    /// <summary>
    /// 解析一份 playlists.cfg 文本。
    /// </summary>
    /// <param name="text">文件全文；空文本返回空结果而不是抛异常（实例刚装好时文件还不存在是常态）。</param>
    /// <returns>解析出的播放列表与可读诊断；诊断不阻断解析，只说明哪一段被跳过或退回默认值。</returns>
    public static (List<SoundtrackPlaylist> Playlists, List<string> Diagnostics) Parse(string text)
    {
        var playlists = new List<SoundtrackPlaylist>();
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return (playlists, diagnostics);
        }

        var scanner = new Scanner(text);
        var ordinal = 0;
        while (true)
        {
            scanner.SkipTrivia();
            if (scanner.AtEnd)
            {
                break;
            }

            if (scanner.Current == '}')
            {
                // 顶层多出来的右大括号说明文件被手工改坏了；吃掉它继续读后面的节点，别让一处笔误吞掉整份文件。
                diagnostics.Add(Line(scanner.LineNumber(scanner.Index)) + "有多余的右大括号，已忽略。");
                scanner.Advance();
                continue;
            }

            if (!scanner.TryReadIdentifier(out var name, out var nameStart))
            {
                diagnostics.Add(Line(scanner.LineNumber(scanner.Index)) + "有无法识别的文本，已跳过。");
                scanner.Advance();
                continue;
            }

            if (!scanner.TryConsume('{'))
            {
                diagnostics.Add(Line(scanner.LineNumber(nameStart)) + "节点 " + name + " 后面没有左大括号，已跳过。");
                continue;
            }

            if (!string.Equals(name, PlaylistNodeName, StringComparison.Ordinal))
            {
                // 顶层非 playlist 节点没有地方保存（模型是固定的），只能保留原样之外的选择：记诊断并跳过。
                scanner.CaptureNodeText(nameStart);
                diagnostics.Add(Line(scanner.LineNumber(nameStart)) + "跳过本插件不管理的顶层节点 " + name + "。");
                continue;
            }

            ordinal++;
            var playlist = ParsePlaylistBody(scanner, diagnostics, ordinal);
            if (playlist is not null)
            {
                playlists.Add(playlist);
            }
        }

        return (playlists, diagnostics);
    }

    /// <summary>
    /// 把播放列表写成规范化的 playlists.cfg 文本。
    /// </summary>
    /// <param name="playlists">要写出的播放列表，按给定顺序。</param>
    /// <param name="newLine">换行符；沿用原文件的风格（Load 会探测 CRLF/LF）。</param>
    /// <returns>UTF-8 可直接落盘的文本，末尾恰好一个换行，不含 BOM（BOM 由写盘方控制）。</returns>
    public static string Format(IEnumerable<SoundtrackPlaylist> playlists, string newLine)
    {
        ArgumentNullException.ThrowIfNull(playlists);
        var lineBreak = string.IsNullOrEmpty(newLine) ? "\r\n" : newLine;
        var builder = new StringBuilder(4096);
        foreach (var playlist in playlists)
        {
            ArgumentNullException.ThrowIfNull(playlist);
            AppendPlaylist(builder, playlist, lineBreak);
        }

        return builder.ToString();
    }

    private static void AppendPlaylist(StringBuilder builder, SoundtrackPlaylist playlist, string newLine)
    {
        builder.Append(PlaylistNodeName).Append(newLine);
        builder.Append('{').Append(newLine);

        AppendKey(builder, 1, "name", playlist.Name, newLine);
        if (playlist.Enabled is { } enabled)
        {
            AppendKey(builder, 1, "enabled", BoolText(enabled), newLine);
        }

        AppendKey(builder, 1, "loop", BoolText(playlist.Loop), newLine);
        AppendKey(builder, 1, "shuffle", BoolText(playlist.Shuffle), newLine);
        AppendKey(builder, 1, "pauseOnGamePause", BoolText(playlist.PauseOnGamePause), newLine);
        AppendKey(builder, 1, "disableAfterPlay", BoolText(playlist.DisableAfterPlay), newLine);
        AppendKey(builder, 1, "playNext", playlist.PlayNext, newLine);
        AppendKey(builder, 1, "playBefore", playlist.PlayBefore, newLine);
        AppendKey(builder, 1, "playAfter", playlist.PlayAfter, newLine);
        if (playlist.Channel is { } channel)
        {
            AppendKey(builder, 1, "channel", channel.ToString(CultureInfo.InvariantCulture), newLine);
        }

        if (playlist.PreloadTime is { } preloadTime)
        {
            AppendKey(builder, 1, "preloadTime", preloadTime.ToString(CultureInfo.InvariantCulture), newLine);
        }

        if (playlist.Tracks.Count > 0)
        {
            builder.Append(Indent).Append(TracksNodeName).Append(newLine);
            builder.Append(Indent).Append('{').Append(newLine);
            foreach (var track in playlist.Tracks)
            {
                AppendKey(builder, 2, TrackKey, track.Name, newLine);
            }

            builder.Append(Indent).Append('}').Append(newLine);
        }

        if (playlist.PlayWhen.Count > 0)
        {
            builder.Append(Indent).Append(PlayWhenNodeName).Append(newLine);
            builder.Append(Indent).Append('{').Append(newLine);
            foreach (var pair in playlist.PlayWhen)
            {
                AppendKey(builder, 2, pair.Key, pair.Value, newLine);
            }

            builder.Append(Indent).Append('}').Append(newLine);
        }

        foreach (var pair in playlist.ExtraValues)
        {
            AppendKey(builder, 1, pair.Key, pair.Value, newLine);
        }

        foreach (var node in playlist.UnknownNodes)
        {
            AppendRawBlock(builder, node, newLine);
        }

        builder.Append('}').Append(newLine);
    }

    private static void AppendKey(StringBuilder builder, int depth, string key, string value, string newLine)
    {
        builder.Append(Indent, depth).Append(key).Append(" =");
        var safe = Sanitize(value);
        if (safe.Length > 0)
        {
            builder.Append(' ').Append(safe);
        }

        builder.Append(newLine);
    }

    private static void AppendRawBlock(StringBuilder builder, string raw, string newLine)
    {
        foreach (var line in raw.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            // 未知节点只保留相对缩进（解析时按公共缩进剪掉了），写出时重新挂到当前层级，
            // 这样保存一次不会把玩家的自定义节点越缩越深。
            builder.Append(Indent).Append(line).Append(newLine);
        }
    }

    private static string BoolText(bool value) => value ? "True" : "False";

    /// <summary>值里混进换行会把一行拆成两行、把文件写坏；宁可换成空格也不能让下一行变成新键。</summary>
    private static string Sanitize(string? value) =>
        value is null ? string.Empty : value.Replace('\r', ' ').Replace('\n', ' ');

    private static SoundtrackPlaylist? ParsePlaylistBody(Scanner scanner, List<string> diagnostics, int ordinal)
    {
        var playlist = new SoundtrackPlaylist();
        var openedAt = scanner.LineNumber(scanner.Index);
        while (true)
        {
            scanner.SkipTrivia();
            if (scanner.AtEnd)
            {
                diagnostics.Add(Line(openedAt) + "playlist 节点没有闭合的大括号，已读到文件末尾。");
                break;
            }

            if (scanner.Current == '}')
            {
                scanner.Advance();
                break;
            }

            if (!scanner.TryReadIdentifier(out var key, out var keyStart))
            {
                diagnostics.Add(Line(scanner.LineNumber(scanner.Index)) + "有无法识别的文本，已跳过。");
                scanner.Advance();
                continue;
            }

            var keyLine = scanner.LineNumber(keyStart);
            if (scanner.TryConsume('{'))
            {
                ParseChildNode(playlist, key, keyStart, scanner, diagnostics, keyLine);
                continue;
            }

            if (!scanner.TryConsume('='))
            {
                diagnostics.Add(Line(keyLine) + key + " 后面既不是等号也不是左大括号，已跳过。");
                continue;
            }

            ApplyScalar(playlist, key, scanner.ReadValue(), diagnostics, keyLine);
        }

        if (string.IsNullOrWhiteSpace(playlist.Name))
        {
            // 没有名字就无法被 playNext/playAfter 引用，写回去只会让 Soundtrack 读到空名列表。
            diagnostics.Add(Line(openedAt) + "跳过第 " + ordinal.ToString(CultureInfo.InvariantCulture) + " 个没有 name 的 playlist 节点。");
            return null;
        }

        return playlist;
    }

    private static void ParseChildNode(
        SoundtrackPlaylist playlist,
        string name,
        int nameStart,
        Scanner scanner,
        List<string> diagnostics,
        int line)
    {
        if (string.Equals(name, TracksNodeName, StringComparison.Ordinal))
        {
            ParseTracksNode(playlist, scanner, diagnostics, line);
            return;
        }

        if (string.Equals(name, PlayWhenNodeName, StringComparison.Ordinal))
        {
            ParsePlayWhenNode(playlist, scanner, diagnostics, line);
            return;
        }

        var raw = scanner.CaptureNodeText(nameStart);
        if (raw.Length == 0)
        {
            return;
        }

        playlist.UnknownNodes.Add(raw);
    }

    private static void ParseTracksNode(SoundtrackPlaylist playlist, Scanner scanner, List<string> diagnostics, int line)
    {
        while (true)
        {
            scanner.SkipTrivia();
            if (scanner.AtEnd)
            {
                diagnostics.Add(Line(line) + "tracks 节点没有闭合的大括号，已读到文件末尾。");
                return;
            }

            if (scanner.Current == '}')
            {
                scanner.Advance();
                return;
            }

            if (!scanner.TryReadIdentifier(out var key, out var keyStart))
            {
                diagnostics.Add(Line(scanner.LineNumber(scanner.Index)) + "tracks 节点里有无法识别的文本，已跳过。");
                scanner.Advance();
                continue;
            }

            if (scanner.TryConsume('{'))
            {
                scanner.CaptureNodeText(keyStart);
                diagnostics.Add(Line(scanner.LineNumber(keyStart)) + "tracks 节点不支持子节点 " + key + "，已跳过。");
                continue;
            }

            if (!scanner.TryConsume('='))
            {
                diagnostics.Add(Line(scanner.LineNumber(keyStart)) + key + " 后面既不是等号也不是左大括号，已跳过。");
                continue;
            }

            var value = scanner.ReadValue();
            if (!string.Equals(key, TrackKey, StringComparison.Ordinal))
            {
                diagnostics.Add(Line(scanner.LineNumber(keyStart)) + "tracks 节点里不认识的键 " + key + "，已忽略。");
                continue;
            }

            if (value.Length == 0)
            {
                diagnostics.Add(Line(scanner.LineNumber(keyStart)) + "空的 track 条目已跳过。");
                continue;
            }

            playlist.Tracks.Add(new SoundtrackTrackRef { Name = value });
        }
    }

    private static void ParsePlayWhenNode(SoundtrackPlaylist playlist, Scanner scanner, List<string> diagnostics, int line)
    {
        while (true)
        {
            scanner.SkipTrivia();
            if (scanner.AtEnd)
            {
                diagnostics.Add(Line(line) + "playWhen 节点没有闭合的大括号，已读到文件末尾。");
                return;
            }

            if (scanner.Current == '}')
            {
                scanner.Advance();
                return;
            }

            if (!scanner.TryReadIdentifier(out var key, out var keyStart))
            {
                diagnostics.Add(Line(scanner.LineNumber(scanner.Index)) + "playWhen 节点里有无法识别的文本，已跳过。");
                scanner.Advance();
                continue;
            }

            if (scanner.TryConsume('{'))
            {
                scanner.CaptureNodeText(keyStart);
                diagnostics.Add(Line(scanner.LineNumber(keyStart)) + "playWhen 节点不支持子节点 " + key + "，已跳过。");
                continue;
            }

            if (!scanner.TryConsume('='))
            {
                diagnostics.Add(Line(scanner.LineNumber(keyStart)) + key + " 后面既不是等号也不是左大括号，已跳过。");
                continue;
            }

            Upsert(playlist.PlayWhen, key, scanner.ReadValue());
        }
    }

    private static void ApplyScalar(
        SoundtrackPlaylist playlist,
        string key,
        string value,
        List<string> diagnostics,
        int line)
    {
        switch (key)
        {
            case "name":
                playlist.Name = value;
                return;
            case "enabled":
                if (TryReadBool(value, out var enabled))
                {
                    playlist.Enabled = enabled;
                }
                else
                {
                    diagnostics.Add(BadBool(key, value, line));
                }

                return;
            case "loop":
                if (TryReadBool(value, out var loop))
                {
                    playlist.Loop = loop;
                }
                else
                {
                    diagnostics.Add(BadBool(key, value, line));
                }

                return;
            case "shuffle":
                if (TryReadBool(value, out var shuffle))
                {
                    playlist.Shuffle = shuffle;
                }
                else
                {
                    diagnostics.Add(BadBool(key, value, line));
                }

                return;
            case "pauseOnGamePause":
                if (TryReadBool(value, out var pauseOnGamePause))
                {
                    playlist.PauseOnGamePause = pauseOnGamePause;
                }
                else
                {
                    diagnostics.Add(BadBool(key, value, line));
                }

                return;
            case "disableAfterPlay":
                if (TryReadBool(value, out var disableAfterPlay))
                {
                    playlist.DisableAfterPlay = disableAfterPlay;
                }
                else
                {
                    diagnostics.Add(BadBool(key, value, line));
                }

                return;
            case "playNext":
                playlist.PlayNext = value;
                return;
            case "playBefore":
                playlist.PlayBefore = value;
                return;
            case "playAfter":
                playlist.PlayAfter = value;
                return;
            case "channel":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel))
                {
                    playlist.Channel = channel;
                }
                else
                {
                    diagnostics.Add(Line(line) + "channel 的值“" + value + "”不是整数，已忽略。");
                }

                return;
            case "preloadTime":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var preloadTime))
                {
                    playlist.PreloadTime = preloadTime;
                }
                else
                {
                    diagnostics.Add(Line(line) + "preloadTime 的值“" + value + "”不是数字，已忽略。");
                }

                return;
            default:
                // 不认识的标量键留在 ExtraValues，保存时按原顺序写回：模组作者可能加了自己的键，
                // 本插件看不清语义，但丢掉它就等于替用户做了一次破坏性编辑。
                Upsert(playlist.ExtraValues, key, value);
                return;
        }
    }

    private static bool TryReadBool(string value, out bool parsed) => bool.TryParse(value, out parsed);

    private static string BadBool(string key, string value, int line) =>
        Line(line) + key + " 的值“" + value + "”不是 True/False，保留默认值。";

    private static void Upsert(List<KeyValuePair<string, string>> pairs, string key, string value)
    {
        for (var index = 0; index < pairs.Count; index++)
        {
            if (string.Equals(pairs[index].Key, key, StringComparison.Ordinal))
            {
                // 重复键后者覆盖：保留第一次出现的位置，值换成最后一次，行的相对顺序才不会跳。
                pairs[index] = new KeyValuePair<string, string>(key, value);
                return;
            }
        }

        pairs.Add(new KeyValuePair<string, string>(key, value));
    }

    private static string Line(int number) => "第 " + number.ToString(CultureInfo.InvariantCulture) + " 行：";

    /// <summary>
    /// ConfigNode 文本的字符级扫描器：解析状态全在这里，避免用正则去猜嵌套结构。
    /// </summary>
    private sealed class Scanner
    {
        private readonly string _text;
        private int _index;

        public Scanner(string text) => _text = text;

        public bool AtEnd => _index >= _text.Length;

        public char Current => _text[_index];

        public int Index => _index;

        public void Advance() => _index++;

        public void SkipTrivia()
        {
            while (!AtEnd)
            {
                if (char.IsWhiteSpace(Current))
                {
                    _index++;
                    continue;
                }

                if (IsCommentStart())
                {
                    while (!AtEnd && Current != '\n')
                    {
                        _index++;
                    }

                    continue;
                }

                break;
            }
        }

        public bool TryReadIdentifier(out string name, out int start)
        {
            SkipTrivia();
            start = _index;
            if (AtEnd || Current is '{' or '}' or '=')
            {
                name = string.Empty;
                return false;
            }

            var begin = _index;
            while (!AtEnd && !char.IsWhiteSpace(Current) && Current is not ('{' or '}' or '='))
            {
                _index++;
            }

            name = _text[begin.._index];
            return name.Length > 0;
        }

        public bool TryConsume(char expected)
        {
            SkipTrivia();
            if (!AtEnd && Current == expected)
            {
                _index++;
                return true;
            }

            return false;
        }

        public string ReadValue()
        {
            while (!AtEnd && (Current == ' ' || Current == Indent))
            {
                _index++;
            }

            if (!AtEnd && Current == '"')
            {
                // 引号包起来的值里可以有空格、大括号，甚至换行；按引号收尾，别被内联大括号截断。
                var open = _index;
                _index++;
                while (!AtEnd)
                {
                    if (Current == '\\' && _index + 1 < _text.Length)
                    {
                        _index += 2;
                        continue;
                    }

                    if (Current == '"')
                    {
                        _index++;
                        break;
                    }

                    _index++;
                }

                return Unquote(_text[open.._index]);
            }

            var start = _index;
            while (!AtEnd && Current is not ('\n' or '\r' or '{' or '}'))
            {
                _index++;
            }

            return _text[start.._index].TrimEnd();
        }

        /// <summary>从节点名开始，一直吃到与之配对的大括号（含两端），返回去掉公共缩进后的原文。</summary>
        public string CaptureNodeText(int nameStart)
        {
            var depth = 1;
            while (!AtEnd && depth > 0)
            {
                if (Current == '{')
                {
                    depth++;
                }
                else if (Current == '}')
                {
                    depth--;
                }

                _index++;
            }

            return NormalizeBlock(_text[nameStart.._index]);
        }

        public int LineNumber(int position)
        {
            var line = 1;
            var limit = Math.Min(position, _text.Length);
            for (var index = 0; index < limit; index++)
            {
                if (_text[index] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private bool IsCommentStart()
        {
            if (Current != '/' || _index + 1 >= _text.Length || _text[_index + 1] != '/')
            {
                return false;
            }

            // 只把“整行注释”当注释：值里出现 // （路径、名字）时不能把后半行吃掉。
            var previous = _index - 1;
            while (previous >= 0 && _text[previous] is ' ' or '\t' or '\r')
            {
                previous--;
            }

            return previous < 0 || _text[previous] == '\n';
        }

        private static string NormalizeBlock(string raw)
        {
            var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var indent = int.MaxValue;
            for (var index = 1; index < lines.Length; index++)
            {
                if (lines[index].Trim().Length == 0)
                {
                    continue;
                }

                var spaces = 0;
                while (spaces < lines[index].Length && lines[index][spaces] is ' ' or '\t')
                {
                    spaces++;
                }

                indent = Math.Min(indent, spaces);
            }

            if (indent == int.MaxValue)
            {
                indent = 0;
            }

            var builder = new StringBuilder(raw.Length);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = index == 0
                    ? lines[index].Trim()
                    : (lines[index].Length >= indent ? lines[index][indent..] : lines[index].Trim());
                builder.Append(line.TrimEnd());
                if (index < lines.Length - 1)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString().TrimEnd('\n');
        }

        private static string Unquote(string raw)
        {
            if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            {
                return raw[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            }

            return raw;
        }
    }
}
