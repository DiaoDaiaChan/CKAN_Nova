using CkanUI.Plugin.Soundtrack.Playlists;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.Playlists;

/// <summary>
/// <see cref="SoundtrackService"/> 的读盘、曲目解析与保存。
/// </summary>
/// <remarks>
/// 这里造的是“真实实例形状”的目录（Music/、GameData/、Playlists/），而不是几个空文件：
/// 扫描规则和保存路径的坑全在目录结构上（Parts 要跳过、GameData 要递归、备份要落在原文件旁边），
/// 只用文件桩测不出来。每个用例自带临时实例目录，跑完即删。
/// </remarks>
[TestFixture]
public sealed class SoundtrackServiceTests
{
    private SoundtrackService _service = null!;

    [SetUp]
    public void SetUp() => _service = new SoundtrackService();

    [Test]
    public void 空实例路径给出诊断而不是抛异常()
    {
        var document = _service.Load("   ");

        Assert.Multiple(() =>
        {
            Assert.That(document.Diagnostics, Is.Not.Empty);
            Assert.That(document.Diagnostics[0], Does.Contain("没有选择实例"));
            Assert.That(document.FileExists, Is.False);
            Assert.That(document.Playlists, Is.Empty);
        });
    }

    [Test]
    public void 实例目录不存在时返回空文档()
    {
        using var game = new TempGameDirectory();
        var missing = game.Combine("missing-instance");

        var document = _service.Load(missing);

        Assert.Multiple(() =>
        {
            Assert.That(document.FileExists, Is.False);
            Assert.That(document.DirectoryExists, Is.False);
            Assert.That(document.Playlists, Is.Empty);
            Assert.That(document.MusicLibrary, Is.Empty);
            Assert.That(document.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void 读取契约样例实例()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);
        Directory.CreateDirectory(game.Combine("GameData", "SoundtrackEditorForked"));

        var document = _service.Load(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(document.FileExists, Is.True);
            Assert.That(document.DirectoryExists, Is.True);
            Assert.That(document.FilePath, Is.EqualTo(game.Combine("Playlists", "playlists.cfg")));
            Assert.That(document.Playlists, Has.Count.EqualTo(12));
            Assert.That(document.Playlists.Sum(playlist => playlist.Tracks.Count), Is.EqualTo(37));
            Assert.That(document.NewLine, Is.EqualTo("\r\n"));
            Assert.That(document.ModDirectory, Is.EqualTo(game.Combine("GameData", "SoundtrackEditorForked")));
            Assert.That(document.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void 模组目录按Soundtrack优先探测()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);
        Directory.CreateDirectory(game.Combine("GameData", "Soundtrack"));
        Directory.CreateDirectory(game.Combine("GameData", "SoundtrackEditor"));

        var document = _service.Load(game.Path);

        // 两个都装时以原版 Soundtrack 为准：它才是真正读这个文件的模组。
        Assert.That(document.ModDirectory, Is.EqualTo(game.Combine("GameData", "Soundtrack")));
    }

    [Test]
    public void 模组目录回退到SoundtrackEditor与Forked()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);
        Directory.CreateDirectory(game.Combine("GameData", "SoundtrackEditorForked"));

        // 只装 Forked 分支时也要认出来，否则界面会把已装的模组当成没装。
        Assert.That(
            _service.Load(game.Path).ModDirectory,
            Is.EqualTo(game.Combine("GameData", "SoundtrackEditorForked")));

        Directory.CreateDirectory(game.Combine("GameData", "SoundtrackEditor"));

        // 原版编辑器与 Forked 同时存在时取更接近原版的那个，行为才和 Soundtrack 读取路径一致。
        Assert.That(
            _service.Load(game.Path).ModDirectory,
            Is.EqualTo(game.Combine("GameData", "SoundtrackEditor")));
    }

    [Test]
    public void 没装模组时ModDirectory为null()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);

