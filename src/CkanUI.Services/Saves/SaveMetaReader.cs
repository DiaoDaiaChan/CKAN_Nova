using System.Globalization;
using System.Text;
using CkanUI.Sfs;

namespace CkanUI.Services.Saves;

/// <summary>
/// 存档元数据快读（13 篇 §7.1）：优先读同级 <c>.loadmeta</c>（实测 379 字节），
/// 缺失时降级为只读 <c>.sfs</c> 头部 200 行取 <c>GAME</c> 直接子键 —— 绝不加载全文
/// （50 个存档首屏 ≤300ms）。
/// </summary>
/// <remarks>
/// 两条路径都吞掉 <see cref="IOException"/>：列表页读不到元数据只影响展示，不能因此让整页失败。
/// </remarks>
internal static class SaveMetaReader
{
    /// <summary>头部读取行预算（13 篇 §7.1）。</summary>
    private const int HeaderLineBudget = 200;

    /// <summary>同级元数据文件扩展名。</summary>
    private const string LoadMetaExtension = ".loadmeta";

    /// <summary>元数据投影；取不到的字段保持 null（不允许用 0/空串冒充）。</summary>
    internal sealed record Metadata(
        bool HasLoadMeta,
        string? Title,
        string? Mode,
        string? GameVersion,
        string? Scene,
        string? UniversalTime,
        int? VesselCount,
        string? Funds);

    /// <summary>读取一个存档文件的元数据。</summary>
    public static Metadata Read(string savePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);

        var header = ReadHeaderPairs(savePath);
        var loadMeta = ReadLoadMetaPairs(savePath);

        var title = Header(header, "Title");
        var mode = Pick(loadMeta, "gameMode") ?? Header(header, "Mode");
        var version = Pick(loadMeta, "kspVersion") ?? Header(header, "version");
        var scene = Header(header, "scene");
        var universalTime = Pick(loadMeta, "UT") ?? Header(header, "UT");
        var funds = Pick(loadMeta, "funds");

        int? vesselCount = null;
        if (Pick(loadMeta, "vesselCount") is { } vessels &&
            int.TryParse(vessels, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVessels))
        {
            vesselCount = parsedVessels;
        }

        return new Metadata(loadMeta.Count > 0, title, mode, version, scene, universalTime, vesselCount, funds);
    }

    /// <summary>头部 200 行的键值对（键 = 第一次出现的那个）。</summary>
    private static Dictionary<string, string> ReadHeaderPairs(string savePath)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var text = ReadHeaderText(savePath);
            if (text.Length == 0 || SfsParser.Parse(text, savePath).Document is not { } document)
            {
                return pairs;
            }

            Collect(document, pairs);
        }
        catch (IOException)
        {
            // 文件被占用 / 正在写：列表页退化为"只显示文件名 + 体积 + 时间"。
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足同样只退化展示。
        }

        return pairs;
    }

    /// <summary>同级 <c>.loadmeta</c> 的键值对（缺文件时返回空字典）。</summary>
    private static Dictionary<string, string> ReadLoadMetaPairs(string savePath)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        var metaPath = Path.ChangeExtension(savePath, LoadMetaExtension);
        if (!File.Exists(metaPath))
        {
            return pairs;
        }

        try
        {
            var text = File.ReadAllText(metaPath);
            if (SfsParser.Parse(text, metaPath).Document is not { } document)
            {
                return pairs;
            }

            Collect(document, pairs);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return pairs;
    }

    private static string ReadHeaderText(string savePath)
    {
        using var stream = new FileStream(
            savePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var builder = new StringBuilder();
        for (var i = 0; i < HeaderLineBudget; i++)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 收集键值：顶层键值对 + 根节点（<c>GAME</c>）的直接子键（13 篇 §7.1 只要求这两层）。
    /// 同名键取第一次出现的那个；再深的层级不扫，避免把 <c>SCENARIO</c> 内部的同名键捞上来。
    /// </summary>
    private static void Collect(SfsDocument document, Dictionary<string, string> pairs)
    {
        foreach (var element in document.Elements)
        {
            switch (element)
            {
                case SfsPair pair:
                    Add(pair, pairs);
                    break;

                case SfsNode node:
                    foreach (var child in node.Pairs())
                    {
                        Add(child, pairs);
                    }

                    break;

                default:
                    break;
            }
        }
    }

    private static void Add(SfsPair pair, Dictionary<string, string> pairs)
    {
        var key = pair.Key.Trim();
        if (key.Length == 0 || pairs.ContainsKey(key))
        {
            return;
        }

        pairs[key] = pair.Value;
    }

    private static string? Header(Dictionary<string, string> pairs, string key) => Normalize(pairs.GetValueOrDefault(key));

    private static string? Pick(Dictionary<string, string> pairs, string key) => Normalize(pairs.GetValueOrDefault(key));

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
