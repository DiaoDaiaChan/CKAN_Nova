using CkanUI.Plugin.Soundtrack.Playlists;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.Playlists;

/// <summary>
/// playWhen 触发器目录：把 cfg 里的键值翻译成人话，并按 scene 归组。
/// </summary>
/// <remarks>
/// 这一层只做翻译，不碰磁盘也不改模型，所以用例全部手写 <see cref="SoundtrackPlaylist"/>：
/// 守的是“键名别写错、默认值别当条件、看不懂的别吞掉”三件事——节点树整页的可读性都压在这里。
/// </remarks>
[TestFixture]
public sealed class SoundtrackTriggersTests
{
    [Test]
    public void 场景与时段翻译成中文并保留原始值()
    {
        var playlist = CreatePlaylist(
            ("scene", "VAB | SPH"),
            ("timeOfDay", "NightAM | TwilightPM"),
            ("inAtmosphere", "False"));

        var conditions = SoundtrackTriggers.Describe(playlist);

        Assert.Multiple(() =>
        {
            Assert.That(conditions.Select(condition => condition.Key),
                Is.EqualTo(new[] { "scene", "timeOfDay", "inAtmosphere" }));

            Assert.That(conditions[0].Label, Is.EqualTo("场景"));
            Assert.That(conditions[0].Value, Is.EqualTo("载具组装厂、航天器机库"));
            Assert.That(conditions[0].Raw, Is.EqualTo("VAB | SPH"));
            Assert.That(conditions[0].Tooltip, Is.EqualTo("scene = VAB | SPH"));

            Assert.That(conditions[1].Value, Is.EqualTo("凌晨、黄昏"));
            Assert.That(conditions[2].Value, Is.EqualTo("大气层外"));
        });
    }

    [Test]
    public void 节点行不重复展示场景()
    {
        var playlist = CreatePlaylist(("scene", "Flight"), ("paused", "True"));

        var conditions = SoundtrackTriggers.Describe(playlist, includeScene: false);

        Assert.Multiple(() =>
        {
            Assert.That(conditions.Select(condition => condition.Key), Is.EqualTo(new[] { "paused" }));
            Assert.That(conditions[0].Value, Is.EqualTo("已暂停"));
        });
    }

    [Test]
    public void 没写场景时补一条任何场景且提示没有这个键()
    {
        var playlist = CreatePlaylist(("bodyName", "Kerbin"));

        var conditions = SoundtrackTriggers.Describe(playlist);

        Assert.Multiple(() =>
        {
            Assert.That(conditions[0].Key, Is.EqualTo("scene"));
            Assert.That(conditions[0].Value, Is.EqualTo("任何场景"));
            Assert.That(conditions[0].Raw, Is.Empty);
            Assert.That(conditions[0].Tooltip, Does.Contain("没有写这个键"));
            Assert.That(conditions[1].Value, Is.EqualTo("Kerbin"));
        });
    }

    [Test]
    public void 空星球名视为任意星球()
    {
        var playlist = CreatePlaylist(("bodyName", ""));

        var condition = SoundtrackTriggers.Describe(playlist).Single(entry => entry.Key == "bodyName");

        Assert.That(condition.Value, Is.EqualTo("任何星球"));
    }

    [Test]
    public void 高度上下限合成一条范围()
    {
        var playlist = CreatePlaylist(("minAltitude", "1000"), ("maxAltitude", "5000"));

        var condition = SoundtrackTriggers.Describe(playlist).Single(entry => entry.Key == "minAltitude");

        Assert.Multiple(() =>
        {
            Assert.That(condition.Label, Is.EqualTo("高度"));
            Assert.That(condition.Value, Is.EqualTo("1 km – 5 km"));
            Assert.That(condition.Raw, Is.EqualTo("min=1000 · max=5000"));
        });
    }

    [Test]
    public void 只有上限时显示小于等于()
    {
        var playlist = CreatePlaylist(("maxAltitude", "800"));

        var condition = SoundtrackTriggers.Describe(playlist).Single(entry => entry.Key == "minAltitude");

        Assert.That(condition.Value, Is.EqualTo("≤ 800 m"));
    }

    [Test]
    public void 默认值与零下限不算条件()
    {
        var playlist = CreatePlaylist(
            ("minAltitude", float.MinValue.ToString("R")),
            ("maxAltitude", float.MaxValue.ToString("R")),
            ("minVesselDistance", "0"),
            ("maxVesselDistance", float.MaxValue.ToString("R")));

        var conditions = SoundtrackTriggers.Describe(playlist, includeScene: false);

        Assert.That(conditions, Is.Empty, "默认值应该被当成“没有限制”，不该变成一排 3.4E+38。");
    }

    [Test]
    public void 表面速度不带千位换算()
    {
        var playlist = CreatePlaylist(("minVelocitySurface", "0.5"), ("maxVelocitySurface", "120"));

        var condition = SoundtrackTriggers.Describe(playlist).Single(entry => entry.Key == "minVelocitySurface");

        Assert.Multiple(() =>
        {
            Assert.That(condition.Label, Is.EqualTo("表面速度"));
            Assert.That(condition.Value, Is.EqualTo("0.5 m/s – 120 m/s"));
        });
    }

