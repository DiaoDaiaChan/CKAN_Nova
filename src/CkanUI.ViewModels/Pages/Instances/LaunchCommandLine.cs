using System.Text;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 启动命令文本的规整、拆分与转义（F-05 多命令选择 / F-06 启动命令行编辑）。
/// </summary>
/// <remarks>
/// <para>上游语义（<c>external/CKAN/Core/GameInstance.cs</c> 的 <c>PlayGame</c>）：
/// 命令行先过 <c>Game.AdjustCommandLine(command.Split(' '), Version())</c>，
/// 再把 <c>split[0]</c> 当可执行文件、其余用空格拼成 <c>Arguments</c>，<c>UseShellExecute = true</c>。
/// 本类按同样的口径拆分，但额外支持引号与反斜杠转义，让「路径带空格」的条目也能正确落地
/// （Windows 的 <c>CommandLineToArgvW</c> 规则：反斜杠只在引号前特殊）。</para>
/// <para>纯函数、无 IO，可 headless 单测。</para>
/// </remarks>
public static class LaunchCommandLine
{
    /// <summary>Steam 启动链接前缀（<c>steam://rungameid/220200</c>）。</summary>
    public const string SteamPrefix = "steam://";

    /// <summary>单实例启动参数（与 Core 的 <c>DefaultCommandLines</c> 一致）。</summary>
    public const string SingleInstanceArgument = "-single-instance";

    /// <summary>是否为 Steam 链接命令（Core 的 <c>SteamLibrary.IsSteamCmdLine</c> 语义）。</summary>
    public static bool IsSteamCommand(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.TrimStart().StartsWith(SteamPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>规整单条命令：去掉首尾空白；null 视为空串。</summary>
    public static string Normalize(string? text) => text?.Trim() ?? string.Empty;

    /// <summary>规整命令列表：去空白、丢空串、按序去重（忽略大小写）。</summary>
    public static IReadOnlyList<string> NormalizeList(IEnumerable<string?>? commands)
    {
        var result = new List<string>();
        if (commands is null)
        {
            return result;
        }

        foreach (var raw in commands)
        {
            var text = Normalize(raw);
            if (text.Length == 0)
            {
                continue;
            }

            var duplicate = false;
            foreach (var existing in result)
            {
                if (string.Equals(existing, text, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>按 Core 的口径拆成「可执行文件 + 参数串」；空命令返回两个空串。</summary>
    public static (string Binary, string Arguments) Split(string? command)
    {
        var tokens = Tokenize(command);
        if (tokens.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var rest = new string[tokens.Count - 1];
        for (var i = 1; i < tokens.Count; i++)
        {
            rest[i - 1] = tokens[i];
        }

        return (tokens[0], Join(rest));
    }

    /// <summary>按 Windows 命令行规则切词（支持 <c>"a b"</c> 与 <c>\"</c> 转义）。</summary>
    public static IReadOnlyList<string> Tokenize(string? command)
    {
        var text = command ?? string.Empty;
        var tokens = new List<string>();
        var index = 0;

        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var token = new StringBuilder();
            var inQuotes = false;
            while (index < text.Length)
            {
                var current = text[index];
                if (current == '\\')
                {
                    var start = index;
                    while (index < text.Length && text[index] == '\\')
                    {
                        index++;
                    }

                    var count = index - start;
                    if (index < text.Length && text[index] == '"')
                    {
                        token.Append('\\', count / 2);
                        if (count % 2 == 1)
                        {
                            token.Append('"');
                            index++;
                        }
                    }
                    else
                    {
                        token.Append('\\', count);
                    }

                    continue;
                }

                if (current == '"')
                {
                    inQuotes = !inQuotes;
                    index++;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(current))
                {
                    break;
                }

                token.Append(current);
                index++;
            }

            tokens.Add(token.ToString());
        }

        return tokens;
    }

    /// <summary>把单个参数转义成可拼回命令行的形式（必要时加引号）。</summary>
    public static string Quote(string? token)
    {
        var text = token ?? string.Empty;
        if (text.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || character == '"')
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (current == '\\')
            {
                var start = i;
                while (i < text.Length && text[i] == '\\')
                {
                    i++;
                }

                var count = i - start;
                i--;
                var beforeQuoteOrEnd = i + 1 >= text.Length || text[i + 1] == '"';
                builder.Append('\\', beforeQuoteOrEnd ? count * 2 : count);
                continue;
            }

            if (current == '"')
            {
                builder.Append('\\').Append('"');
                continue;
            }

            builder.Append(current);
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>把参数列表拼回一条命令行（空列表得到空串）。</summary>
    public static string Join(IEnumerable<string?>? tokens)
    {
        if (tokens is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            var quoted = Quote(token);
            if (quoted.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(quoted);
        }

        return builder.ToString();
    }
}
