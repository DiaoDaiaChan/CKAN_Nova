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
    public async Task 没写enabled的列表按开启显示且拨回后不留显式值()
    {
        var document = CreateEmptyDocument(fileExists: true);
        var unspecified = new SoundtrackPlaylist { Name = "默认开" };
        unspecified.Tracks.Add(new SoundtrackTrackRef { Name = "ambient.ogg" });
        document.Playlists.Add(unspecified);

        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        var row = page.SelectedPlaylist!;

        Assert.Multiple(() =>
        {
            Assert.That(row.Enabled, Is.Null, "cfg 没写 enabled 时模型保持 null");
            Assert.That(row.IsEnabled, Is.True, "Soundtrack 把没写 enabled 当作开启，界面不能显示成关");
        });

        row.IsEnabled = false;
        Assert.Multiple(() =>
        {
            Assert.That(row.Enabled, Is.False);
            Assert.That(page.IsDirty, Is.True);
        });

        row.IsEnabled = true;
        Assert.Multiple(() =>
        {
            Assert.That(row.Enabled, Is.Null, "拨回开启应回到「没写」，而不是补一行 enabled = True");
            Assert.That(row.IsEnabled, Is.True);
            Assert.That(page.IsDirty, Is.False, "表示回到与磁盘一致，脏标记应清零");
        });
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

    [Test]
    public async Task 节点树按场景分组且未限定场景排在最前()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("scene", "VAB | SPH"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        Assert.Multiple(() =>
        {
            Assert.That(page.HasTriggerGroups, Is.True);
            Assert.That(page.TriggerGroups, Has.Count.EqualTo(2));

            Assert.That(page.TriggerGroups[0].Key, Is.EqualTo(SoundtrackScenes.UnrestrictedKey));
            Assert.That(page.TriggerGroups[0].Title, Is.EqualTo("未限定场景"));
            Assert.That(page.TriggerGroups[0].Playlists.Single().Name, Is.EqualTo("太空"));

            Assert.That(page.TriggerGroups[1].Key, Is.EqualTo("VAB|SPH"));
            Assert.That(page.TriggerGroups[1].Title, Is.EqualTo("载具组装厂、航天器机库"));
            Assert.That(page.TriggerGroups[1].Subtitle, Is.EqualTo("VAB | SPH"));
            Assert.That(page.TriggerGroups[1].Playlists.Single().Name, Is.EqualTo("发射"));
            Assert.That(page.TriggerGroups[1].CountText, Is.EqualTo("1 个列表 · 2 首"));
            Assert.That(page.TriggerGroups[1].MissingText, Is.EqualTo("1 首缺失"));

            // 行内不重复场景，详情区才把 scene 也列出来。
            Assert.That(page.Playlists[0].Conditions.Select(condition => condition.Key), Does.Not.Contain("scene"));
            Assert.That(page.Playlists[0].AllConditions[0].Value, Is.EqualTo("载具组装厂、航天器机库"));
        });
    }

    [Test]
    public async Task 写了bodyName的场景按星球细分且任何星球排最前()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("scene", "Flight | PSystem"));
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("bodyName", "Kerbin"));
        document.Playlists[1].PlayWhen.Add(new KeyValuePair<string, string>("scene", "Flight | PSystem"));
        document.Playlists[1].PlayWhen.Add(new KeyValuePair<string, string>("bodyName", "Duna"));
        var generic = new SoundtrackPlaylist { Name = "通用" };
        generic.PlayWhen.Add(new KeyValuePair<string, string>("scene", "Flight | PSystem"));
        document.Playlists.Add(generic);
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        var group = page.TriggerGroups.Single(entry => entry.Key == "Flight|PSystem");
        Assert.Multiple(() =>
        {
            Assert.That(group.HasBodyGroups, Is.True);
            Assert.That(group.BodyGroups.Select(body => body.Title),
                Is.EqualTo(new[] { "任何星球", "Duna", "Kerbin" }), "没写 bodyName 的列表不能丢，归到「任何星球」并排最前。");
            Assert.That(group.BodyGroups[0].Playlists.Single().Name, Is.EqualTo("通用"));
            Assert.That(group.BodyGroups.Single(body => body.Key == "Kerbin").Playlists.Single().Name, Is.EqualTo("发射"));
            Assert.That(group.BodyGroups.Single(body => body.Key == "Duna").Playlists.Single().Name, Is.EqualTo("太空"));
            Assert.That(group.BodyGroups.All(body => body.IsExpanded), Is.True, "星球少时默认展开，别让用户每层都点一次。");
            Assert.That(page.SelectedPlaylist!.Name, Is.EqualTo("通用"), "初始选中应取画布第一行，而不是 cfg 里第一条。");
        });

        var kerbin = group.BodyGroups.Single(body => body.Key == "Kerbin");
        page.ToggleBodyGroupCommand.Execute(kerbin);

        Assert.Multiple(() =>
        {
            Assert.That(kerbin.IsExpanded, Is.False);
            Assert.That(page.SelectedPlaylist!.Name, Is.EqualTo("通用"));
        });

        page.SelectPlaylistCommand.Execute(page.Playlists[0]);

        Assert.Multiple(() =>
        {
            Assert.That(kerbin.IsExpanded, Is.True, "点一条藏在收起星球里的列表，要把它所在星球展开。");
            Assert.That(page.SelectedPlaylist!.Name, Is.EqualTo("发射"));
        });
    }

    [Test]
    public async Task 添加或移除bodyName会实时增减星球分组()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("scene", "Flight"));
        document.Playlists[1].PlayWhen.Add(new KeyValuePair<string, string>("scene", "Flight"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());
        await StartAsync(page, service);
        page.SelectPlaylistCommand.Execute(page.Playlists[0]);

        page.NewConditionField = SoundtrackTriggerCatalog.Find("bodyName");
        page.NewConditionValue = "Kerbin";
        page.AddConditionCommand.Execute(null);

        var group = page.TriggerGroups.Single(entry => entry.Key == "Flight");
        Assert.Multiple(() =>
        {
            Assert.That(group.HasBodyGroups, Is.True);
            Assert.That(group.BodyGroups.Select(body => body.Title), Is.EqualTo(new[] { "任何星球", "Kerbin" }));
            Assert.That(page.SelectedPlaylist!.Model.PlayWhen, Has.Some.Matches<KeyValuePair<string, string>>(
                pair => pair.Key == "bodyName" && pair.Value == "Kerbin"));
            Assert.That(page.IsDirty, Is.True);
        });

        var row = page.SelectedConditions.Single(condition => condition.Key == "bodyName");
        page.RemoveConditionCommand.Execute(row);

        // 树是整棵重建的，重建前的 group 引用已经不在 TriggerGroups 里，断言要看新对象。
        var rebuilt = page.TriggerGroups.Single(entry => entry.Key == "Flight");
        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.HasBodyGroups, Is.False, "最后一个 bodyName 删掉后，星球层要收掉，不能留一个空壳。");
            Assert.That(rebuilt.Playlists, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task 点节点切换选中并展开所属分组()
    {
        var document = CreateDocument();
        document.Playlists[1].PlayWhen.Add(new KeyValuePair<string, string>("scene", "Flight"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        var group = page.TriggerGroups.Single(entry => entry.Key == "Flight");
        group.IsExpanded = false;

        page.SelectPlaylistCommand.Execute(page.Playlists[1]);

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedPlaylist, Is.SameAs(page.Playlists[1]));
            Assert.That(page.Playlists[1].IsSelected, Is.True);
            Assert.That(group.IsExpanded, Is.True, "选中项藏在收起的组里，用户会以为点击没反应。");
        });

        page.ToggleGroupCommand.Execute(group);

        Assert.That(group.IsExpanded, Is.False);
    }

    [Test]
    public async Task 详情区给出触发条件原文与没写playWhen的说明()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("scene", "VAB | SPH"));
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("timeOfDay", "DayAM"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        // 初始选中取节点树第一行（按场景重排后的第一项），这里显式选中带 playWhen 的那条再断言详情区。
        page.SelectPlaylistCommand.Execute(page.Playlists[0]);

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedHasPlayWhen, Is.True);
            Assert.That(page.SelectedPlayWhenText, Is.EqualTo("scene = VAB | SPH · timeOfDay = DayAM"));
            Assert.That(page.SelectedPlaylist!.AllConditions.Select(condition => condition.Key),
                Is.EqualTo(new[] { "scene", "timeOfDay" }));
        });

        page.SelectPlaylistCommand.Execute(page.Playlists[1]);

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedHasPlayWhen, Is.False);
            Assert.That(page.SelectedPlayWhenText, Is.EqualTo("playWhen 里没有写任何键"));
            Assert.That(page.SelectedPlaylist!.AllConditions.Single().Value, Is.EqualTo("任何场景"));
        });
    }

    [Test]
    public async Task 新建的播放列表落到未限定场景分组()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("scene", "VAB"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);
        page.CreatePlaylist();

        Assert.Multiple(() =>
        {
            Assert.That(page.TriggerGroups, Has.Count.EqualTo(2));
            var group = page.TriggerGroups.Single(entry => entry.Key == SoundtrackScenes.UnrestrictedKey);
            Assert.That(group.Playlists.Select(row => row.Name), Does.Contain("新建播放列表"));
            Assert.That(page.SelectedPlaylist!.Name, Is.EqualTo("新建播放列表"));
            Assert.That(group.IsExpanded, Is.True);
        });
    }

    [Test]
    public async Task 播放列表与音乐播放器联动及音效触发()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        var instances = CreateInstances();
        var musicPlayer = new FakeMusicPlayerService();
        var soundEffects = new FakeSoundEffectService();
        using var page = SoundtrackPageViewModel.CreateForTests(service, instances, musicPlayer, soundEffects);

        await StartAsync(page, service);

        // 验证初始状态
        Assert.Multiple(() =>
        {
            Assert.That(page.IsPlaying, Is.False);
            Assert.That(page.PlayPauseButtonText, Is.EqualTo("播放"));
            Assert.That(page.PlaySelectedPlaylistCommand.CanExecute(null), Is.True);
        });

        // 播放当前选中播放列表
        page.PlaySelectedPlaylist();

        Assert.Multiple(() =>
        {
            Assert.That(musicPlayer.PlayCount, Is.GreaterThan(0));
            Assert.That(musicPlayer.CurrentPlaylistName, Is.EqualTo("发射"));
            Assert.That(soundEffects.PlayedEffects, Does.Contain(CkanUI.Services.Abstractions.Audio.SoundEffectType.Ok));
        });

        // 切换播放/暂停
        page.TogglePlayPause();
        Assert.That(musicPlayer.PauseCount, Is.EqualTo(1));

        page.TogglePlayPause();
        Assert.That(musicPlayer.ResumeCount, Is.EqualTo(1));

        // 停止播放
        page.StopMusic();
        Assert.That(musicPlayer.StopCount, Is.EqualTo(1));

        // 下一首/上一首
        page.NextTrack();
        Assert.That(musicPlayer.NextCount, Is.EqualTo(1));

        page.PreviousTrack();
        Assert.That(musicPlayer.PreviousCount, Is.EqualTo(1));

        // 静音与音量调节
        page.TogglePlayerMute();
        Assert.That(page.IsPlayerMuted, Is.True);
        Assert.That(page.PlayerMuteButtonText, Is.EqualTo("🔇"));

        page.PlayerVolume = 50;
        Assert.That(musicPlayer.Volume, Is.EqualTo(0.5).Within(0.01));

        // 进度拖拽跳转
        page.ProgressValue = 60;
        Assert.That(musicPlayer.LastSeek, Is.Not.Null);
    }

    [Test]
    public async Task 流程链从首曲名走到条件且只在链尾收箭头()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("scene", "VAB"));
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("timeOfDay", "DayAM"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        var nodes = page.Playlists[0].FlowNodes;
        Assert.Multiple(() =>
        {
            Assert.That(nodes[0].Label, Is.EqualTo("曲目"));
            Assert.That(nodes[0].Value, Is.EqualTo("launch.ogg +1"), "链头写曲名，后面补总数；用户第一个问题就是“哪首歌”。");
            Assert.That(nodes.Select(node => node.Label), Is.EqualTo(new[] { "曲目", "时段" }));
            Assert.That(nodes[^1].IsLast, Is.True);
            Assert.That(nodes.Count(node => node.IsLast), Is.EqualTo(1), "只有链尾不接箭头，否则会指向空气。");
        });

        // 没写 playWhen 的那条链也要有落点，不能是一节孤零零的“曲目”。
        var plain = page.Playlists[1].FlowNodes;
        Assert.Multiple(() =>
        {
            Assert.That(plain.Select(node => node.Label), Is.EqualTo(new[] { "曲目", "条件" }));
            Assert.That(plain[^1].Value, Is.EqualTo("任意"));
            Assert.That(plain[^1].IsLast, Is.True);
        });
    }

    [Test]
    public async Task 流程链把超过三条的条件收成还有N项()
    {
        var document = CreateDocument();
        var playlist = document.Playlists[0];
        playlist.PlayWhen.Add(new KeyValuePair<string, string>("scene", "Flight"));
        playlist.PlayWhen.Add(new KeyValuePair<string, string>("bodyName", "Kerbin"));
        playlist.PlayWhen.Add(new KeyValuePair<string, string>("timeOfDay", "DayAM"));
        playlist.PlayWhen.Add(new KeyValuePair<string, string>("cameraMode", "Map"));
        playlist.PlayWhen.Add(new KeyValuePair<string, string>("situation", "FLYING"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());

        await StartAsync(page, service);

        var nodes = page.Playlists[0].FlowNodes;
        Assert.Multiple(() =>
        {
            // 顺序跟触发器目录一致：星球 → 时段 → 视角；第四条“飞行状态”收成“还有 1 项”。
            Assert.That(nodes.Select(node => node.Label),
                Is.EqualTo(new[] { "曲目", "星球", "时段", "视角", "还有" }));
            Assert.That(nodes[^1].Value, Is.EqualTo("1 项"));
            Assert.That(nodes[^1].IsLast, Is.True);
        });
    }

    [Test]
    public async Task 添加条件后重排分组并计入脏状态()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());
        await StartAsync(page, service);

        page.NewConditionField = SoundtrackTriggerCatalog.Find("timeOfDay");
        page.NewConditionValue = "nightam | DayPM";
        page.AddConditionCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedConditions, Has.Count.EqualTo(1));
            Assert.That(page.SelectedConditions[0].Key, Is.EqualTo("timeOfDay"));
            Assert.That(page.SelectedConditions[0].Value, Is.EqualTo("NightAM | DayPM"));
            Assert.That(page.SelectedPlaylist!.Model.PlayWhen.Single().Value, Is.EqualTo("NightAM | DayPM"));
            Assert.That(page.IsDirty, Is.True, "签名必须把 playWhen 算进去，否则改完条件页面还说和磁盘一致。");
            Assert.That(page.SelectedPlayWhenText, Does.Contain("timeOfDay = NightAM | DayPM"));
        });

        page.NewConditionField = SoundtrackTriggerCatalog.Find("scene");
        page.NewConditionValue = "VAB|SPH";
        page.AddConditionCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(page.TriggerGroups, Has.Count.EqualTo(2));
            var vab = page.TriggerGroups.Single(group => group.Key == "VAB|SPH");
            Assert.That(vab.Playlists.Select(row => row.Name), Does.Contain("发射"));
            Assert.That(page.Playlists[0].FlowNodes.Select(node => node.Label), Does.Contain("时段"));
        });
    }

    [Test]
    public async Task 添加条件时非法枚举报在表单里不动模型()
    {
        var service = new FakeSoundtrackService { Document = CreateDocument() };
        using var page = CreatePage(service, CreateInstances());
        await StartAsync(page, service);

        page.NewConditionField = SoundtrackTriggerCatalog.Find("situation");
        page.NewConditionValue = "LANDED | DANCING";
        page.AddConditionCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(page.HasNewConditionError, Is.True);
            Assert.That(page.NewConditionError, Does.Contain("DANCING"));
            Assert.That(page.SelectedPlaylist!.Model.PlayWhen, Is.Empty);
            Assert.That(page.IsDirty, Is.False);
        });
    }

    [Test]
    public async Task 应用条件时非法值弹错误并保留原值()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("timeOfDay", "DayAM"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());
        await StartAsync(page, service);
        page.SelectPlaylistCommand.Execute(page.Playlists[0]);

        var row = page.SelectedConditions.Single();
        row.Draft = "DANCING";
        page.ApplyConditionCommand.Execute(row);

        Assert.Multiple(() =>
        {
            Assert.That(page.ErrorMessage, Does.Contain("时段"));
            Assert.That(row.Value, Is.EqualTo("DayAM"));
            Assert.That(page.SelectedPlaylist!.Model.PlayWhen.Single().Value, Is.EqualTo("DayAM"), "校验失败不能把半截值写进模型。");
        });
    }

    [Test]
    public async Task 移除场景条件后列表回到未限定分组()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("scene", "VAB"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());
        await StartAsync(page, service);
        page.SelectPlaylistCommand.Execute(page.Playlists[0]);

        var row = page.SelectedConditions.Single(condition => condition.Key == "scene");
        page.RemoveConditionCommand.Execute(row);

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedPlaylist!.Model.PlayWhen, Is.Empty);
            Assert.That(page.TriggerGroups, Has.Count.EqualTo(1));
            Assert.That(page.TriggerGroups[0].Playlists.Select(entry => entry.Name), Does.Contain("发射"));
            Assert.That(page.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task 保存前把逗号分隔的situation规范化()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("situation", "LANDED, ORBITING"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());
        await StartAsync(page, service);

        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.SaveCount, Is.EqualTo(1));
            Assert.That(service.SavedDocument!.Playlists[0].PlayWhen.Single().Value, Is.EqualTo("LANDED | ORBITING"));
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.HasError, Is.False);
        });
    }

    [Test]
    public async Task 非法条件在保存前被拦下()
    {
        var document = CreateDocument();
        document.Playlists[0].PlayWhen.Add(new KeyValuePair<string, string>("timeOfDay", "DANCING"));
        var service = new FakeSoundtrackService { Document = document };
        using var page = CreatePage(service, CreateInstances());
        await StartAsync(page, service);

        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.SaveCount, Is.Zero);
            Assert.That(page.ErrorMessage, Does.Contain("填得不对"));
            Assert.That(page.ErrorMessage, Does.Contain("时段"));
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