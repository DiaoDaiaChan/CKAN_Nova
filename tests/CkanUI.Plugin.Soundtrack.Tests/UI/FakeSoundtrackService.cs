using CkanUI.Plugin.Soundtrack.Playlists;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Soundtrack.Tests.UI;

/// <summary>
/// 记录调用、按用例改返回值的播放列表服务替身。
/// </summary>
/// <remarks>
/// 页面只依赖 ISoundtrackService 的三个成员，所以用例不需要临时目录和真实文件：
/// 读盘结果由 <see cref="Document"/> 直接给，保存成败由 <see cref="SaveHandler"/> 决定，
/// 这样“服务说失败时页面还敢不敢清脏标记”这类断言才不会被磁盘状态带偏。
/// </remarks>
internal sealed class FakeSoundtrackService : ISoundtrackService
{
    /// <summary>每次 Load 返回的文档；重新载入的用例换成新实例来模拟“磁盘上的内容变了”。</summary>
    public SoundtrackDocument Document { get; set; } = new();

    public int LoadCount { get; private set; }

    public List<string> LoadedGameDirs { get; } = new();

    public int SaveCount { get; private set; }

    public SoundtrackDocument? SavedDocument { get; private set; }

    public int ResolveCount { get; private set; }

    /// <summary>不设置时返回成功并带备份路径；用例可用它模拟服务失败或第一次写入。</summary>
    public Func<SoundtrackDocument, OperationResult<SoundtrackSaveOutcome>>? SaveHandler { get; set; }

    public SoundtrackDocument Load(string gameDir)
    {
        LoadCount++;
        LoadedGameDirs.Add(gameDir);
        return Document;
    }

    public OperationResult<SoundtrackSaveOutcome> Save(SoundtrackDocument document)
    {
        SaveCount++;
        SavedDocument = document;
        if (SaveHandler is not null)
        {
            return SaveHandler(document);
        }

        return OperationResult<SoundtrackSaveOutcome>.Ok(new SoundtrackSaveOutcome(
            document.FilePath,
            document.FilePath + ".bak",
            document.Playlists.Count,
            document.Playlists.Sum(playlist => playlist.Tracks.Count)));
    }

    public void ResolveTracks(SoundtrackDocument document) => ResolveCount++;
}
