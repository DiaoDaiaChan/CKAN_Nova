using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CkanUI.App.Converters;
using CkanUI.App.Tests.Automation;
using CkanUI.Plugin.ModText;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Plugins;
using CkanUI.ViewModels;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 内置插件必须自带图标：侧栏里「性能」「模组翻译」不能都长成同一个七巧板。
/// </summary>
/// <remarks>
/// <para>契约的 <see cref="PluginPageDescriptor.Glyph"/> 只是字符串，写错名字不会编译失败——
/// 宿主的字形表认不出来时会退回通用「插件」图标，界面上看起来"就是没做图标"。
/// 所以这条用例把「声明了图标」和「这个图标名宿主真的认识」分开断言，两段都必须过。</para>
/// <para>用真插件 dll 而不是手写描述符：只有这样才验得到插件里那张 <c>PageList</c> 表真的填了 Glyph，
/// 而不是文档里写了、代码里忘了。</para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class PluginGlyphTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void 内置插件的页签各自声明了宿主认识的图标()
    {
        var staged = StageBundledPlugins();
        try
        {
            var storePath = Path.Combine(staged, "plugin-activation.json");
            var service = new PluginService(
                Path.Combine(staged, "user"),
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: staged,
                activationStore: new PluginActivationStore(storePath));

            var discovered = Task.Run(() => service.DiscoverAsync(activateBundled: true)).GetAwaiter().GetResult();
            Assert.That(discovered.IsSuccess, Is.True, discovered.Message);

            var pages = service.Active.SelectMany(plugin => plugin.Pages).ToArray();
            Assert.That(pages, Has.Length.EqualTo(2), "内置插件应当各贡献一个页签");

            foreach (var page in pages)
            {
                Assert.That(page.Glyph, Is.Not.Null.And.Not.Empty, page.Key + " 必须声明图标，否则侧栏只能画通用插件图标");
                Assert.That(page.Glyph, Is.Not.EqualTo("Puzzle"), page.Key + " 不该用通用插件图标");

                var geometry = NavGlyphToGeometryConverter.Instance.Convert(page.Glyph, typeof(Geometry), null, System.Globalization.CultureInfo.InvariantCulture) as Geometry;
                Assert.That(geometry, Is.Not.Null, "宿主字形表里没有「" + page.Glyph + "」：写错名字会静默退回七巧板");
            }

            var byKey = pages.ToDictionary(page => page.Key, page => page.Glyph!, StringComparer.Ordinal);
            Assert.That(byKey["performance"], Is.EqualTo("Performance"));
            Assert.That(byKey["modtext"], Is.EqualTo("Translate"));
            Assert.That(byKey["performance"], Is.Not.EqualTo(byKey["modtext"]), "两个插件页签必须能一眼区分");

            // 外壳建导航项时用的就是这张表：插件的图标要真的进 NavItem，而不是只留在描述符里。
            var shell = new ShellViewModel(new SilentNavigation(), plugins: service);
            Assert.That(shell.NavItems.Single(item => item.Key == "performance").Glyph, Is.EqualTo("Performance"));
            Assert.That(shell.NavItems.Single(item => item.Key == "modtext").Glyph, Is.EqualTo("Translate"));

            Task.Run(() => service.UnloadAsync(service.Active[0].Id)).GetAwaiter().GetResult();
        }
        finally
        {
            try
            {
                Directory.Delete(staged, recursive: true);
            }
            catch (IOException)
            {
                // 插件程序集句柄由 ALC 回收节奏决定，删不掉不算失败。
            }
        }
    }

    /// <summary>把两个内置插件的 dll/pdb 复制进一个干净目录（依赖副本不进去，否则扫描器会留失败记录）。</summary>
    private static string StageBundledPlugins()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ckanui-glyph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        // 配置名从本用例引用的插件程序集反推（Debug / Verify 都能对上）。
        var modTextAssembly = typeof(ModTextPlugin).Assembly.Location;
        var config = new DirectoryInfo(Path.GetDirectoryName(modTextAssembly)!).Parent?.Name ?? "Debug";
        var repoRoot = FindRepoRoot();

        foreach (var (project, assemblyName) in new[]
                 {
                     ("CkanUI.Plugin.ModText", "CkanUI.Plugin.ModText.dll"),
                     ("CkanUI.Plugin.Performance", "CkanUI.Plugin.Performance.dll"),
                 })
        {
            var source = Path.Combine(repoRoot, "src", project, "bin", config, "net10.0", assemblyName);
            Assert.That(File.Exists(source), Is.True, "找不到插件产物：" + source + "（先构建插件工程）");
            File.Copy(source, Path.Combine(directory, assemblyName), overwrite: true);

            var pdb = Path.ChangeExtension(source, ".pdb");
            if (File.Exists(pdb))
            {
                File.Copy(pdb, Path.Combine(directory, Path.GetFileName(pdb)), overwrite: true);
            }
        }

        return directory;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ckan-ui.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("找不到仓库根目录（ckan-ui.sln）");
    }
}
