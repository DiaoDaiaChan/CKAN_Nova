using CkanUI.Plugin.Performance.Telemetry;
using NUnit.Framework;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>
/// 探针状态机（契约 §1 的"三段判定"）：Mod 没装 / 没数据 / 陈旧 / 在线 / 不兼容 必须分得开。
/// </summary>
/// <remarks>
/// 判定错一档，用户在界面上看到的话就是错的：把"游戏没开"说成"插件坏了"，或者把"数据三分钟没动"
/// 说成"实时 60 fps"。所以这里逐个状态钉死，而不是只测"能读出快照"。
/// </remarks>
[TestFixture]
public class KppTelemetryClientTests
{
    [Test]
    public void 没选实例是NoInstance()
    {
        using var client = new KppTelemetryClient(null);
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.NoInstance));
            Assert.That(client.Latest.Snapshot, Is.Null);
        });
    }

    [Test]
    public void 目录不存在是ModMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "kpp-missing-" + Guid.NewGuid().ToString("N"));
        using var client = new KppTelemetryClient(missing);
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.ModMissing));
            Assert.That(client.Latest.Detail, Does.Contain(missing));
        });
    }

    [Test]
    public void 找到KPP本体但没有Telemetry是ModOutdated()
    {
        // 旧版 KPP（不带文件桥）装在 GameData/KSPPerformanceProfiler，只有 Plugins/，没有 Telemetry/。
        var root = Path.Combine(Path.GetTempPath(), "kpp-oldmod-" + Guid.NewGuid().ToString("N"));
        var modDir = Path.Combine(root, "GameData", "KSPPerformanceProfiler");
        Directory.CreateDirectory(Path.Combine(modDir, "Plugins"));
        try
        {
            using var client = new KppTelemetryClient(Path.Combine(modDir, "Telemetry"));
            client.Poll();

            Assert.Multiple(() =>
            {
                // 「没装」和「装的是旧版」要分开：前者去装，后者去升级。
                Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.ModOutdated));
                Assert.That(client.Latest.Detail, Does.Contain(modDir));
                Assert.That(client.Latest.Snapshot, Is.Null);
            });
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清不掉不影响断言，留给系统清理。
            }
        }
    }

    [Test]
    public void 目录在但没有latest是NoData()
    {
        using var dir = new TempTelemetryDirectory();
        using var client = new KppTelemetryClient(dir.Path);
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.NoData));
            Assert.That(client.Latest.Snapshot, Is.Null);
        });
    }

    [Test]
    public void 新鲜数据是Online()
    {
        using var dir = new TempTelemetryDirectory();
        dir.WriteLatest(TelemetryFixtures.LatestAgedBy(0));

        using var client = new KppTelemetryClient(dir.Path);
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.Online));
            Assert.That(client.Latest.Snapshot!.Seq, Is.EqualTo(4821));
            Assert.That(client.Latest.AgeMs, Is.Not.Null.And.LessThan(KppTelemetryClient.StaleMultiplier * 1000));
        });
    }

    [Test]
    public void 超过三倍发布周期是Stale但仍给最后一份数据()
    {
        using var dir = new TempTelemetryDirectory();
        dir.WriteLatest(TelemetryFixtures.LatestAgedBy(60_000));

        using var client = new KppTelemetryClient(dir.Path);
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.Stale));
            // 陈旧不等于没有数据：界面要能继续显示"最后一份"，只是打个过期标记。
            Assert.That(client.Latest.Snapshot, Is.Not.Null);
            Assert.That(client.Latest.Detail, Does.Contain("陈旧"));
        });
    }

    [Test]
    public void 写方停止写入时判Stale并说明原因()
    {
        using var dir = new TempTelemetryDirectory();
        dir.WriteLatest(TelemetryFixtures.LatestAgedBy(0));
        dir.WriteStatus(TelemetryFixtures.StatusWithWriterAlive(false));

        using var client = new KppTelemetryClient(dir.Path);
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.Stale));
            Assert.That(client.Latest.Detail, Does.Contain("已停止"));
            Assert.That(client.Latest.Status!.WriterAlive, Is.False);
        });
    }

    [Test]
    public void 读到半个文件时保留上一份好数据()
    {
        using var dir = new TempTelemetryDirectory();
        dir.WriteLatest(TelemetryFixtures.LatestAgedBy(0));

        using var client = new KppTelemetryClient(dir.Path);
        client.Poll();
        Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.Online));

        // 模拟写方正在覆盖（或被外部工具截断）：这一轮解析必然失败，但不许把界面清空。
        dir.WriteLatest("{\"schema\":\"kpp.telemetry/1\",\"seq\":48");
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.Snapshot, Is.Not.Null);
            Assert.That(client.Latest.Snapshot!.Seq, Is.EqualTo(4821));
            Assert.That(client.Latest.State, Is.EqualTo(KppTelemetryState.Online));
        });
    }

    [Test]
    public void 主版本不符时判Incompatible且不解析新数据()
    {
        using var dir = new TempTelemetryDirectory();
        dir.WriteLatest(TelemetryFixtures.LatestAgedBy(0));

        using var client = new KppTelemetryClient(dir.Path);
        client.Poll();

        dir.WriteLatest(TelemetryFixtures.LatestWithSchema("kpp.telemetry/2")!);
        client.Poll();

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.Incompatible));
            Assert.That(client.Latest.Detail, Does.Contain("不兼容"));
            // 只保留升级前那份：拿一份自己看不懂的 schema 去渲染比不渲染更危险。
            Assert.That(client.Latest.Snapshot!.SchemaVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public void 换实例会清掉上一实例的数据()
    {
        using var dir = new TempTelemetryDirectory();
        dir.WriteLatest(TelemetryFixtures.LatestAgedBy(0));

        using var client = new KppTelemetryClient(dir.Path);
        client.Poll();
        Assert.That(client.Latest!.Snapshot, Is.Not.Null);

        client.Retarget(null);

        Assert.Multiple(() =>
        {
            Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.NoInstance));
            Assert.That(client.Latest.Snapshot, Is.Null);
        });
    }

    [Test]
    public void 订阅者抛异常不影响轮询()
    {
        using var dir = new TempTelemetryDirectory();
        dir.WriteLatest(TelemetryFixtures.LatestAgedBy(0));

        using var client = new KppTelemetryClient(dir.Path);
        client.Updated += _ => throw new InvalidOperationException("界面订阅者炸了");

        Assert.DoesNotThrow(client.Poll);
        Assert.That(client.Latest!.State, Is.EqualTo(KppTelemetryState.Online));
    }
}
