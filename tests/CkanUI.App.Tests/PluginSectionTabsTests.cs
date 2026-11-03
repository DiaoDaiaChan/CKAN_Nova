using CkanUI.Plugin.Assets;
using CkanUI.Plugin.KspConfig;
using CkanUI.Plugin.Saves;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Plugins;
using CkanUI.ViewModels;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 「实例」分节内嵌页签（存档 / 飞船 / 旗帜 / 加载图 / KSP 游戏设置）在<b>真插件程序集</b>上的接线验证。
/// </summary>
/// <remarks>
/// <para>这几页不是侧栏顶格入口——它们住在「实例」分节顶部的页签条里。契约是靠
/// <c>PluginPageDescriptor.Section</c> 表达的，宿主的 SectionSpecs 保留同名槽位，插件补进来；
/// 断言里同时钉住两件事：<b>页签顺序与搬迁前逐字一致</b>（用户不该看出页面换了主人），
/// 以及<b>它们不进侧栏</b>（否则"实例"分节旁边会多出五个并列入口）。</para>
/// <para>用真 dll 而不是假描述符：Section 字符串在插件程序集里，宿主这一侧只有一份解析逻辑，
/// 假条目测不到"插件作者把 Section 拼错"这类真实故障。</para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class PluginSectionTabsTests
{
    /// <summary>搬迁后「实例」分节的页签顺序：槽位顺序由宿主 SectionSpecs 决定，与搬迁前完全一致。</summary>
    private static readonly string[] ExpectedInstancesTabs =
    [
        "instances", "saves", "ships", "flags", "loadingimages", "kspconfig",
        "unmanaged", "installhistory", "playtime",
    ];

    [Test]
    public void 实例分节的五个页签由插件补进槽位_不进侧栏且顺序不变()
    {
        var bundledDirectory = StageBundledPlugins();
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-plugins-sec-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-sec-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            var discovered = Task.Run(() => service.DiscoverAsync(activateBundled: true)).GetAwaiter().GetResult();
            Assert.That(discovered.IsSuccess, Is.True, discovered.Message);
            Assert.That(service.Failures, Is.Empty, "内置目录里只有三个插件自身：" + DescribeFailures(service));

            var shell = new ShellViewModel(new SilentNavigation(), plugins: service);

            var instances = shell.Sections.SingleOrDefault(section => section.Key == "instances");
            Assert.That(instances, Is.Not.Null, "「实例」分节没有建起来；实际分节：" + string.Join(", ", shell.Sections.Select(s => s.Key)));

            Assert.That(
                instances!.Tabs.Select(tab => tab.Key),
                Is.EqualTo(ExpectedInstancesTabs),
                "内嵌页签的顺序必须与搬迁前逐字一致——槽位由宿主定序，插件只负责把自己放进槽位");

            Assert.Multiple(() =>
            {
                foreach (var key in new[] { "saves", "ships", "flags", "loadingimages", "kspconfig" })
                {
                    // 内嵌页签仍要进 NavItems（选中态与预热队列都按它查），但不能出现在侧栏（RailItems）。
                    Assert.That(shell.NavItems.Select(item => item.Key), Does.Contain(key), key + " 丢了：选中态与预热队列都按 NavItems 找页面");
                    Assert.That(shell.RailItems.Select(item => item.Key), Does.Not.Contain(key), key + " 不该出现在侧栏：它属于「实例」分节的页签条");
                }
            });

            // 页签标题走本地化键，插件不硬编码中文：这里只确认标题不是空串。
            Assert.That(instances.Tabs.Select(tab => tab.Title), Has.None.Empty);
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    [Test]
    public void 停用资产插件后它提供的三个页签一起消失_其他页签不受影响()
    {
        var bundledDirectory = StageBundledPlugins();
        var userDirectory = Path.Combine(Path.GetTempPath(), "ckanui-plugins-off-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(Path.GetTempPath(), "ckanui-activation-off-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PluginService(
                userDirectory,
                new AssemblyLoadContextPluginLoader(),
                hostVersion: null,
                bundledDirectory: bundledDirectory,
                activationStore: new PluginActivationStore(storePath));

            Task.Run(() => service.DiscoverAsync(activateBundled: true)).GetAwaiter().GetResult();
            var shell = new ShellViewModel(new SilentNavigation(), plugins: service);

            var deactivated = Task.Run(() => service.DeactivateAsync(AssetsPlugin.PluginId)).GetAwaiter().GetResult();
            Assert.That(deactivated.IsSuccess, Is.True, deactivated.Message);

            var instances = shell.Sections.Single(section => section.Key == "instances");
            Assert.That(
                instances.Tabs.Select(tab => tab.Key),
                Is.EqualTo(new[] { "instances", "saves", "kspconfig", "unmanaged", "installhistory", "playtime" }),
                "停用资产插件只该拿走它自己声明的三个页签，存档与 KSP 设置仍在");

            Assert.That(shell.NavItems.Select(item => item.Key), Does.Not.Contain("ships"));
        }
        finally
        {
            DeleteQuietly(bundledDirectory);
            DeleteQuietly(userDirectory);
            DeleteQuietly(storePath);
        }
    }

    /// <summary>把三个插件工程的产物 dll/pdb 复制进一个干净目录，模拟发布形态的 &lt;App&gt;/plugins。</summary>
    private static string StageBundledPlugins()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ckanui-bundled-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        foreach (var assembly in new[]
                 {
                     typeof(SavesPlugin).Assembly,
                     typeof(AssetsPlugin).Assembly,
                     typeof(KspConfigPlugin).Assembly,
                 })
        {
            var source = assembly.Location;
            Assert.That(source, Is.Not.Empty, "插件程序集没有磁盘路径（说明它是从流里加载的），本用例没法搭发布形态");
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: true);
            var pdb = Path.ChangeExtension(source, ".pdb");
            if (File.Exists(pdb))
            {
                File.Copy(pdb, Path.Combine(directory, Path.GetFileName(pdb)), overwrite: true);
            }
        }

        return directory;
    }

    private static string DescribeFailures(IPluginService service) =>
        service.Failures.Count == 0 ? "无" : string.Join(" | ", service.Failures.Select(info => info.Id + ": " + info.FailureReason));

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响断言结果；下次跑测试会换个 guid。
        }
    }
}
