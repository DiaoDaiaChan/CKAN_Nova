using System.Globalization;
using System.Text.Json;
using CkanUI.Plugin.Performance.Telemetry;
using NUnit.Framework;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>
/// 命令桥的写方（契约 §5）：命令文件形状、ack 往返、超时自清、失败必可读。
/// </summary>
/// <remarks>
/// 这层的失败在真机上极难复现（要开游戏、要卡帧、要玩家连点），所以只能靠"自己扮演 KPP"把它钉住：
/// 测试替 KPP 写出 ack，验证客户端拿到的是可显示的结果，而不是静默的成功或静默的失败。
/// </remarks>
[TestFixture]
public class KppCommandClientTests
{
    [Test]
    public async Task 命令文件形状符合契约且ack能读回()
    {
        using var dir = new TempTelemetryDirectory();
        var client = new KppCommandClient(dir.Path);

        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["megabytes"] = 4096,
        };

        var send = client.SendAsync("set_pad", args, TimeSpan.FromSeconds(10));
        var commandPath = await WaitForCommandFileAsync(dir.CommandsDirectory);

        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(commandPath)))
        {
            var root = document.RootElement;
            var id = root.GetProperty("id").GetString();
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kpp.command/1"));
                Assert.That(root.GetProperty("op").GetString(), Is.EqualTo("set_pad"));
                Assert.That(root.GetProperty("issuedUnixMs").GetInt64(), Is.GreaterThan(0));
                Assert.That(root.GetProperty("args").GetProperty("megabytes").GetInt32(), Is.EqualTo(4096));
                Assert.That(id, Is.Not.Null.And.Not.Empty);
            });

            // 文件名格式 {seq:D8}-{id}.json：按名字排序即按发出顺序（重启后仍然成立）。
            var fileName = Path.GetFileName(commandPath);
            Assert.Multiple(() =>
            {
                Assert.That(fileName, Does.Match("^[0-9]{8}-" + id + "\\.json$"));
            });

            WriteAck(dir, id!, "set_pad", ok: true, "已垫高 4096 MB");
        }

        var ack = await send;
        Assert.Multiple(() =>
        {
            Assert.That(ack.Ok, Is.True);
            Assert.That(ack.Op, Is.EqualTo("set_pad"));
            Assert.That(ack.Message, Is.EqualTo("已垫高 4096 MB"));
        });
    }

    [Test]
    public async Task 没有ack时超时并自己清掉命令文件()
    {
        using var dir = new TempTelemetryDirectory();
        var client = new KppCommandClient(dir.Path);

        var ack = await client.SendAsync("ping", timeout: TimeSpan.FromMilliseconds(400));

        Assert.Multiple(() =>
        {
            Assert.That(ack.Ok, Is.False);
            Assert.That(ack.Message, Does.Contain("超时"));
            // 自己写的命令自己收：KPP 下次启动不该被一堆过期命令淹掉。
            Assert.That(Directory.EnumerateFiles(dir.CommandsDirectory, "*.json").ToArray(), Is.Empty);
        });
    }

    [Test]
    public async Task KPP回执失败时把可读原因透出来()
    {
        using var dir = new TempTelemetryDirectory();
        var client = new KppCommandClient(dir.Path);

        var send = client.SendAsync("set_capture", timeout: TimeSpan.FromSeconds(10));
        var commandPath = await WaitForCommandFileAsync(dir.CommandsDirectory);

        string id;
        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(commandPath)))
        {
            id = document.RootElement.GetProperty("id").GetString()!;
        }

        WriteAck(dir, id, "set_capture", ok: false, "thresholdPct 是编译期常量，本版无法运行时修改");

        var ack = await send;
        Assert.Multiple(() =>
        {
            Assert.That(ack.Ok, Is.False);
            Assert.That(ack.Message, Does.Contain("编译期常量"));
        });
    }

    [Test]
    public async Task 没选实例时立刻失败而不是干等()
    {
        var client = new KppCommandClient(null);

        var ack = await client.SendAsync("ping", timeout: TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(ack.Ok, Is.False);
            Assert.That(ack.Message, Does.Contain("未选择 KSP 实例"));
        });
    }

    [Test]
    public void 清理过期命令只删超过时限的()
    {
        using var dir = new TempTelemetryDirectory();
        Directory.CreateDirectory(dir.CommandsDirectory);

        var old = Path.Combine(dir.CommandsDirectory, "00000001-old.json");
        var fresh = Path.Combine(dir.CommandsDirectory, "00000002-fresh.json");
        File.WriteAllText(old, "{}");
        File.WriteAllText(fresh, "{}");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));

        var client = new KppCommandClient(dir.Path);
        client.PruneStaleCommands(TimeSpan.FromMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(old), Is.False);
            Assert.That(File.Exists(fresh), Is.True);
        });
    }

    private static void WriteAck(TempTelemetryDirectory dir, string id, string op, bool ok, string message)
    {
        Directory.CreateDirectory(dir.AcksDirectory);
        var json = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"schema\":\"kpp.command/1\",\"id\":\"{id}\",\"op\":\"{op}\",\"ok\":{(ok ? "true" : "false")},\"completedUnixMs\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"message\":\"{message}\"}}");
        File.WriteAllText(Path.Combine(dir.AcksDirectory, id + ".json"), json);
    }

    private static async Task<string> WaitForCommandFileAsync(string commandsDirectory)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(commandsDirectory))
            {
                var file = Directory.EnumerateFiles(commandsDirectory, "*.json").FirstOrDefault();
                if (file is not null)
                {
                    return file;
                }
            }

            await Task.Delay(20);
        }

        throw new AssertionException("5 秒内没有写出命令文件（SendAsync 是否卡在别处了？）");
    }
}
