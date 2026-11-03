using System.Globalization;
using System.Text.Json.Nodes;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>
/// 契约样例的读取与改造（夹具来自 docs/design/performance-tab.sample，随构建拷到 Fixtures/）。
/// </summary>
internal static class TelemetryFixtures
{
    /// <summary>契约样例 latest.json 的原文。</summary>
    public static string LatestJson => Read("latest.sample.json");

    /// <summary>契约样例 status.json 的原文。</summary>
    public static string StatusJson => Read("status.sample.json");

    /// <summary>
    /// 把样例的 <c>publishedUnixMs</c> 改成相对当前的时刻，用于构造"新鲜"与"陈旧"两种情形。
    /// </summary>
    /// <param name="ageMs">发布时刻距今多少毫秒（正数 = 过去）。</param>
    public static string LatestAgedBy(double ageMs)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var node = JsonNode.Parse(LatestJson)!.AsObject();
        node["publishedUnixMs"] = now - ageMs;
        return node.ToJsonString();
    }

    /// <summary>把样例整体替换掉 schema 字段。</summary>
    /// <param name="schema">新的 schema 字符串。</param>
    public static string LatestWithSchema(string schema)
    {
        var node = JsonNode.Parse(LatestJson)!.AsObject();
        node["schema"] = schema;
        return node.ToJsonString();
    }

    /// <summary>造一份 status.json，只改 writerAlive 与 pid。</summary>
    /// <param name="writerAlive">写方是否存活。</param>
    public static string StatusWithWriterAlive(bool writerAlive)
    {
        var node = JsonNode.Parse(StatusJson)!.AsObject();
        node["writerAlive"] = writerAlive;
        return node.ToJsonString();
    }

    /// <summary>按文件名读 Fixtures 下的样例；找不到就直接失败（夹具丢了必须炸，不能静默跳过）。</summary>
    /// <param name="fileName">样例文件名。</param>
    private static string Read(string fileName)
    {
        // 必须相对"本测试程序集所在的目录"找夹具，不能用 AppContext.BaseDirectory：
        // tests/CkanUI.TestRunner 是进程内跑所有测试程序集的宿主，AppContext.BaseDirectory 指向它自己的输出目录，
        // 那里从来没有 Fixtures/。dotnet test 时两者恰好相同，所以这个坑只在 TestRunner 下暴露。
        var assemblyDirectory = Path.GetDirectoryName(typeof(TelemetryFixtures).Assembly.Location)!;
        var path = Path.Combine(assemblyDirectory, "Fixtures", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                string.Create(CultureInfo.InvariantCulture, $"缺少契约样例 {path}（docs/design/performance-tab.sample 是否还被拷到输出目录？）"),
                path);
        }

        return File.ReadAllText(path);
    }
}

/// <summary>测试用的临时遥测目录：建在 TEMP 下，用完删。</summary>
internal sealed class TempTelemetryDirectory : IDisposable
{
    /// <summary>建一个空目录。</summary>
    public TempTelemetryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "kpp-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>目录全路径。</summary>
    public string Path { get; }

    /// <summary>写 latest.json（覆盖）。</summary>
    /// <param name="json">完整 JSON 文本。</param>
    public void WriteLatest(string json) => File.WriteAllText(System.IO.Path.Combine(Path, "latest.json"), json);

    /// <summary>写 status.json（覆盖）。</summary>
    /// <param name="json">完整 JSON 文本。</param>
    public void WriteStatus(string json) => File.WriteAllText(System.IO.Path.Combine(Path, "status.json"), json);

    /// <summary>命令目录。</summary>
    public string CommandsDirectory => System.IO.Path.Combine(Path, "commands");

    /// <summary>ack 目录。</summary>
    public string AcksDirectory => System.IO.Path.Combine(Path, "commands", "acks");

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 删不掉就交给 TEMP 清理，测试不该因为收尾失败变红。
        }
    }
}
