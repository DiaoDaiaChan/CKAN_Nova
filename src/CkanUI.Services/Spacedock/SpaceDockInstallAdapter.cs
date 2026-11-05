using System.IO;

namespace CkanUI.Services.Spacedock;

/// <summary>
/// SpaceDock ZIP 安装布局适配器。
/// </summary>
/// <remarks>
/// <para>SpaceDock 只提供压缩包，压缩包内部的目录约定并不统一：有的直接以 <c>GameData</c> 开头，
/// 有的把 <c>GameData</c> 包在一层发布目录里，有的只给一个模组文件夹，还有 Ships / Missions
/// 这类必须落到 KSP 根目录的内容。这里先把条目归类成「目标根目录 + 相对路径」，
/// 安装器只负责按计划解压，不再用“猜第一个 GameData 下标”的方式处理所有包。</para>
/// <para>适配器只做结构判断，不碰磁盘；非法路径仍然抛 <see cref="InvalidDataException"/>，
/// 由安装器统一转成用户可读的失败结果。</para>
/// </remarks>
public sealed class SpaceDockInstallAdapter
{
    private static readonly HashSet<string> GameRootFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ships",
        "Missions",
        "Subassemblies",
        "Saves",
        "Scenarios",
    };

    /// <summary>安装目标根目录。</summary>
    public enum Target
    {
        /// <summary>KSP 的 GameData 目录。</summary>
        GameData,

        /// <summary>KSP 游戏根目录（Ships、Missions 等）。</summary>
        GameRoot,
    }

    /// <summary>识别出的压缩包布局。</summary>
    public enum Strategy
    {
        /// <summary>压缩包里有 GameData，按 GameData 下的内容安装。</summary>
        GameDataTree,

        /// <summary>没有 GameData，但只有一个顶层模组文件夹。</summary>
        SingleFolder,

        /// <summary>没有 GameData，也没有统一顶层文件夹，整体收进 GameData/&lt;模组名&gt;。</summary>
        FlatPayload,

        /// <summary>压缩包提供的是 Ships / Missions 等游戏根目录内容。</summary>
        GameRootTree,

        /// <summary>同时包含 GameData 与游戏根目录内容。</summary>
        MixedRoots,
    }

    /// <summary>一条压缩包条目的安装映射。</summary>
    /// <param name="SourcePath">压缩包内的原始条目路径。</param>
    /// <param name="Target">目标根目录。</param>
    /// <param name="RelativePath">相对目标根目录的路径，统一使用 <c>/</c>。</param>
    public sealed record Entry(string SourcePath, Target Target, string RelativePath);

    /// <summary>一次安装的完整布局计划。</summary>
    /// <param name="Strategy">识别出的布局。</param>
    /// <param name="InstalledRoot">GameData 下的安装根目录；纯游戏根目录布局为空。</param>
    /// <param name="Entries">需要解压的条目及目标路径。</param>
    public sealed record Plan(Strategy Strategy, string InstalledRoot, IReadOnlyList<Entry> Entries);

    /// <summary>
    /// 根据压缩包条目名生成安装计划。
    /// </summary>
    /// <param name="entryNames">压缩包内的条目路径（目录条目可传可不传）。</param>
    /// <param name="modName">SpaceDock 模组名，用作无结构压缩包的兜底文件夹名。</param>
    public static Plan Analyze(IEnumerable<string> entryNames, string modName)
    {
        ArgumentNullException.ThrowIfNull(entryNames);
        var files = new List<FileParts>();
        foreach (var name in entryNames)
        {
            var parts = SplitEntryPath(name);
            if (parts.Length > 0)
            {
                files.Add(new FileParts(name, parts));
            }
        }

        var safeModName = SanitizeName(modName);
        if (files.Count == 0)
        {
            return new Plan(Strategy.FlatPayload, safeModName, Array.Empty<Entry>());
        }

        var hasGameData = files.Any(static file => IndexOfGameData(file.Parts) >= 0);
        var rootFiles = files
            .Where(static file => IndexOfGameData(file.Parts) < 0 && IndexOfGameRoot(file.Parts) >= 0)
            .ToArray();

        if (hasGameData)
        {
            var entries = new List<Entry>(files.Count);
            foreach (var file in files)
            {
                var index = IndexOfGameData(file.Parts);
                if (index >= 0 && index + 1 < file.Parts.Length)
                {
                    var relative = file.Parts[(index + 1)..];
                    if (relative.Length == 1)
                    {
                        // GameData/Mod.dll 这种没有模组文件夹的包，补一层，避免把 DLL 散落在 GameData 根。
                        relative = [safeModName, .. relative];
                    }

                    entries.Add(new Entry(file.Name, Target.GameData, string.Join('/', relative)));
                }
                else
                {
                    var rootIndex = IndexOfGameRoot(file.Parts);
                    if (rootIndex >= 0)
                    {
                        entries.Add(new Entry(file.Name, Target.GameRoot, string.Join('/', file.Parts[rootIndex..])));
                    }
                }
            }

            var installedRoot = entries
                .Where(static entry => entry.Target == Target.GameData)
                .Select(static entry => entry.RelativePath.Split('/', 2)[0])
                .FirstOrDefault() ?? safeModName;
            return new Plan(rootFiles.Length > 0 ? Strategy.MixedRoots : Strategy.GameDataTree, installedRoot, entries);
        }

        if (rootFiles.Length > 0)
        {
            var entries = rootFiles
                .Select(static file =>
                {
                    var rootIndex = IndexOfGameRoot(file.Parts);
                    return new Entry(file.Name, Target.GameRoot, string.Join('/', file.Parts[rootIndex..]));
                })
                .ToArray();
            return new Plan(Strategy.GameRootTree, string.Empty, entries);
        }

        var topFolders = files
            .Select(static file => file.Parts[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var singleFolder = topFolders.Length == 1 && files.All(static file => file.Parts.Length > 1);
        if (singleFolder)
        {
            // 单层顶层目录通常只是发布包装（Wrapper/、Release/），不是模组真正的 GameData 文件夹；
            // 去掉这一层后统一收到模组名下，和旧版安装行为保持一致。
            var entries = files
                .Select(file => new Entry(file.Name, Target.GameData, string.Join('/', [safeModName, .. file.Parts[1..]])))
                .ToArray();
            return new Plan(Strategy.SingleFolder, safeModName, entries);
        }

        var flatEntries = files
            .Select(file => new Entry(file.Name, Target.GameData, string.Join('/', [safeModName, .. file.Parts])))
            .ToArray();
        return new Plan(Strategy.FlatPayload, safeModName, flatEntries);
    }

    /// <summary>拆分并校验压缩包条目路径；绝对路径、盘符与 .. 都会拒绝。</summary>
    public static string[] SplitEntryPath(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Array.Empty<string>();
        }

        if (fullName[0] is '/' or '\\')
        {
            throw new InvalidDataException($"压缩包条目使用了绝对路径：{fullName}");
        }

        var normalized = fullName.Replace('\\', '/');
        if (normalized.Length == 0 || Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"压缩包条目使用了绝对路径：{fullName}");
        }

        var rawParts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>(rawParts.Length);
        foreach (var part in rawParts)
        {
            if (part is "." or "..")
            {
                throw new InvalidDataException($"压缩包条目包含相对路径跳转：{fullName}");
            }

            if (part.Contains(':') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException($"压缩包条目包含非法字符：{fullName}");
            }

            parts.Add(part);
        }

        return parts.ToArray();
    }

    /// <summary>把模组名压成安全的单层文件夹名。</summary>
    public static string SanitizeName(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "SpaceDockMod" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (var ch in source)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(result) ? "SpaceDockMod" : result;
    }

    private static int IndexOfGameData(string[] parts)
    {
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Equals("GameData", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfGameRoot(string[] parts)
    {
        // 只认顶层或一层发布目录下的 Ships/Missions：再深就可能只是模组自己的子目录。
        for (var index = 0; index < parts.Length && index < 2; index++)
        {
            if (GameRootFolders.Contains(parts[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record FileParts(string Name, string[] Parts);
}
