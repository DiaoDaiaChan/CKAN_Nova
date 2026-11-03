using CkanUI.Plugin.Soundtrack.Playlists;
using CkanUI.Plugin.Soundtrack.UI;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.UI;

/// <summary>
/// 「音乐」页视图模型：载入、编辑、脏状态、保存与空态。
/// </summary>
/// <remarks>
/// 用例守的是界面层最容易骗人的几件事：改完到底脏没脏、服务说失败时页面敢不敢说成功、
/// 缺失曲目会不会被悄悄藏起来、文件不存在时用户有没有一条能走通的创建路径。
/// 页面只依赖 ISoundtrackService 与 IInstanceService，所以全部走替身，不碰真实磁盘。
/// </remarks>
[TestFixture]
public sealed class SoundtrackPageViewModelTests
{
    private static string GameDir => Path.Combine(Path.GetTempPath(), "ckan-ui-soundtrack-tests", "demo");

    private static string AudioDir => Path.Combine(GameDir, "GameData", "Soundtrack", "Music");

    private static string PlaylistPath => Path.Combine(GameDir, "Playlists", "playlists.cfg");

    [Test]
    public async Task 载入后左栏行数与选中项正确()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        var instances = CreateInstances();
        using var page = CreatePage(service, instances);

        await StartAsync(page, service);

