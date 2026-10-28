using System.Diagnostics;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Unmanaged;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 未管理区遍历边界（12 篇 F-42）：GameData 里的目录联接/符号链接既不能被扫描跟进，也不能被接管或删除。
/// </summary>
/// <remarks>
/// <para>为什么要专门测边界：<c>SearchOption.AllDirectories</c> 会跟进重解析点。指回上层的联接会让扫描无限递归，
/// 指向外部的联接会把 GameData 之外的文件当成「未管理内容」列出来，接着被用户一键删掉（越界删除）。
/// 联接自身也必须拒删——删掉一个联接有的实现会连带删掉目标内容。</para>
/// <para>造联接需要权限：符号链接要开发者模式或管理员，所以退回 <c>mklink /J</c>（目录联接，普通用户可用）；
/// 两种都造不出来时用 <see cref="Assert.Ignore(string)"/> 跳过，不让环境限制伪装成功能缺陷。</para>
/// </remarks>
[TestFixture]
public sealed class UnmanagedTraversalBoundaryTests
{
    [Test]
    public void Scan_DoesNotFollowLinkedDirectoryOutsideGameData()
    {
        using var workspace = PrepareGameData("unmanaged-link-scan");
        var gameData = GameDataOf(workspace);
        Write(gameData, "Real/junk.txt", "abc");

        // 联接目标放在 GameData 之外（仍是工作区里的临时目录）：跟进它就会把外部的文件列成「未管理」。
        var outside = Path.Combine(workspace.Path, "OutsideGameData");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "outside.txt"), "xyz");
        if (!TryCreateLink(Path.Combine(gameData, "Linked"), outside))
        {
            Assert.Ignore("当前环境造不出目录联接/符号链接，跳过遍历边界用例。");
        }

        var result = UnmanagedScanner.Scan(gameData, EmptyManaged());

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(
            result.Value!.Select(entry => entry.RelativePath),
            Is.EquivalentTo(new[] { "Real", "Real/junk.txt" }),
            "联接里的外部文件不属于 GameData，不能出现在未管理列表里");
    }

    [Test]
    public void Scan_SelfReferencingLink_DoesNotRecurseForever()
    {
        using var workspace = PrepareGameData("unmanaged-link-loop");
        var gameData = GameDataOf(workspace);
        Write(gameData, "Real/junk.txt", "abc");
        if (!TryCreateLink(Path.Combine(gameData, "Loop"), gameData))
        {
            Assert.Ignore("当前环境造不出目录联接/符号链接，跳过遍历边界用例。");
        }

        // 跟进这个联接会无限递归：能返回就说明边遍历边跳过了。
        var result = UnmanagedScanner.Scan(gameData, EmptyManaged());

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entries = result.Value!;
        Assert.That(
            entries.Select(entry => entry.RelativePath).Where(path => path.StartsWith("Loop", StringComparison.Ordinal)),
            Is.Empty,
            "指回 GameData 自身的联接不能被当成目录内容");
        Assert.That(entries.Select(entry => entry.RelativePath), Does.Contain("Real/junk.txt"));
    }

    [Test]
    public void Scan_StillWalksNestedRealDirectories()
    {
        using var workspace = PrepareGameData("unmanaged-nested");
        var gameData = GameDataOf(workspace);
        Write(gameData, "A/B/C/deep.txt", "deep");

        var result = UnmanagedScanner.Scan(gameData, EmptyManaged());

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(
            result.Value!.Select(entry => entry.RelativePath),
            Does.Contain("A/B/C/deep.txt"),
            "跳过联接不能顺带把普通子目录也跳掉");
    }

    [Test]
    public void AdoptPlan_RefusesLinkedDirectory()
    {
        using var workspace = PrepareGameData("unmanaged-link-adopt");
        var gameData = GameDataOf(workspace);
        var outside = Path.Combine(workspace.Path, "OutsideGameData");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "OutsideMod.dll"), "dll");
        if (!TryCreateLink(Path.Combine(gameData, "Linked"), outside))
        {
            Assert.Ignore("当前环境造不出目录联接/符号链接，跳过遍历边界用例。");
        }

        var plan = UnmanagedAdopter.Plan(
            gameData, EmptyManaged(), new Dictionary<string, string>(StringComparer.Ordinal), new[] { "Linked" });

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        var item = plan.Value!.Items.Single();
        Assert.That(item.Outcome, Is.EqualTo(UnmanagedAdoptOutcome.Refused));
        Assert.That(item.Reason, Does.Contain("联接"));
        Assert.That(plan.Value.Additions, Is.Empty, "外部目录里的 DLL 不能被登记成受管安装");
    }

    [Test]
    public void AdoptPlan_StillAdoptsDllInRealDirectory()
    {
        using var workspace = PrepareGameData("unmanaged-adopt-real");
        var gameData = GameDataOf(workspace);
        Write(gameData, "Leftover/JunkMod.dll", "dll");

        var plan = UnmanagedAdopter.Plan(
            gameData, EmptyManaged(), new Dictionary<string, string>(StringComparer.Ordinal), new[] { "Leftover" });

        Assert.That(plan.IsSuccess, Is.True, plan.Message);
        Assert.That(plan.Value!.Items.Single().Outcome, Is.EqualTo(UnmanagedAdoptOutcome.Adopted));
        Assert.That(plan.Value.Additions["JunkMod"], Is.EqualTo("GameData/Leftover/JunkMod.dll"));
    }

    [Test]
    public void Delete_RefusesLinkedDirectory()
    {
        using var workspace = PrepareGameData("unmanaged-link-delete");
        var gameData = GameDataOf(workspace);
        var outside = Path.Combine(workspace.Path, "OutsideGameData");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "keep.txt");
        File.WriteAllText(outsideFile, "keep");
        if (!TryCreateLink(Path.Combine(gameData, "Linked"), outside))
        {
            Assert.Ignore("当前环境造不出目录联接/符号链接，跳过遍历边界用例。");
        }

        var result = UnmanagedScanner.Delete(gameData, EmptyManaged(), new[] { "Linked" }, dryRun: false);

        Assert.That(result.IsSuccess, Is.False, "联接必须拒删");
        Assert.That(result.Message, Does.Contain("联接"));
        Assert.That(File.Exists(outsideFile), Is.True, "拒绝删除后目标内容必须原样还在");
        Assert.That(Directory.Exists(Path.Combine(gameData, "Linked")), Is.True, "联接自身也不能被删掉");
    }

    [Test]
    public void Delete_DryRunAlsoRefusesLinkedDirectory()
    {
        using var workspace = PrepareGameData("unmanaged-link-dryrun");
        var gameData = GameDataOf(workspace);
        var outside = Path.Combine(workspace.Path, "OutsideGameData");
        Directory.CreateDirectory(outside);
        if (!TryCreateLink(Path.Combine(gameData, "Linked"), outside))
        {
            Assert.Ignore("当前环境造不出目录联接/符号链接，跳过遍历边界用例。");
        }

        // 预演与真删必须给同一个答案，否则界面会先显示「可删除」再失败。
        var result = UnmanagedScanner.Delete(gameData, EmptyManaged(), new[] { "Linked" }, dryRun: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("联接"));
    }

    [Test]
    public void Delete_MissingPathStillReportsMissingInsteadOfLink()
    {
        using var workspace = PrepareGameData("unmanaged-missing");
        var gameData = GameDataOf(workspace);

        var result = UnmanagedScanner.Delete(gameData, EmptyManaged(), new[] { "NotThere" }, dryRun: false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("找不到要删除的内容"), "不存在的路径仍走原来的失败文案，不能被联接检查吞掉");
    }

    private static IReadOnlySet<string> EmptyManaged() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static TempWorkspace PrepareGameData(string label)
    {
        var workspace = new TempWorkspace(label);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "GameData"));
        return workspace;
    }

    private static string GameDataOf(TempWorkspace workspace) => Path.Combine(workspace.Path, "GameData");

    private static void Write(string gameData, string relative, string content)
    {
        var path = Path.Combine(gameData, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>造一个重解析点：先试符号链接（需要权限），不行退回 Windows 目录联接（普通用户可用）。</summary>
    private static bool TryCreateLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            TestContext.Out.WriteLine($"符号链接不可用（{ex.GetType().Name}），改用目录联接。");
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return Directory.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            TestContext.Out.WriteLine($"跳过符号链接用例：{ex.GetType().Name} {ex.Message}");
            return false;
        }
    }
}
