using CkanUI.Core.Steam;
using NUnit.Framework;

namespace CkanUI.Core.Tests;

/// <summary>
/// <c>libraryfolders.vdf</c> 的纯解析（06 篇 UI-32 Steam 导入）。
/// </summary>
/// <remarks>
/// 解析器原先挂在视图模型层，随 Steam 库定位一起下沉到 Core（服务层要读 vdf，而服务层不能引用视图模型层），
/// 用例也跟着搬过来，断言保持原样。
/// </remarks>
[TestFixture]
public sealed class SteamLibraryParserTests
{
    [Test]
    public void ParseLibraryRoots_支持两种VDF写法并去重()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"contentstatsid\" \"1\"\n\t\"path\" \"D:\\\\SteamLibrary\"\n\t\"path\" \"D:\\\\SteamLibrary\"\n}\n";

        var roots = SteamLibraryParser.ParseLibraryRoots(vdf);
        var candidates = SteamLibraryParser.CandidateGameDirs(roots);

        Assert.That(roots, Is.EqualTo(new[] { @"D:\SteamLibrary" }));
        Assert.That(candidates, Is.EqualTo(new[] { Path.Combine(@"D:\SteamLibrary", "steamapps", "common", "Kerbal Space Program") }));
    }

    [Test]
    public void ParseLibraryRoots_空内容返回空集合()
    {
        Assert.That(SteamLibraryParser.ParseLibraryRoots(null), Is.Empty);
        Assert.That(SteamLibraryParser.CandidateGameDirs(null), Is.Empty);
    }
}