        Assert.Multiple(() =>
        {
            Assert.That(page.Playlists, Has.Count.EqualTo(2));
            Assert.That(page.SelectedPlaylist, Is.Not.Null);
            Assert.That(page.SelectedPlaylist!.Name, Is.EqualTo("发射"));
            Assert.That(page.SelectedPlaylist.IsSelected, Is.True);
            Assert.That(page.Playlists[1].IsSelected, Is.False);
            Assert.That(page.SelectedTracks, Has.Count.EqualTo(2));
            Assert.That(page.PlaylistCountText, Is.EqualTo("2 个"));
            Assert.That(page.TrackCountText, Is.EqualTo("3 首"));
            Assert.That(page.MissingTrackCountText, Is.EqualTo("1 首缺失"));
            Assert.That(page.PlaylistFilePath, Is.EqualTo(PlaylistPath));
            Assert.That(page.ModStatusText, Does.Contain("已安装"));
            Assert.That(page.IsModInstalled, Is.True);
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.ShowContent, Is.True);
            Assert.That(service.LoadedGameDirs, Is.EqualTo(new[] { GameDir }));
        });
    }

    [Test]
    public async Task 缺失曲目仍然显示并标注缺失()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        var missing = page.SelectedTracks.Single(row => row.Name == "missing.ogg");
        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedTracks, Has.Count.EqualTo(2));
            Assert.That(missing.Exists, Is.False);
            Assert.That(missing.HasMissing, Is.True);
            Assert.That(missing.MissingText, Is.EqualTo("缺失"));
            Assert.That(page.SelectedTrackCountText, Does.Contain("1 首缺失"));
            Assert.That(page.MissingTrackCountText, Does.Contain("1 首缺失"));
        });
    }

    [Test]
    public async Task 改开关后变为脏状态()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        Assert.That(page.IsDirty, Is.False);

        page.SelectedPlaylist!.Enabled = false;

        Assert.That(page.IsDirty, Is.True);
        Assert.That(page.DirtyText, Is.EqualTo("有未保存的修改"));
    }

    [Test]
    public async Task 改名后变为脏状态()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        page.SelectedPlaylistName = "改过的名字";

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedPlaylist!.Name, Is.EqualTo("改过的名字"));
            Assert.That(page.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task 从乐曲库添加曲目后追加到末尾并变脏()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        var libraryRow = page.LibraryRows.Single(row => row.Name == "new_song.ogg");

        page.AddTrack(libraryRow);

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedTracks, Has.Count.EqualTo(3));
            Assert.That(page.SelectedPlaylist!.Model.Tracks[^1].Name, Is.EqualTo("new_song.ogg"));
            Assert.That(page.SelectedTracks[^1].Name, Is.EqualTo("new_song.ogg"));
            Assert.That(libraryRow.IsAdded, Is.True);
            Assert.That(page.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task 曲目上移下移后顺序与脏状态跟随变化()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        var first = page.SelectedTracks[0].Name;
        var second = page.SelectedTracks[1].Name;

        page.MoveTrackUp(page.SelectedTracks[1]);
        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedTracks[0].Name, Is.EqualTo(second));
            Assert.That(page.SelectedTracks[1].Name, Is.EqualTo(first));
            Assert.That(page.SelectedPlaylist!.Model.Tracks[0].Name, Is.EqualTo(second));
            Assert.That(page.IsDirty, Is.True);
        });

        page.MoveTrackDown(page.SelectedTracks[0]);
        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedTracks[0].Name, Is.EqualTo(first));
            // 改回原顺序等于和磁盘一致：脏标记应该自己消失，而不是逼用户再存一次。
            Assert.That(page.IsDirty, Is.False);
        });
    }

    [Test]
    public async Task 移除曲目后行与模型同步并变脏()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        var removed = page.SelectedTracks[0];

        page.RemoveTrack(removed);

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedTracks, Has.Count.EqualTo(1));
            Assert.That(page.SelectedPlaylist!.Model.Tracks, Has.Count.EqualTo(1));
            Assert.That(page.SelectedPlaylist.Model.Tracks.Any(track => track.Name == removed.Name), Is.False);
            Assert.That(page.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task 保存成功只调用一次服务并清脏且提示备份路径()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        page.SelectedPlaylistName = "改过的名字";
        Assert.That(page.IsDirty, Is.True);

        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.SaveCount, Is.EqualTo(1));
            Assert.That(service.SavedDocument, Is.Not.Null);
            Assert.That(service.SavedDocument!.Playlists[0].Name, Is.EqualTo("改过的名字"));
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.StatusMessage, Does.Contain("已保存"));
            Assert.That(page.StatusMessage, Does.Contain(".bak"));
            Assert.That(page.ErrorMessage, Is.Empty);
        });
    }

    [Test]
    public async Task 保存失败时保留脏状态并显示服务给的原因()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        service.SaveHandler = _ => OperationResult<SoundtrackSaveOutcome>.Fail("playlists.cfg 正被 Soundtrack 占用。");
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        page.SelectedPlaylistName = "改过的名字";

        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.SaveCount, Is.EqualTo(1));
            Assert.That(page.IsDirty, Is.True);
            Assert.That(page.StatusMessage, Is.Empty);
            Assert.That(page.ErrorMessage, Does.Contain("保存失败"));
            Assert.That(page.ErrorMessage, Does.Contain("占用"));
        });
    }

    [Test]
    public async Task 空名字在保存前被拦下且不调用服务()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        page.SelectedPlaylistName = "   ";

        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.SaveCount, Is.Zero);
            Assert.That(page.ErrorMessage, Does.Contain("名字是空的"));
            Assert.That(page.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task 重名在保存前被拦下且不调用服务()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        page.Playlists[1].Name = "发射";

        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.SaveCount, Is.Zero);
            Assert.That(page.ErrorMessage, Does.Contain("重复"));
            Assert.That(page.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task 文件不存在时给出路径与创建入口()
    {
        var service = new FakeSoundtrackService { Document = CreateEmptyDocument(fileExists: false) };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.ShowCreateEntry, Is.True);
            Assert.That(page.EmptyTitle, Is.EqualTo("这个实例还没有 Playlists/playlists.cfg"));
            Assert.That(page.EmptyDetail, Does.Contain(PlaylistPath));
            Assert.That(page.PlaylistFilePath, Is.EqualTo(PlaylistPath));
            Assert.That(page.Playlists, Is.Empty);
        });
    }

    [Test]
    public async Task 空态新建并保存会创建第一个播放列表()
    {
        var service = new FakeSoundtrackService { Document = CreateEmptyDocument(fileExists: false) };
        service.SaveHandler = document => OperationResult<SoundtrackSaveOutcome>.Ok(
            new SoundtrackSaveOutcome(document.FilePath, null, document.Playlists.Count, 0));
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        await page.CreatePlaylistAndSaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.SaveCount, Is.EqualTo(1));
            Assert.That(service.SavedDocument, Is.Not.Null);
            Assert.That(service.SavedDocument!.Playlists, Has.Count.EqualTo(1));
            Assert.That(service.SavedDocument.FilePath, Is.EqualTo(PlaylistPath));
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.StatusMessage, Does.Contain("第一次写入"));
            Assert.That(page.ShowContent, Is.True);
            Assert.That(page.ShowEmptyState, Is.False);
        });
    }

    [Test]
    public async Task 文件存在但零个播放列表同样给创建入口()
    {
        var service = new FakeSoundtrackService { Document = CreateEmptyDocument(fileExists: true) };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.ShowCreateEntry, Is.True);
            Assert.That(page.EmptyTitle, Is.EqualTo("playlists.cfg 里还没有播放列表"));
            Assert.That(page.EmptyDetail, Does.Contain(PlaylistPath));
        });
    }

    [Test]
    public async Task 没有选中实例时给出专门文案且不读盘()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        var instances = new FakeInstanceService { Current = null };
        using var page = CreatePage(service, instances);

        page.Start();
        await WaitUntilAsync(() => page.EmptyTitle.Contains("还没有选择游戏实例"), "未选实例的空态");

        Assert.Multiple(() =>
        {
            Assert.That(service.LoadCount, Is.Zero);
            Assert.That(page.ShowCreateEntry, Is.False);
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.EmptyDetail, Does.Contain("实例"));
            Assert.That(page.Playlists, Is.Empty);
        });
    }

    [Test]
    public async Task 缺播放列表服务时给出可读说明()
    {
        var instances = CreateInstances();
        // 两个参数都传 null 服务，才是真正的“宿主没注册”形态；单参构造会自己接上插件内的实现。
        using var page = new SoundtrackPageViewModel((ISoundtrackService?)null, instances);

        page.Start();
        await WaitUntilAsync(() => page.EmptyTitle.Contains("播放列表服务未就绪"), "服务缺失空态");

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.ShowCreateEntry, Is.False);
            Assert.That(page.EmptyDetail, Does.Contain("Soundtrack"));
        });
    }

    [Test]
    public async Task 只给实例服务时自己接上播放列表服务()
    {
        var instances = CreateInstances();
        using var page = new SoundtrackPageViewModel(instances);

        page.Start();
        await WaitUntilAsync(() => !page.IsBusy, "加载完成");

        Assert.That(page.EmptyTitle, Does.Not.Contain("服务未就绪"), "插件自带的实现不该被当成缺失");
    }

    [Test]
    public async Task 重新载入丢弃未保存的修改()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        page.SelectedPlaylistName = "临时改名";
        Assert.That(page.IsDirty, Is.True);

        service.Document = CreateDocument();
        await page.ReloadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.LoadCount, Is.EqualTo(2));
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.SelectedPlaylist!.Name, Is.EqualTo("发射"));
        });
    }

    private static SoundtrackPageViewModel CreatePage(
        FakeSoundtrackService service,
        FakeInstanceService instances)
        => SoundtrackPageViewModel.CreateForTests(service, instances);

    private static FakeInstanceService CreateInstances()
        => new() { Current = new InstanceInfo("演示实例", GameDir, "1.12.5", true, false, 12) };

    private static SoundtrackDocument CreateEmptyDocument(bool fileExists)
        => new()
        {
            GameDir = GameDir,
            FilePath = PlaylistPath,
            FileExists = fileExists,
            DirectoryExists = fileExists,
            ModDirectory = Path.Combine(GameDir, "GameData", "Soundtrack"),
        };

    private static SoundtrackDocument CreateDocument()
    {
        var document = CreateEmptyDocument(fileExists: true);

        var launch = new SoundtrackPlaylist { Name = "发射", Enabled = true };
        launch.Tracks.Add(new SoundtrackTrackRef
        {
            Name = "launch.ogg",
            FilePath = Path.Combine(AudioDir, "launch.ogg"),
            SizeBytes = 2048,
        });
        // 只有名字没有路径：模拟玩家手改过 cfg、音频文件已经不在磁盘上的曲目。
        launch.Tracks.Add(new SoundtrackTrackRef { Name = "missing.ogg" });
        document.Playlists.Add(launch);

        var space = new SoundtrackPlaylist { Name = "太空", Enabled = false, Loop = false };
        space.Tracks.Add(new SoundtrackTrackRef
        {
            Name = "space.ogg",
            FilePath = Path.Combine(AudioDir, "space.ogg"),
            SizeBytes = 4096,
        });
        document.Playlists.Add(space);

        document.MusicLibrary.Add(new SoundtrackMusicFile(
            "new_song.ogg", Path.Combine(AudioDir, "new_song.ogg"), 1024, ".ogg"));
        document.MusicLibrary.Add(new SoundtrackMusicFile(
            "zzz.ogg", Path.Combine(AudioDir, "zzz.ogg"), 1024, ".ogg"));

        return document;
    }

    private static async Task StartAsync(SoundtrackPageViewModel page, FakeSoundtrackService service)
    {
        page.Start();
        await WaitUntilAsync(() => service.LoadCount > 0 && !page.IsBusy, "页面载入");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.Fail("等待" + what + "超时（5 秒）。");
    }
}
