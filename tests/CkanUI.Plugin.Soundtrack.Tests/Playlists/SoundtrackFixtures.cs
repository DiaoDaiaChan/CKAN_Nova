using System.Globalization;
using System.Text;
using CkanUI.Plugin.Soundtrack.Playlists;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.Playlists;

/// <summary>
/// 播放列表测试的共享夹具：仓库根、契约样例、临时实例目录。
/// </summary>
internal static class SoundtrackFixtures
{
    /// <summary>仓库根（含 ckan-ui.sln 的目录）。</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>契约样例 playlists.cfg 的路径。</summary>
    public static string SamplePath =>
        Path.Combine(RepositoryRoot, "docs", "design", "soundtrack.sample", "playlists.cfg");

    /// <summary>契约样例原文（保持签出时的行尾）。</summary>
    public static string SampleText => File.ReadAllText(SamplePath);

    /// <summary>契约样例统一成 LF 后的文本。</summary>
    public static string SampleLf => SampleText.ReplaceLineEndings("\n");

    /// <summary>
    /// 把播放列表变成可逐字比较的文本，用于“往返后语义相等”的断言。
    /// 用文本而不是逐字段断言：失败时 NUnit 会直接打印两行差异，能一眼看出是哪个键丢了。
    /// </summary>
    public static string Describe(SoundtrackPlaylist playlist) => string.Join(
        " | ",
        playlist.Name,
        playlist.Enabled?.ToString() ?? "<null>",
        playlist.Loop.ToString(),
        playlist.Shuffle.ToString(),
        playlist.PauseOnGamePause.ToString(),
        playlist.DisableAfterPlay.ToString(),
        playlist.PlayNext,
        playlist.PlayBefore,
        playlist.PlayAfter,
        playlist.Channel?.ToString(CultureInfo.InvariantCulture) ?? "<null>",
        playlist.PreloadTime?.ToString("R", CultureInfo.InvariantCulture) ?? "<null>",
        "tracks:" + string.Join(",", playlist.Tracks.Select(track => track.Name)),
        "playWhen:" + string.Join(",", playlist.PlayWhen.Select(pair => pair.Key + "=" + pair.Value)),
        "extra:" + string.Join(",", playlist.ExtraValues.Select(pair => pair.Key + "=" + pair.Value)),
        "unknown:" + string.Join("~~~", playlist.UnknownNodes).Replace("\n", "\\n", StringComparison.Ordinal));

    /// <summary>把一组播放列表描述成多行文本。</summary>
    public static string DescribeAll(IEnumerable<SoundtrackPlaylist> playlists) =>
        string.Join(Environment.NewLine, playlists.Select(Describe));

    private static string FindRepositoryRoot()
    {
        // 必须从测试程序集所在目录往上找，不能用 AppContext.BaseDirectory：
        // TestRunner（tests/CkanUI.TestRunner）是进程内宿主，BaseDirectory 指向它自己的输出目录，
        // 从那里向上找虽然也能到仓库根，但一旦换成别的宿主就会指向完全不相关的位置。
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(SoundtrackPlaylistFormat).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ckan-ui.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到仓库根（ckan-ui.sln）；测试夹具无法定位契约样例。");
    }
}

/// <summary>
/// 临时“游戏实例”目录：建在 artifacts/tmp 下（仓库约定：沙箱里的系统 TEMP 可能不可写），用完递归删。
/// </summary>
internal sealed class TempGameDirectory : IDisposable
{
    public TempGameDirectory()
    {
        Path = System.IO.Path.Combine(
            SoundtrackFixtures.RepositoryRoot,
            "artifacts",
            "tmp",
            "soundtrack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>实例根目录。</summary>
    public string Path { get; }

    /// <summary>在实例根目录下拼子路径（不创建）。</summary>
    public string Combine(params string[] parts) =>
        System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    /// <summary>写入 Playlists/playlists.cfg，返回文件全路径。</summary>
    public string WritePlaylists(string text, string newLine = "\r\n")
    {
        var file = Combine("Playlists", "playlists.cfg");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text.ReplaceLineEndings(newLine), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return file;
    }

    /// <summary>造一个指定内容的音频文件，返回全路径。</summary>
    public string WriteMusic(string relativePath, int sizeBytes = 128)
    {
        var file = Combine(relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, new byte[sizeBytes]);
        return file;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 句柄未释放（杀毒/索引器）时留着，artifacts 本来就不入库。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
