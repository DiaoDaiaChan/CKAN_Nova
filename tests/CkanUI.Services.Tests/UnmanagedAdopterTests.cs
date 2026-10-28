using CkanUI.Services.Unmanaged;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 接管判定（12 篇 F-42 / INT-09）：只接管 .dll、目录展开、已受管跳过、标识符冲突显式拒绝、非法路径整批失败。
/// </summary>
/// <remarks>全部在 <see cref="TempWorkspace"/> 造出来的假 GameData 里跑，不碰真实实例。</remarks>
[TestFixture]
public sealed class UnmanagedAdopterTests
{
    [Test]
    public void Plan_AcceptsDllAndRegistersTheGameDataRelativePath()
    {
        using var workspace = Prepare("adopt-plan-ok");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");

        var plan = Plan(workspace, Array.Empty<string>(), Installed(), "Stray/JunkMod.dll");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        var (items, additions) = (plan.Value!.Items, plan.Value!.Additions);
        Assert.That(items.Single().Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Adopted));
        Assert.That(items.Single().Identifier, Is.EqualTo("JunkMod"));
        Assert.That(additions["JunkMod"], Is.EqualTo("GameData/Stray/JunkMod.dll"));
    }

    [Test]
    public void Plan_RefusesEverythingThatIsNotADll()
    {
        using var workspace = Prepare("adopt-plan-nondll");
        Write(workspace.Path, "loose.cfg", "cfg");

        var plan = Plan(workspace, Array.Empty<string>(), Installed(), "loose.cfg");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Additions, Is.Empty);
        Assert.That(plan.Value!.Items.Single().Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Refused));
        Assert.That(plan.Value!.Items.Single().Reason, Does.Contain(".dll"));
    }

    [Test]
    public void Plan_ExpandsDirectoriesIntoTheirDlls()
    {
        using var workspace = Prepare("adopt-plan-dir");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        Write(workspace.Path, "Stray/readme.txt", "txt");
        Write(workspace.Path, "Stray/Inner/Deep.dll", "dll");

        var plan = Plan(workspace, Array.Empty<string>(), Installed(), "Stray");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(
            plan.Value!.Additions.Keys,
            Is.EquivalentTo(new[] { "JunkMod", "Deep" }),
            "目录要递归展开，非 DLL 不参与");
        Assert.That(plan.Value!.Items.Select(item => item.RelativePath), Is.EqualTo(new[] { "Stray/Inner/Deep.dll", "Stray/JunkMod.dll" }));
    }

    [Test]
    public void Plan_SkipsFilesThatAreAlreadyManaged()
    {
        using var workspace = Prepare("adopt-plan-skip");
        Write(workspace.Path, "ManagedMod/ManagedMod.dll", "dll");

        var plan = Plan(workspace, new[] { "ManagedMod/ManagedMod.dll" }, Installed(), "ManagedMod/ManagedMod.dll");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Additions, Is.Empty);
        Assert.That(plan.Value!.Items.Single().Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Skipped));
    }

    [Test]
    public void Plan_RefusesDuplicateIdentifiersInsideOneBatch()
    {
        using var workspace = Prepare("adopt-plan-dup");
        Write(workspace.Path, "A/Junk.dll", "dll");
        Write(workspace.Path, "B/Junk.dll", "dll");

        var plan = Plan(workspace, Array.Empty<string>(), Installed(), "A/Junk.dll", "B/Junk.dll");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Additions, Has.Count.EqualTo(1));
        Assert.That(plan.Value!.Items[0].Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Adopted));
        Assert.That(plan.Value!.Items[1].Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Refused));
        Assert.That(plan.Value!.Items[1].Reason, Does.Contain("重复"));
    }

    [Test]
    public void Plan_RefusesAnIdentifierThatBelongsToAnotherPath()
    {
        using var workspace = Prepare("adopt-plan-taken");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        var plan = Plan(workspace, Array.Empty<string>(), Installed("JunkMod"), "Stray/JunkMod.dll");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Additions, Is.Empty);
        Assert.That(plan.Value!.Items.Single().Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Refused));
        Assert.That(plan.Value!.Items.Single().Reason, Does.Contain("已登记给"));
    }

    [Test]
    public void Plan_AcceptsTheSamePathThatIsAlreadyRegisteredForTheSameDll()
    {
        using var workspace = Prepare("adopt-plan-same");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        var installed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JunkMod"] = "GameData/Stray/JunkMod.dll",
        };

        var plan = Plan(workspace, Array.Empty<string>(), installed, "Stray/JunkMod.dll");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Items.Single().Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Adopted),
            "同路径重复登记是幂等的，不算冲突");
    }

    [Test]
    public void Plan_RejectsAbsolutePathsForTheWholeBatch()
    {
        using var workspace = Prepare("adopt-plan-abs");

        var plan = Plan(workspace, Array.Empty<string>(), Installed(), @"C:\Windows\System32\x.dll");

        Assert.That(plan.IsSuccess, Is.False, "非法路径要整批失败，不能只拒一项");
        Assert.That(plan.Message, Does.Contain("路径不合法"));
    }

    [Test]
    public void Plan_RefusesFilesThatVanished()
    {
        using var workspace = Prepare("adopt-plan-gone");

        var plan = Plan(workspace, Array.Empty<string>(), Installed(), "Stray/Gone.dll");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Items.Single().Outcome, Is.EqualTo(CkanUI.Services.Abstractions.UnmanagedAdoptOutcome.Refused));
        Assert.That(plan.Value!.Items.Single().Reason, Does.Contain("已不存在"));
    }

    [Test]
    public void Plan_NormalizesBackslashesAndSanitizesIdentifiers()
    {
        using var workspace = Prepare("adopt-plan-normalize");
        Write(workspace.Path, "Stray/Junk_Mod.dll", "dll");

        var plan = Plan(
            workspace,
            Array.Empty<string>(),
            Installed(),
            @"Stray\Junk_Mod.dll");

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Items.Single().RelativePath, Is.EqualTo("Stray/Junk_Mod.dll"), "反斜杠要归一成正斜杠");
        Assert.That(plan.Value!.Items.Single().Identifier, Is.EqualTo("Junk-Mod"), "标识符走注册表的 Sanitize");
    }

    private static CkanUI.Services.Abstractions.OperationResult<UnmanagedAdoptPlan> Plan(
        TempWorkspace workspace,
        IReadOnlyCollection<string> managed,
        IReadOnlyDictionary<string, string> installed,
        params string[] relativePaths)
        => UnmanagedAdopter.Plan(
            Path.Combine(workspace.Path, "GameData"),
            new HashSet<string>(managed, StringComparer.OrdinalIgnoreCase),
            installed,
            relativePaths,
            Sanitize);

    /// <summary>造一份「标识符 → GameData/标识符/标识符.dll」的已登记表。</summary>
    private static IReadOnlyDictionary<string, string> Installed(params string[] identifiers)
        => identifiers.ToDictionary(
            key => key,
            key => "GameData/" + key + "/" + key + ".dll",
            StringComparer.Ordinal);

    private static string Sanitize(string raw)
    {
        var chars = raw.Trim().Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static TempWorkspace Prepare(string label)
    {
        var workspace = new TempWorkspace(label);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "GameData"));
        return workspace;
    }

    private static void Write(string gameDir, string relative, string content = "x")
    {
        var path = Path.Combine(gameDir, "GameData", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
