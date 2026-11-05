using CkanUI.Plugin.Rp1.Career;
using CkanUI.Sfs;
using CkanUI.TestSupport;

namespace CkanUI.Plugin.Rp1.Tests.Support;

internal static class Rp1TestData
{
    public static SfsNode Scenario(string body)
    {
        var parsed = SfsParser.Parse("SCENARIO\n{\n" + body + "\n}\n");
        return parsed.Document?.Elements.OfType<SfsNode>().First(node => node.Name == "SCENARIO")
               ?? throw new InvalidOperationException("无法解析测试场景：" + parsed.ErrorCount);
    }

    public static Rp1ScenarioSet Set(params (string Name, SfsNode Node)[] scenarios)
    {
        var map = scenarios.ToDictionary(pair => pair.Name, pair => pair.Node, StringComparer.OrdinalIgnoreCase);
        return new Rp1ScenarioSet(5000, "1.12.5", "Test Save", map, []);
    }

    public static string SaveText(string scenarios)
        => "GAME\n{\n  UT = 5000\n  version = 1.12.5\n  Title = Test Save\n" + scenarios + "\n}\n";
}

internal sealed class TempGame : IDisposable
{
    private readonly TempWorkspace _workspace;

    public TempGame(bool withRp1 = true)
    {
        _workspace = new TempWorkspace("ckanui-rp1");
        Root = _workspace.Path;
        if (withRp1)
        {
            Directory.CreateDirectory(Path.Combine(Root, "GameData", "RP-1"));
        }
    }

    public string Root { get; }

    public string WriteSave(string name, string text)
    {
        var directory = Path.Combine(Root, "saves", name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "persistent.sfs");
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }
}
