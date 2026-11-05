using CkanUI.Plugin.Soundtrack.Playlists;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.Playlists;

/// <summary>
/// 触发器目录：页面能编辑的 17 个键与取值规范化。
/// </summary>
/// <remarks>
/// 这些用例守的是“写进 playlists.cfg 的东西游戏能不能读”：
/// Soundtrack 的枚举解析只认 | 和 &，还把不认识的枚举当异常；
/// 一旦写错，整份文件会静默回落到原版播放列表，玩家只会觉得“我的音乐没了”。
/// </remarks>
[TestFixture]
public sealed class SoundtrackTriggerCatalogTests
{
    [Test]
    public void 目录覆盖全部十七个触发器键()
    {
        var keys = SoundtrackTriggerCatalog.Fields.Select(field => field.Key).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(keys, Has.Length.EqualTo(17));
            Assert.That(keys, Is.Unique);
            Assert.That(keys, Does.Contain("minVesselDistance"), "Persistor 把这个键读成 minAltitude，但写出时仍是 minVesselDistance。");
            Assert.That(keys, Does.Contain("bodyName"));
            Assert.That(SoundtrackTriggerCatalog.Find("SCENE")!.Key, Is.EqualTo("scene"), "键名按文件里的写法匹配，不该区分大小写。");
            Assert.That(SoundtrackTriggerCatalog.Find("nope"), Is.Null);
        });
    }

    [Test]
    public void 多选枚举按文件写法规范化()
    {
        var field = SoundtrackTriggerCatalog.Find("scene")!;

        Assert.Multiple(() =>
        {
            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "vab|sph", out var normalized, out _), Is.True);
            Assert.That(normalized, Is.EqualTo("VAB | SPH"));

            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "VAB, SPH, VAB", out normalized, out _), Is.True);
            Assert.That(normalized, Is.EqualTo("VAB | SPH"), "逗号是旧编辑器的写法，重复值只留一次。");

            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "VAB | DANCING", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("DANCING"));
            Assert.That(error, Does.Contain("可选："));

            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "  ", out _, out error), Is.False);
            Assert.That(error, Does.Contain("至少要选一个值"));
        });
    }

    [Test]
    public void 布尔只收True或False()
    {
        var field = SoundtrackTriggerCatalog.Find("inAtmosphere")!;

        Assert.Multiple(() =>
        {
            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, " true ", out var normalized, out _), Is.True);
            Assert.That(normalized, Is.EqualTo("True"));

            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "1", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("True 或 False"));
        });
    }

    [Test]
    public void 数字用不变文化且拒绝无穷()
    {
        var field = SoundtrackTriggerCatalog.Find("maxAltitude")!;

        Assert.Multiple(() =>
        {
            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, " 70000 ", out var normalized, out _), Is.True);
            Assert.That(normalized, Is.EqualTo("70000"));

            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "1000.5", out normalized, out _), Is.True);
            Assert.That(normalized, Is.EqualTo("1000.5"));

            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "1,5", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("小数点用 ."));

            Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "Infinity", out _, out _), Is.False);
        });
    }

    [Test]
    public void 天体名只做去空格不校验()
    {
        var field = SoundtrackTriggerCatalog.Find("bodyName")!;

        Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "  Kerbin ", out var normalized, out _), Is.True);
        Assert.That(normalized, Is.EqualTo("Kerbin"));

        Assert.That(SoundtrackTriggerCatalog.TryNormalize(field, "   ", out normalized, out _), Is.True);
        Assert.That(normalized, Is.Empty, "空串在 Soundtrack 里表示任意星球。");
    }

    [Test]
    public void 未知键不参与校验()
    {
        Assert.That(SoundtrackTriggerCatalog.TryNormalize(null, "anything", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("不认识这个键"));
    }
}
