using Avalonia.Controls;
using CkanUI.App.Views;
using CkanUI.Plugin.ModText;
using CkanUI.Plugin.Saves;
using NUnit.Framework;

namespace CkanUI.App.Tests.Automation;

/// <summary>
/// 全页面全按钮批量自动化测试套件：
/// 在 Avalonia.Headless 纯内存环境下，无需任何宿主系统窗口与物理鼠标键盘操作，
/// 自动化扫描并批量触发所有主要页面视图中的按钮、菜单与切换操作，验证零崩溃、零死锁与零未捕获异常。
/// </summary>
[TestFixture]
public sealed class BatchButtonOperationTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        HeadlessAppHost.EnsureInitialized();
    }

    [TestCase(typeof(InstancesPageView), "实例管理页")]
    [TestCase(typeof(ModsPageView), "模组列表页")]
    [TestCase(typeof(ChangesPageView), "变更集审查页")]
    [TestCase(typeof(ReposPageView), "仓库管理页")]
    [TestCase(typeof(SettingsPageView), "全局设置页")]
    [TestCase(typeof(ModPacksPageView), "整合包列表页")]
    [TestCase(typeof(ModPackEditorPageView), "整合包编辑页")]
    [TestCase(typeof(CachePageView), "缓存管理页")]
    [TestCase(typeof(ContentPageView), "内容预览页")]
    [TestCase(typeof(DedupPageView), "重复模组页")]
    [TestCase(typeof(DownloadStatsPageView), "下载统计页")]
    [TestCase(typeof(InstallHistoryPageView), "安装历史页")]
    [TestCase(typeof(PlayTimePageView), "游戏时间统计页")]
    [TestCase(typeof(PluginsPageView), "插件管理页")]
    [TestCase(typeof(SavesPageView), "存档工作台页")]
    [TestCase(typeof(UnmanagedPageView), "未受管文件页")]
    [TestCase(typeof(ModTextPageView), "模组文本翻译页")]
    public void EveryPage_BatchScanAndExecuteAllButtons(Type viewType, string displayName)
    {
        // 1. 实例化视图并挂载至无头测试窗口
        var view = (Control)Activator.CreateInstance(viewType)!;
        var window = HeadlessAppHost.CreateTestWindow(view);

        try
        {
            // 2. 深度扫描页面视觉/逻辑树中的所有可交互控件
            var controls = ControlTreeScanner.ScanInteractiveControls(view);

            Assert.That(controls, Is.Not.Null, $"{displayName} 控件扫描结果不能为 null。");
            Console.WriteLine($"[{displayName}] 扫描发现 {controls.Count} 个交互项");

            // 3. 批量执行所有交互动作
            var results = InteractiveActionExecutor.ExecuteBatch(controls);

            // 4. 断言与审计报告
            var failures = results.Where(r => !r.Succeeded).ToList();
            var executedCount = results.Count(r => r.WasExecutable);

            Console.WriteLine($"[{displayName}] 审计结果: 共 {results.Count} 项，实际执行 {executedCount} 项，失败 {failures.Count} 项");

            if (failures.Count > 0)
            {
                foreach (var fail in failures)
                {
                    Console.WriteLine($"  - 失败: {fail.Target.DisplayText} ({fail.Target.ControlType}) -> {fail.ErrorMessage}");
                }
            }

            Assert.That(failures, Is.Empty,
                $"{displayName} 存在 {failures.Count} 个按钮/操作执行失败: {string.Join("; ", failures.Select(f => f.ErrorMessage))}");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
