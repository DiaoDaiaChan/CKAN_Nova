using CkanUI.Plugin.Performance.Telemetry;
using NUnit.Framework;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>
/// latest.json / status.json 的解析纪律（契约 §3、§6）。
/// </summary>
/// <remarks>
/// 这组用例的价值不在"能不能读出数字"，而在三条不能破的纪律：
/// 缺字段必须是 null 而不是 0；GPU 段 valid=false 时 ms 必须被纠正成 null；
/// schema 主版本不符时只能拒绝，不能猜着解析。
/// </remarks>
[TestFixture]
public class KppTelemetrySnapshotTests
{
    [Test]
    public void 契约样例能完整解析出各段()
    {
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestJson, out var snapshot, out var error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.Schema, Is.EqualTo("kpp.telemetry/1"));
            Assert.That(snapshot.SchemaVersion, Is.EqualTo(1));
            Assert.That(snapshot.IsCompatible, Is.True);
            Assert.That(snapshot.Seq, Is.EqualTo(4821));
            Assert.That(snapshot.Writer!.Pid, Is.EqualTo(24188));
            Assert.That(snapshot.Writer.KspVersion, Is.EqualTo("1.12.5"));
            Assert.That(snapshot.Session!.FrameCount, Is.EqualTo(61234));
            Assert.That(snapshot.Context!.PartCount, Is.EqualTo(187));
            Assert.That(snapshot.Frame!.OnePercentLowFps, Is.EqualTo(31.2));
            Assert.That(snapshot.Frame.PhysSteps, Is.EqualTo(4));
            Assert.That(snapshot.Breakdown!.TufxPostFx, Is.EqualTo(0.94));
            Assert.That(snapshot.Gpu!.Available, Is.True);
            Assert.That(snapshot.Gpu.Stages, Has.Count.EqualTo(5));
            Assert.That(snapshot.GpuBrackets, Has.Count.EqualTo(2));
            Assert.That(snapshot.GpuBrackets[0].Owner, Is.EqualTo("Scatterer"));
            Assert.That(snapshot.Memory!.Padder!.HeldMb, Is.EqualTo(4096d));
            Assert.That(snapshot.Memory.Probe!.AssetsScanned, Is.EqualTo(14320));
            Assert.That(snapshot.Memory.TopAllocators, Has.Count.EqualTo(2));
            Assert.That(snapshot.Hotspots!.Plugins, Has.Count.EqualTo(2));
            Assert.That(snapshot.Hotspots.Modules[0].Friendly, Is.EqualTo("太阳能板"));
            Assert.That(snapshot.Hotspots.Assemblies, Has.Count.EqualTo(3));
            Assert.That(snapshot.Timeline!.Frames, Has.Count.EqualTo(3));
            Assert.That(snapshot.Diagnostics!.Severity, Is.EqualTo("warn"));
            Assert.That(snapshot.Spikes, Has.Count.EqualTo(1));
            Assert.That(snapshot.Counters!.GcCount, Is.EqualTo(271));
        });
    }

    [Test]
    public void GPU段valid为false时ms必须是null而不是0()
    {
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestJson, out var snapshot, out _), Is.True);

        var final = snapshot!.Gpu!.Stages.Single(stage => stage.Key == "final");

        Assert.Multiple(() =>
        {
            // 写方给的 ms 是 null 且 valid=false：读方不许"修正"成 0，否则 UI 会显示一段不存在的耗时。
            Assert.That(final.Valid, Is.False);
            Assert.That(final.Ms, Is.Null);
            Assert.That(snapshot.Gpu.Stages[0].Ms, Is.EqualTo(1.42));
        });
    }

    [Test]
    public void 写方违约给出valid为false但带ms时读方要纠正成null()
    {
        // 防的是"写方哪天填错"：契约 §3.1 的纪律必须由读方兜住，不能指望对面永远对。
        var json = TelemetryFixtures.LatestJson.Replace(
            "\"key\": \"depth\",\n        \"ms\": 1.42,\n        \"valid\": true",
            "\"key\": \"depth\",\n        \"ms\": 7.5,\n        \"valid\": false",
            StringComparison.Ordinal);

        Assert.That(json, Does.Not.EqualTo(TelemetryFixtures.LatestJson), "夹具被改过，替换没命中——先修这个用例");
        Assert.That(KppTelemetrySnapshot.TryParse(json, out var snapshot, out _), Is.True);

        var depth = snapshot!.Gpu!.Stages[0];
        Assert.Multiple(() =>
        {
            Assert.That(depth.Valid, Is.False);
            Assert.That(depth.Ms, Is.Null);
        });
    }

    [Test]
    public void 时间线的gc是布尔标记()
    {
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestJson, out var snapshot, out _), Is.True);

        var frames = snapshot!.Timeline!.Frames;
        Assert.Multiple(() =>
        {
            Assert.That(frames[0].Gc, Is.False);
            Assert.That(frames[1].Gc, Is.True);
            Assert.That(frames[1].FrameMs, Is.EqualTo(39.6));
        });
    }

    [Test]
    public void 尖峰归因条目与热点条目同构()
    {
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestJson, out var snapshot, out _), Is.True);

        var spike = snapshot!.Spikes[0];
        Assert.Multiple(() =>
        {
            Assert.That(spike.Frame, Is.EqualTo(61230));
            Assert.That(spike.Culprits, Has.Count.EqualTo(1));
            Assert.That(spike.Culprits[0].Name, Is.EqualTo("Scatterer.OnPreCull"));
            Assert.That(spike.Culprits[0].Ms, Is.EqualTo(9.4));
            Assert.That(spike.Culprits[0].Assembly, Is.EqualTo("Scatterer"));
        });
    }

    [Test]
    public void 检查项的value是可直接显示的字符串()
    {
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestJson, out var snapshot, out _), Is.True);

        var checks = snapshot!.Diagnostics!.Checks;
        Assert.Multiple(() =>
        {
            Assert.That(checks, Has.Count.EqualTo(3));
            Assert.That(checks[0].Value, Is.EqualTo("93.4%"));
            Assert.That(checks[1].State, Is.EqualTo("warn"));
            // 不可用的检查项：state=unknown 且 value=null，读方要画灰条而不是绿色。
            Assert.That(checks[2].State, Is.EqualTo("unknown"));
            Assert.That(checks[2].Value, Is.Null);
        });
    }

    [Test]
    public void 只有schema的最小文件不能补默认值()
    {
        Assert.That(KppTelemetrySnapshot.TryParse("{\"schema\":\"kpp.telemetry/1\"}", out var snapshot, out var error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.Frame, Is.Null);
            Assert.That(snapshot.Gpu, Is.Null);
            Assert.That(snapshot.Memory, Is.Null);
            Assert.That(snapshot.GpuBrackets, Is.Empty);
            Assert.That(snapshot.Spikes, Is.Empty);
            // 发布周期缺失时按契约默认 1000ms 判定陈旧，但字段本身仍然是 null（UI 显示 N/A）。
            Assert.That(snapshot.PublishIntervalMs, Is.Null);
            Assert.That(snapshot.EffectivePublishIntervalMs, Is.EqualTo(1000d));
        });
    }

    [Test]
    public void 更高的次版本仍然兼容()
    {
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestWithSchema("kpp.telemetry/1.3"), out var snapshot, out var error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.SchemaVersion, Is.EqualTo(1));
            Assert.That(snapshot.IsCompatible, Is.True);
        });
    }

    [Test]
    public void 主版本不符时解析成功但判定为不兼容()
    {
        // 解析成功 + IsCompatible false：这样 UI 能区分"文件坏了"和"契约升级了"。
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestWithSchema("kpp.telemetry/2"), out var snapshot, out var error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.SchemaVersion, Is.EqualTo(2));
            Assert.That(snapshot.IsCompatible, Is.False);
        });
    }

    [Test]
    public void 别的schema家族直接拒绝()
    {
        Assert.That(KppTelemetrySnapshot.TryParse(TelemetryFixtures.LatestWithSchema("kpp.other/1"), out var snapshot, out var error), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Null);
            Assert.That(error, Does.Contain("schema").IgnoreCase);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("{ half")]
    [TestCase("[1,2,3]")]
    public void 半个文件或非法json都不抛异常(string json)
    {
        var parsed = true;
        string? error = null;

        Assert.DoesNotThrow(() => parsed = KppTelemetrySnapshot.TryParse(json, out _, out error));
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void status样例的桥健康度可解析()
    {
        Assert.That(KppStatus.TryParse(TelemetryFixtures.StatusJson, out var status, out var error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(status!.WriterAlive, Is.True);
            Assert.That(status.Pid, Is.EqualTo(24188));
            Assert.That(status.WriteFailures, Is.Zero);
            Assert.That(status.IsCompatible, Is.True);
        });
    }
}
