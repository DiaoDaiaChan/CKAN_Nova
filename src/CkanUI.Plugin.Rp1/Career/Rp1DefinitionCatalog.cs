using System.Collections.Generic;
using System.Text;
using CkanUI.Sfs;

namespace CkanUI.Plugin.Rp1.Career;

/// <summary>RP-1 配置里的合同定义（CONTRACT_TYPE）。</summary>
public sealed record Rp1ContractDefinition(
    string Name,
    string? Title,
    string? Group,
    string? Synopsis,
    string? Description);

/// <summary>RP-1 配置里的里程碑定义（RP0_MILESTONE）。</summary>
public sealed record Rp1MilestoneDefinition(
    string Name,
    string? ContractName,
    string? Headline,
    string? Article,
    string? Description);

/// <summary>RP-1 配置里的项目定义（RP0_PROGRAM）。</summary>
public sealed record Rp1ProgramDefinition(
    string Name,
    string? Title,
    string? Description);

/// <summary>
/// 把 RP-1 的合同/里程碑/项目配置读成内部名到可读标题的映射。
/// 只做只读解析，解析失败不抛：缺标题时时间线显示内部名，不隐藏事件。
/// </summary>
public sealed class Rp1DefinitionCatalog
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, Rp1ContractDefinition> _contracts = new(NameComparer);
    private readonly Dictionary<string, Rp1MilestoneDefinition> _milestones = new(NameComparer);
    private readonly Dictionary<string, Rp1ProgramDefinition> _programs = new(NameComparer);
    private readonly List<string> _diagnostics = [];

    private Rp1DefinitionCatalog()
    {
    }

    /// <summary>合同定义（键是内部 name）。</summary>
    public IReadOnlyDictionary<string, Rp1ContractDefinition> Contracts => _contracts;

    /// <summary>里程碑定义（键是内部 name）。</summary>
    public IReadOnlyDictionary<string, Rp1MilestoneDefinition> Milestones => _milestones;

    /// <summary>项目定义（键是内部 name）。</summary>
    public IReadOnlyDictionary<string, Rp1ProgramDefinition> Programs => _programs;

    /// <summary>读取过程中的诊断（缺失目录/坏文件），最多保留 24 条。</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>从 &lt;gameDir&gt;/GameData/RP-1 读取三类定义。</summary>
    public static Rp1DefinitionCatalog Load(string gameDir)
    {
        var catalog = new Rp1DefinitionCatalog();
        var rp1Root = Path.Combine(gameDir, "GameData", "RP-1");
        if (!Directory.Exists(rp1Root))
        {
            catalog.AddDiagnostic("没有找到 GameData/RP-1：这个实例可能没有安装 RP-1。");
            return catalog;
        }

        var contracts = Path.Combine(rp1Root, "Contracts");
        if (Directory.Exists(contracts))
        {
            foreach (var file in Directory.EnumerateFiles(contracts, "*.cfg", SearchOption.AllDirectories))
            {
                catalog.ReadFile(file, "CONTRACT_TYPE", catalog.AddContract);
            }
        }
        else
        {
            catalog.AddDiagnostic("没有找到 GameData/RP-1/Contracts，合同会显示内部名。");
        }

        var milestones = Path.Combine(rp1Root, "Strategies", "Milestones.cfg");
        if (File.Exists(milestones))
        {
            catalog.ReadFile(milestones, "RP0_MILESTONE", catalog.AddMilestone);
        }
        else
        {
            catalog.AddDiagnostic("没有找到 GameData/RP-1/Strategies/Milestones.cfg，里程碑会显示内部名。");
        }

        var programs = Path.Combine(rp1Root, "Programs");
        if (Directory.Exists(programs))
        {
            foreach (var file in Directory.EnumerateFiles(programs, "*.cfg", SearchOption.AllDirectories))
            {
                catalog.ReadFile(file, "RP0_PROGRAM", catalog.AddProgram);
            }
        }
        else
        {
            catalog.AddDiagnostic("没有找到 GameData/RP-1/Programs，项目会显示内部名。");
        }

        return catalog;
    }

    private void ReadFile(string path, string nodeName, Action<SfsNode> accept)
    {
        try
        {
            var result = SfsParser.ParseFile(path);
            if (result.Document is null)
            {
                AddDiagnostic($"配置解析失败：{Path.GetFileName(path)}");
                return;
            }

            foreach (var node in Walk(result.Document))
            {
                if (node.Name.Equals(nodeName, StringComparison.OrdinalIgnoreCase))
                {
                    accept(node);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            AddDiagnostic($"读取配置失败：{Path.GetFileName(path)}：{ex.Message}");
        }
    }

    private static IEnumerable<SfsNode> Walk(SfsDocument document)
    {
        foreach (var element in document.Elements)
        {
            if (element is SfsNode node)
            {
                foreach (var descendant in Walk(node))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static IEnumerable<SfsNode> Walk(SfsNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            if (child is SfsNode childNode)
            {
                foreach (var descendant in Walk(childNode))
                {
                    yield return descendant;
                }
            }
        }
    }

    private void AddContract(SfsNode node)
    {
        var name = Value(node, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _contracts[name] = new Rp1ContractDefinition(
            name,
            Text(node, "title"),
            Text(node, "group"),
            Text(node, "synopsis") ?? Text(node, "genericDescription"),
            Text(node, "description") ?? Text(node, "completedMessage"));
    }

    private void AddMilestone(SfsNode node)
    {
        var name = Value(node, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _milestones[name] = new Rp1MilestoneDefinition(
            name,
            Value(node, "contractName"),
            Text(node, "headline"),
            Text(node, "article"),
            Text(node, "description"));
    }

    private void AddProgram(SfsNode node)
    {
        var name = Value(node, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _programs[name] = new Rp1ProgramDefinition(name, Text(node, "title"), Text(node, "description"));
    }

    private static string? Value(SfsNode node, string key)
    {
        var value = node.FindPair(key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Text(SfsNode node, string key) => Rp1Text.Clean(Value(node, key));

    private void AddDiagnostic(string message)
    {
        if (_diagnostics.Count < 24)
        {
            _diagnostics.Add(message);
        }
    }
}