    [Test]
    public void 逗号分隔的飞行状态也能拆开()
    {
        var playlist = CreatePlaylist(("situation", "LANDED, ORBITING"));

        var condition = SoundtrackTriggers.Describe(playlist).Single(entry => entry.Key == "situation");

        Assert.That(condition.Value, Is.EqualTo("已着陆、环绕中"));
    }

    [Test]
    public void 未知键原样列在最后()
    {
        var playlist = CreatePlaylist(
            ("surfacePressure", "0.4"),
            ("scene", "Flight"),
            ("vesselType", "Probe | Relay"));

        var conditions = SoundtrackTriggers.Describe(playlist);

        Assert.Multiple(() =>
        {
            Assert.That(conditions[^1].Key, Is.EqualTo("surfacePressure"));
            Assert.That(conditions[^1].Label, Is.EqualTo("surfacePressure"));
            Assert.That(conditions[^1].Value, Is.EqualTo("0.4"));
            Assert.That(conditions.Single(entry => entry.Key == "vesselType").Value, Is.EqualTo("探测器、中继卫星"));
        });
    }

    [Test]
    public void 同一个键写两遍时后者生效()
    {
        var playlist = CreatePlaylist(("scene", "VAB"), ("scene", "SPH"));

        var conditions = SoundtrackTriggers.Describe(playlist);

        Assert.Multiple(() =>
        {
            Assert.That(conditions.Count(entry => entry.Key == "scene"), Is.EqualTo(1));
            Assert.That(conditions[0].Value, Is.EqualTo("航天器机库"));
        });
    }

    [Test]
    public void 原始playWhen按文件顺序拼接()
    {
        var playlist = CreatePlaylist(("scene", "VAB | SPH"), ("timeOfDay", "DayAM"));

        Assert.That(SoundtrackTriggers.DescribeRaw(playlist), Is.EqualTo("scene = VAB | SPH · timeOfDay = DayAM"));
        Assert.That(SoundtrackTriggers.DescribeRaw(new SoundtrackPlaylist()), Is.Empty);
    }

    [Test]
    public void 场景分组键与顺序不随书写顺序变化()
    {
        var forward = CreatePlaylist(("scene", "VAB | SPH"));
        var reverse = CreatePlaylist(("scene", "SPH | VAB"));

        Assert.Multiple(() =>
        {
            Assert.That(SoundtrackScenes.GroupKey(forward), Is.EqualTo(SoundtrackScenes.GroupKey(reverse)));
            Assert.That(SoundtrackScenes.GroupKey(forward), Is.EqualTo("VAB|SPH"));
            Assert.That(SoundtrackScenes.GroupTitle("VAB|SPH"), Is.EqualTo("载具组装厂、航天器机库"));
            Assert.That(SoundtrackScenes.GroupSubtitle("VAB|SPH"), Is.EqualTo("VAB | SPH"));
        });
    }

    [Test]
    public void 没写场景的播放列表归到未限定分组且排在最前()
    {
        var playlist = new SoundtrackPlaylist();

        Assert.Multiple(() =>
        {
            Assert.That(SoundtrackScenes.GroupKey(playlist), Is.EqualTo(SoundtrackScenes.UnrestrictedKey));
            Assert.That(SoundtrackScenes.GroupTitle(SoundtrackScenes.UnrestrictedKey), Is.EqualTo("未限定场景"));
            Assert.That(SoundtrackScenes.GroupSubtitle(SoundtrackScenes.UnrestrictedKey), Does.Contain("未写"));
            Assert.That(SoundtrackScenes.GroupOrder(SoundtrackScenes.UnrestrictedKey),
                Is.LessThan(SoundtrackScenes.GroupOrder("MainMenu")));
            Assert.That(SoundtrackScenes.GroupOrder("Flight"), Is.GreaterThan(SoundtrackScenes.GroupOrder("VAB")));
        });
    }

    [Test]
    public void 不认识的场景枚举名原样保留并排在最后()
    {
        var playlist = CreatePlaylist(("scene", "KSPedia2 | Flight"));

        Assert.Multiple(() =>
        {
            Assert.That(SoundtrackScenes.GroupKey(playlist), Is.EqualTo("Flight|KSPedia2"));
            Assert.That(SoundtrackScenes.GroupTitle(SoundtrackScenes.GroupKey(playlist)), Is.EqualTo("飞行中、KSPedia2"));
            Assert.That(SoundtrackScenes.GroupOrder(SoundtrackScenes.GroupKey(playlist)),
                Is.EqualTo(SoundtrackScenes.GroupOrder("Flight")), "一组里有认识的场景时，按认识的那个排。");
        });
    }

    private static SoundtrackPlaylist CreatePlaylist(params (string Key, string Value)[] pairs)
    {
        var playlist = new SoundtrackPlaylist { Name = "测试列表" };
        foreach (var (key, value) in pairs)
        {
            playlist.PlayWhen.Add(new KeyValuePair<string, string>(key, value));
        }

        return playlist;
    }
}
