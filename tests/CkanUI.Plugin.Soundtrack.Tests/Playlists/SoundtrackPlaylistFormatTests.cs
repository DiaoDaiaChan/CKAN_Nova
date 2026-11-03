using CkanUI.Plugin.Soundtrack.Playlists;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.Playlists;

/// <summary>
/// playlists.cfg 的解析与规范化写出。
/// </summary>
/// <remarks>
/// 这些用例守的是“保存一次不能丢玩家配置”：Soundtrack 自己写出来的文件很规整，但玩家的文件会被手改、
/// 被别的模组改写、被跨平台编辑器的行尾污染。解析器把不认识的键和节点原样留下，写出时按固定顺序重排，
/// 所以每条用例都同时看“读进来对不对”和“写出去再读回来还一样吗”。
/// </remarks>
[TestFixture]
public sealed class SoundtrackPlaylistFormatTests
{
    [Test]
    public void 契约样例解析出12个列表和37首曲目()
    {
        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(SoundtrackFixtures.SampleText);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(playlists, Has.Count.EqualTo(12));
            Assert.That(playlists.Sum(playlist => playlist.Tracks.Count), Is.EqualTo(37));
            Assert.That(playlists[0].Name, Is.EqualTo("Construction"));
            Assert.That(playlists[0].Tracks, Has.Count.EqualTo(6));
            Assert.That(playlists[7].Name, Is.EqualTo("Space"));
            Assert.That(playlists[7].Tracks, Has.Count.EqualTo(21));
            Assert.That(playlists[11].Name, Is.EqualTo("Space Center Ambience Night"));
        });
    }

    [Test]
    public void 契约样例往返后语义不变()
    {
        var (first, _) = SoundtrackPlaylistFormat.Parse(SoundtrackFixtures.SampleText);
        var text = SoundtrackPlaylistFormat.Format(first, "\n");
        var (second, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(
                SoundtrackFixtures.DescribeAll(second),
                Is.EqualTo(SoundtrackFixtures.DescribeAll(first)));
        });
    }

    [Test]
    public void 契约样例的playWhen保持原始顺序与原始值()
    {
        var (playlists, _) = SoundtrackPlaylistFormat.Parse(SoundtrackFixtures.SampleText);

        Assert.Multiple(() =>
        {
            Assert.That(
                playlists[0].PlayWhen,
                Is.EqualTo(new[] { new KeyValuePair<string, string>("scene", "VAB | SPH") }));
            Assert.That(playlists[11].PlayWhen.Select(pair => pair.Key), Is.EqualTo(new[] { "timeOfDay", "scene" }));
            Assert.That(playlists[11].PlayWhen[0].Value, Is.EqualTo("NightAM | TwilightPM | NightPM"));
            Assert.That(playlists[11].SceneSummary, Does.Contain("NightAM"));
        });
    }

    [Test]
    public void 契约样例没写enabled时保持null()
    {
        var (playlists, _) = SoundtrackPlaylistFormat.Parse(SoundtrackFixtures.SampleText);

        // “没写 enabled”和“写了 enabled = False”在 Soundtrack 里语义不同（前者用模组默认值），
        // 保存时不能擅自补一个 False 进去。
        Assert.That(playlists.Select(playlist => playlist.Enabled), Is.All.Null);
    }

    [Test]
    public void enabled显式写出时解析为布尔值()
    {
        const string text = """
            playlist
            {
                name = Menu
                enabled = False
                loop = True
            }
            playlist
            {
                name = Flight
                enabled = true
            }
            """;

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(playlists[0].Enabled, Is.False);
            Assert.That(playlists[1].Enabled, Is.True);
        });
    }

    [Test]
    public void 重复键后者覆盖且不抛异常()
    {
        // 样例里 preloadTime 就出现了两次（Soundtrack 自己的写入器留下的），解析绝不能炸。
        const string text = """
            playlist
            {
                name = Menu
                preloadTime = 5
                loop = True
                preloadTime = 7.5
                channel = 1
                channel = 2
            }
            """;

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(playlists, Has.Count.EqualTo(1));
            Assert.That(playlists[0].PreloadTime, Is.EqualTo(7.5));
            Assert.That(playlists[0].Channel, Is.EqualTo(2));
        });
    }

    [Test]
    public void 未知标量键按出现顺序保留并写回()
    {
        const string text = """
            playlist
            {
                name = Menu
                fadeIn = 1.5
                customTag = night
                loop = False
            }
            """;

        var (playlists, _) = SoundtrackPlaylistFormat.Parse(text);
        var formatted = SoundtrackPlaylistFormat.Format(playlists, "\n");
        var (again, _) = SoundtrackPlaylistFormat.Parse(formatted);

        Assert.Multiple(() =>
        {
            Assert.That(playlists[0].ExtraValues.Select(pair => pair.Key), Is.EqualTo(new[] { "fadeIn", "customTag" }));
            Assert.That(playlists[0].ExtraValues[0].Value, Is.EqualTo("1.5"));
            Assert.That(playlists[0].Loop, Is.False);
            Assert.That(
                again[0].ExtraValues.Select(pair => pair.Key + "=" + pair.Value),
                Is.EqualTo(new[] { "fadeIn=1.5", "customTag=night" }));
        });
    }

    [Test]
    public void 未知子节点整段保留并可写回()
    {
        const string text = """
            playlist
            {
                name = Menu
                fadeSettings
                {
                    fadeIn = 2
                    nested
                    {
                        depth = 1
                    }
                }
                tracks
                {
                    track = Alpha
                }
            }
            """;

        var (playlists, _) = SoundtrackPlaylistFormat.Parse(text);
        var formatted = SoundtrackPlaylistFormat.Format(playlists, "\n");
        var (again, _) = SoundtrackPlaylistFormat.Parse(formatted);

        Assert.Multiple(() =>
        {
            Assert.That(playlists[0].UnknownNodes, Has.Count.EqualTo(1));
            Assert.That(playlists[0].UnknownNodes[0], Does.Contain("fadeSettings"));
            Assert.That(playlists[0].UnknownNodes[0], Does.Contain("fadeIn = 2"));
            Assert.That(playlists[0].UnknownNodes[0], Does.Contain("depth = 1"));
            Assert.That(playlists[0].UnknownNodes[0], Does.StartWith("{").Or.StartWith("fadeSettings"));
            Assert.That(again[0].UnknownNodes, Has.Count.EqualTo(1));
            Assert.That(again[0].UnknownNodes[0], Does.Contain("nested"));
            Assert.That(again[0].Tracks.Select(track => track.Name), Is.EqualTo(new[] { "Alpha" }));
        });
    }

    [Test]
    public void 缺name的节点跳过并记诊断()
    {
        const string text = """
            playlist
            {
                loop = True
                tracks
                {
                    track = Alpha
                }
            }
            playlist
            {
                name = Good
            }
            """;

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(playlists, Has.Count.EqualTo(1));
            Assert.That(playlists[0].Name, Is.EqualTo("Good"));
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics[0], Does.Contain("name"));
        });
    }

    [Test]
    public void 引号值去掉外层引号且保留内部空格()
    {
        const string text = """
            playlist
            {
                name = "Menu Theme"
                playNext = "Admin Facility"
                playAfter = ""
            }
            """;

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(playlists[0].Name, Is.EqualTo("Menu Theme"));
            Assert.That(playlists[0].PlayNext, Is.EqualTo("Admin Facility"));
            Assert.That(playlists[0].PlayAfter, Is.Empty);
        });
    }

    [Test]
    public void 注释行被忽略但值里的双斜杠保留()
    {
        const string text = """
            // 顶部注释
            playlist
            {
                // 列表内的注释
                name = Menu
                playAfter = Path//Like
            }
            """;

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(playlists, Has.Count.EqualTo(1));
            Assert.That(playlists[0].PlayAfter, Is.EqualTo("Path//Like"));
        });
    }

    [Test]
    public void 行尾空白与空行不影响解析()
    {
        const string text = "playlist   \r\n{\r\n\tname = Menu   \r\n\r\n\tloop = True\t\r\n}\r\n\r\n";

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(playlists, Has.Count.EqualTo(1));
            Assert.That(playlists[0].Name, Is.EqualTo("Menu"));
            Assert.That(playlists[0].Loop, Is.True);
        });
    }

    [Test]
    public void LF文件与CRLF文件解析结果一致()
    {
        var (crlf, _) = SoundtrackPlaylistFormat.Parse(SoundtrackFixtures.SampleText);
        var (lf, diagnostics) = SoundtrackPlaylistFormat.Parse(SoundtrackFixtures.SampleLf);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(SoundtrackFixtures.DescribeAll(lf), Is.EqualTo(SoundtrackFixtures.DescribeAll(crlf)));
        });
    }

    [Test]
    public void 写出的键顺序固定且布尔用TrueFalse()
    {
        var playlist = new SoundtrackPlaylist
        {
            Name = "Menu",
            Enabled = false,
            Loop = true,
            Shuffle = false,
            PauseOnGamePause = true,
            DisableAfterPlay = false,
            PlayNext = "Next",
            PreloadTime = 5,
            Channel = 3,
        };
        playlist.Tracks.Add(new SoundtrackTrackRef { Name = "Alpha" });
        playlist.PlayWhen.Add(new KeyValuePair<string, string>("scene", "VAB"));

        var text = SoundtrackPlaylistFormat.Format(new[] { playlist }, "\n");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("\tname = Menu\n"));
            Assert.That(text, Does.Contain("\tenabled = False\n"));
            Assert.That(text, Does.Contain("\tloop = True\n"));
            Assert.That(text, Does.Contain("\tpreloadTime = 5\n"));
            Assert.That(text, Does.Contain("\ttracks\n\t{\n\t\ttrack = Alpha\n\t}\n"));
            Assert.That(text, Does.Contain("\tplayWhen\n\t{\n\t\tscene = VAB\n\t}\n"));
            Assert.That(text, Does.Not.Contain("\t\tname"));
            Assert.That(Order(text, "name"), Is.LessThan(Order(text, "enabled")));
            Assert.That(Order(text, "enabled"), Is.LessThan(Order(text, "preloadTime")));
            Assert.That(Order(text, "preloadTime"), Is.LessThan(Order(text, "tracks")));
            Assert.That(Order(text, "tracks"), Is.LessThan(Order(text, "playWhen")));
        });
    }

    [Test]
    public void 没写的可选键不补进文件()
    {
        var playlist = new SoundtrackPlaylist { Name = "Menu" };

        var text = SoundtrackPlaylistFormat.Format(new[] { playlist }, "\n");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("enabled"));
            Assert.That(text, Does.Not.Contain("channel"));
            Assert.That(text, Does.Not.Contain("preloadTime"));
            Assert.That(text, Does.Not.Contain("tracks"));
            Assert.That(text, Does.Not.Contain("playWhen"));
        });
    }

    [Test]
    public void 写出末尾恰好一个换行且换行符跟随调用方()
    {
        var playlist = new SoundtrackPlaylist { Name = "Menu" };

        var crlf = SoundtrackPlaylistFormat.Format(new[] { playlist }, "\r\n");
        var lf = SoundtrackPlaylistFormat.Format(new[] { playlist }, "\n");

        Assert.Multiple(() =>
        {
            Assert.That(crlf, Does.EndWith("}\r\n"));
            Assert.That(crlf, Does.Not.EndWith("\r\n\r\n"));
            Assert.That(lf, Does.Not.Contain("\r"));
            Assert.That(lf, Does.EndWith("}\n"));
            Assert.That(lf, Does.Not.EndWith("\n\n"));
        });
    }

    [Test]
    public void 数字用不变文化写出()
    {
        var playlist = new SoundtrackPlaylist { Name = "Menu", PreloadTime = 1.25 };

        var text = SoundtrackPlaylistFormat.Format(new[] { playlist }, "\n");

        Assert.That(text, Does.Contain("preloadTime = 1.25"));
    }

    [Test]
    public void 非法布尔值保留默认并记诊断()
    {
        const string text = """
            playlist
            {
                name = Menu
                enabled = Yes
            }
            """;

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(playlists[0].Enabled, Is.Null);
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics[0], Does.Contain("True"));
        });
    }

    [Test]
    public void 空曲目名跳过并记诊断()
    {
        const string text = """
            playlist
            {
                name = Menu
                tracks
                {
                    track =
                    track = Alpha
                }
            }
            """;

        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(playlists[0].Tracks.Select(track => track.Name), Is.EqualTo(new[] { "Alpha" }));
            Assert.That(diagnostics, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void 空文本返回空结果()
    {
        var (playlists, diagnostics) = SoundtrackPlaylistFormat.Parse("   \r\n\r\n");

        Assert.Multiple(() =>
        {
            Assert.That(playlists, Is.Empty);
            Assert.That(diagnostics, Is.Empty);
        });
    }

    [Test]
    public void Format拒绝空集合参数()
    {
        Assert.Throws<ArgumentNullException>(() => SoundtrackPlaylistFormat.Format(null!, "\n"));
    }

    private static int Order(string text, string key) =>
        text.IndexOf("\t" + key + " =", StringComparison.Ordinal) is var index && index >= 0
            ? index
            : text.IndexOf("\t" + key + "\n", StringComparison.Ordinal);
}