        Assert.That(_service.Load(game.Path).ModDirectory, Is.Null);
    }

    [Test]
    public void 读取LF文件时探测出LF换行()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText, newLine: "\n");

        var document = _service.Load(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(document.NewLine, Is.EqualTo("\n"));
            Assert.That(document.Playlists, Has.Count.EqualTo(12));
        });
    }

    [Test]
    public void 乐曲库扫描Music与GameData并跳过Parts()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);
        game.WriteMusic("Music/foo.OGG");
        game.WriteMusic("Music/Sub/bar.wav");
        game.WriteMusic("GameData/ModA/baz.MP3");
        game.WriteMusic("GameData/ModA/also.ogg");
        game.WriteMusic("GameData/ModA/Parts/skip.ogg");
        game.WriteMusic("GameData/ModA/Parts/Sub/skip2.ogg");
        game.WriteMusic("GameData/readme.txt", sizeBytes: 8);

        var document = _service.Load(game.Path);
        var names = document.MusicLibrary.Select(file => file.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EqualTo(new[] { "also", "bar", "baz", "foo" }));
            Assert.That(
                document.MusicLibrary.Select(file => file.Extension),
                Is.EquivalentTo(new[] { ".ogg", ".wav", ".mp3", ".ogg" }));
            Assert.That(document.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void 乐曲文件名去掉扩展名并记录字节数()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);
        var path = game.WriteMusic("Music/Investigations.ogg", sizeBytes: 256);

        var document = _service.Load(game.Path);
        var file = document.MusicLibrary.Single(entry => entry.Name == "Investigations");

        Assert.Multiple(() =>
        {
            Assert.That(file.FullPath, Is.EqualTo(path));
            Assert.That(file.SizeBytes, Is.EqualTo(256));
            Assert.That(file.Extension, Is.EqualTo(".ogg"));
        });
    }

    [Test]
    public void 读取后把样例曲目解析到实际文件()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);
        var path = game.WriteMusic("Music/Investigations.ogg", sizeBytes: 256);

        var document = _service.Load(game.Path);
        var track = document.Playlists[0].Tracks.Single(entry => entry.Name == "Investigations");

        Assert.Multiple(() =>
        {
            Assert.That(track.Exists, Is.True);
            Assert.That(track.FilePath, Is.EqualTo(path));
            Assert.That(track.SizeBytes, Is.EqualTo(256));
            Assert.That(document.MissingTrackCount, Is.EqualTo(36));
        });
    }

    [Test]
    public void ResolveTracks先精确再忽略大小写()
    {
        var music = Path.Combine("C:", "KSP", "Music", "Investigations.ogg");
        var document = new SoundtrackDocument { GameDir = Path.Combine("C:", "KSP") };
        document.MusicLibrary.Add(new SoundtrackMusicFile("Investigations", music, 512, ".ogg"));
        var playlist = new SoundtrackPlaylist { Name = "Menu" };
        playlist.Tracks.Add(new SoundtrackTrackRef { Name = "Investigations" });
        playlist.Tracks.Add(new SoundtrackTrackRef { Name = "INVESTIGATIONS" });
        playlist.Tracks.Add(new SoundtrackTrackRef { Name = "Missing", FilePath = music, SizeBytes = 1 });
        document.Playlists.Add(playlist);

        _service.ResolveTracks(document);

        Assert.Multiple(() =>
        {
            Assert.That(playlist.Tracks[0].FilePath, Is.EqualTo(music));
            Assert.That(playlist.Tracks[1].FilePath, Is.EqualTo(music));
            Assert.That(playlist.Tracks[2].Exists, Is.False);
            Assert.That(playlist.Tracks[2].SizeBytes, Is.Null);
            Assert.That(document.MissingTrackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void 游戏占用播放列表时仍能读取()
    {
        using var game = new TempGameDirectory();
        var file = game.WritePlaylists(SoundtrackFixtures.SampleText);

        // 游戏开着时 Soundtrack 会以读写方式占住这个文件；插件只读打开必须允许共享。
        using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var document = _service.Load(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(document.Playlists, Has.Count.EqualTo(12));
            Assert.That(document.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void 损坏文件不抛异常并留下诊断()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists("playlist\n{\n\ttrack = Alpha\n");

        var document = _service.Load(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(document.FileExists, Is.True);
            Assert.That(document.Playlists, Is.Empty);
            Assert.That(document.Diagnostics, Is.Not.Empty);
        });
    }

    [Test]
    public void 保存生成备份且新内容可再解析()
    {
        using var game = new TempGameDirectory();
        var file = game.WritePlaylists(SoundtrackFixtures.SampleText);
        var original = File.ReadAllText(file);
        var document = _service.Load(game.Path);
        document.Playlists[0].PlayBefore = "Menu theme";

        var result = _service.Save(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.BackupPath, Is.Not.Null);
            Assert.That(File.Exists(result.Value.BackupPath), Is.True);
            Assert.That(File.ReadAllText(result.Value.BackupPath!), Is.EqualTo(original));
            Assert.That(result.Value.PlaylistCount, Is.EqualTo(12));
            Assert.That(result.Value.TrackCount, Is.EqualTo(37));
        });

        var (saved, diagnostics) = SoundtrackPlaylistFormat.Parse(File.ReadAllText(file));
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(saved, Has.Count.EqualTo(12));
            Assert.That(saved[0].PlayBefore, Is.EqualTo("Menu theme"));
        });
    }

    [Test]
    public void 首次保存不备份并创建Playlists目录()
    {
        using var game = new TempGameDirectory();
        var document = _service.Load(game.Path);
        document.Playlists.Add(new SoundtrackPlaylist { Name = "Menu" });

        var result = _service.Save(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.BackupPath, Is.Null);
            Assert.That(File.Exists(result.Value.FilePath), Is.True);
            Assert.That(Directory.Exists(game.Combine("Playlists")), Is.True);
        });
    }

    [Test]
    public void 保存拒绝空名字()
    {
        using var game = new TempGameDirectory();
        var document = _service.Load(game.Path);
        document.Playlists.Add(new SoundtrackPlaylist { Name = "   " });

        var result = _service.Save(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("没有名字"));
            Assert.That(File.Exists(document.FilePath), Is.False);
        });
    }

    [Test]
    public void 保存拒绝重名()
    {
        using var game = new TempGameDirectory();
        var document = _service.Load(game.Path);
        document.Playlists.Add(new SoundtrackPlaylist { Name = "Menu" });
        document.Playlists.Add(new SoundtrackPlaylist { Name = "Menu" });

        var result = _service.Save(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("重复"));
        });
    }

    [Test]
    public void 保存拒绝实例目录外的路径()
    {
        using var game = new TempGameDirectory();
        using var other = new TempGameDirectory();
        var document = _service.Load(game.Path);
        document.Playlists.Add(new SoundtrackPlaylist { Name = "Menu" });
        document.FilePath = other.Combine("Playlists", "playlists.cfg");

        var result = _service.Save(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不在实例目录"));
            Assert.That(File.Exists(document.FilePath), Is.False);
        });
    }

    [Test]
    public void 保存拒绝空实例目录()
    {
        var document = new SoundtrackDocument { GameDir = string.Empty };
        document.Playlists.Add(new SoundtrackPlaylist { Name = "Menu" });

        var result = _service.Save(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("没有选择实例"));
        });
    }

    [Test]
    public void 空文档保存返回失败而不是抛异常()
    {
        var result = _service.Save(null!);

        // 契约写的是“不抛”：空文档也要走 OperationResult，界面才能统一按失败文案提示。
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.Not.Empty);
            Assert.That(() => _service.ResolveTracks(null!), Throws.Nothing);
        });
    }

    [Test]
    public void 保存后没有BOM并按探测到的换行写出()
    {
        using var game = new TempGameDirectory();
        var file = game.WritePlaylists(SoundtrackFixtures.SampleText, newLine: "\n");
        var document = _service.Load(game.Path);

        var result = _service.Save(document);

        var bytes = File.ReadAllBytes(file);
        var text = File.ReadAllText(file);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(bytes.Length, Is.GreaterThan(3));
            Assert.That(bytes[0], Is.Not.EqualTo(0xEF));
            Assert.That(bytes[1], Is.Not.EqualTo(0xBB));
            Assert.That(bytes[2], Is.Not.EqualTo(0xBF));
            Assert.That(text, Does.Not.Contain("\r"));
            Assert.That(text, Does.EndWith("}\n"));
        });
    }

    [Test]
    public void 连续保存两次保留两份备份()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists(SoundtrackFixtures.SampleText);
        var document = _service.Load(game.Path);

        var first = _service.Save(document);
        var second = _service.Save(document);
        var backups = Directory.GetFiles(game.Combine("Playlists"), "playlists.cfg.bak-*");

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True, first.Message);
            Assert.That(second.IsSuccess, Is.True, second.Message);
            // 同一秒内连按两次保存时第二份备份要带 -1 后缀，不能把第一份覆盖掉。
            Assert.That(backups, Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void 保存保留未知子节点()
    {
        using var game = new TempGameDirectory();
        game.WritePlaylists("""
            playlist
            {
                name = Menu
                fadeSettings
                {
                    fadeIn = 2
                }
            }
            """);
        var document = _service.Load(game.Path);

        var result = _service.Save(document);
        var text = File.ReadAllText(document.FilePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(text, Does.Contain("fadeSettings"));
            Assert.That(text, Does.Contain("fadeIn = 2"));
            Assert.That(SoundtrackPlaylistFormat.Parse(text).Playlists[0].UnknownNodes, Has.Count.EqualTo(1));
        });
    }
}
