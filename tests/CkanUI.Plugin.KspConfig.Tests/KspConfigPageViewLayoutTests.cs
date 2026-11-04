using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.KspConfig;
using CkanUI.Ui.Icons;
using NUnit.Framework;

namespace CkanUI.Plugin.KspConfig.Tests;

/// <summary>
/// KSP 配置页的视图契约：工具条上的按钮与页签真的连到了视图模型上，行高不再虚胖。
/// </summary>
/// <remarks>
/// <para>这页之前静默地坏过：XAML 里的绑定名写成了 <c>Header</c> / <c>SaveCommand</c> / <c>ClearSearchCommand</c>，
/// 而 <c>x:CompileBindings="False"</c> 让它们一条警告都不报——页签渲染成一排空圆角块，保存按钮没有文字，
/// 「重新加载」「清除」点了没反应。纯视图模型用例测不出这类问题，只有真装一棵可视树才看得见。</para>
/// <para>只断言结构、文案与相对尺寸，不断言像素：具体配色与圆角由宿主样式负责。</para>
/// </remarks>
[TestFixture]
public sealed class KspConfigPageViewLayoutTests
{
    [Test]
    public void ToolbarButtonsAndTabs_AreBoundToRealViewModelMembers()
    {
        UiTestHost.EnsureInitialized();
        RequireWindowAffinity();

        var viewModel = new KspConfigPageViewModel(new FakeKspConfigService());
        var window = Show(viewModel);
        try
        {
            var tabs = window.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(list => list.Classes.Contains("KspTabs"));
            if (tabs is null)
            {
                Assert.Fail("工具条里必须有分类页签栏。");
                return;
            }

            var tabHeaders = tabs.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            var glyphs = tabs.GetVisualDescendants().OfType<GlyphIcon>().ToArray();
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();

            Assert.Multiple(() =>
            {
                // 页签标题：绑错成 Header 时这里全是空字符串。
                Assert.That(tabs.ItemCount, Is.EqualTo(6), "六个分类页签（全部 + 五个分类）。");
                Assert.That(tabHeaders, Does.Contain("全部"));
                Assert.That(tabHeaders, Does.Contain("图像画质"));
                Assert.That(tabHeaders, Does.Contain("高级调试"));

                // 页签图标：字形名写错时 GlyphIcon 会静默留空，HasGlyph 为 false。
                Assert.That(glyphs, Has.Length.EqualTo(6));
                Assert.That(glyphs.All(glyph => glyph.HasGlyph), Is.True, "六个页签的图标名都要能在 GlyphCatalog 里解析。");

                // 工具条动作按钮：按钮存在、文案非空、Command 不是 null（绑到死属性上会是 null）。
                Assert.That(ButtonWithText(buttons, "清除"), Is.Not.Null);
                Assert.That(ButtonWithText(buttons, "重新加载")?.Command, Is.Not.Null, "「重新加载」必须绑到 LoadConfigCommand。");
                Assert.That(ButtonWithText(buttons, "恢复备份")?.Command, Is.Not.Null, "「恢复备份」必须绑到 RestoreBackupCommand。");
                Assert.That(ButtonWithText(buttons, viewModel.SaveButtonText)?.Command, Is.Not.Null, "保存按钮必须绑到 SaveConfigCommand 且有文案。");
            });

            // 页签点击要真的切换筛选（SelectedItem 双向绑定）。
            tabs.SelectedIndex = 1;
            UiTestHost.PumpJobs();
            Assert.That(viewModel.SelectedTab, Is.SameAs(viewModel.Tabs[1]));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void StatusStripIsCollapsedUntilThereIsSomethingToSay()
    {
        UiTestHost.EnsureInitialized();
        RequireWindowAffinity();

        var viewModel = new KspConfigPageViewModel(new FakeKspConfigService());
        var window = Show(viewModel);
        try
        {
            var strip = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("CkanStatusStrip"));

            Assert.That(strip, Is.Not.Null, "状态条要有，只是没话说的时候收起。");
            if (strip is null)
            {
                return;
            }

            // 表头位置是「这一行到底占不占高度」的硬证据：收起时它必须整体上移。
            var headerTopWithStatus = HeaderTop(window);

            // 没话说：收起。之前这条绑在不存在的 HasStatus 上，永远占着一行空白。
            viewModel.StatusMessage = string.Empty;
            UiTestHost.PumpJobs();
            window.UpdateLayout();
            UiTestHost.PumpJobs();

            Assert.That(strip.IsEffectivelyVisible, Is.False, "没有状态消息时状态条必须收起。");
            Assert.That(HeaderTop(window)!.Value, Is.LessThan(headerTopWithStatus!.Value),
                "状态条收起后表头要跟着上移——否则就是留了一条空行。");

            // 有话要说：出现且撑出高度（绑定真的在跟踪 HasStatus）。
            viewModel.StatusMessage = "已加载 5 项配置";
            UiTestHost.PumpJobs();
            window.UpdateLayout();
            UiTestHost.PumpJobs();

            Assert.That(strip.IsEffectivelyVisible, Is.True, "有状态消息时状态条要出来。");
            Assert.That(strip.Bounds.Height, Is.GreaterThan(0), "展开时要有可见高度。");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void EmptyResultShowsExplanationCardInsteadOfBlankList()
    {
        UiTestHost.EnsureInitialized();
        RequireWindowAffinity();

        // 这个实例的 settings.cfg 里什么都没读到：视图挂上来自动加载一轮，列表就该是空的。
        var service = new FakeKspConfigService { Document = FakeKspConfigService.Build(settings: []) };
        var viewModel = new KspConfigPageViewModel(service);
        var window = Show(viewModel);
        try
        {
            WaitUntil(() => viewModel.HasEmptyResult);

            var emptyCard = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("CkanEmptyStateCard"));
            var explanation = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(text => text.IsEffectivelyVisible
                    && text.Text?.Contains("还没有读到", StringComparison.Ordinal) == true);

            var list = window.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(box => box.Classes.Contains("KspConfigList"));

            Assert.Multiple(() =>
            {
                Assert.That(emptyCard, Is.Not.Null);
                Assert.That(emptyCard?.IsEffectivelyVisible, Is.True, "没有配置项时要给说明卡，不是一片空白。");
                Assert.That(explanation, Is.Not.Null, "说明卡要说清为什么是空的。");
                Assert.That(list?.IsEffectivelyVisible, Is.Not.True, "空列表时列表本身不该再占位。");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public async Task RowsAreDenserThanTheOldThreeLineCard()
    {
        UiTestHost.EnsureInitialized();
        RequireWindowAffinity();

        var viewModel = new KspConfigPageViewModel(new FakeKspConfigService());
        await viewModel.LoadConfigAsync();

        var window = Show(viewModel);
        try
        {
            var rows = window.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.That(rows, Is.Not.Empty, "加载 5 项后列表里应该有行。");

            var tallest = rows.Max(row => row.Bounds.Height);
            Assert.That(tallest, Is.LessThanOrEqualTo(48),
                "行高必须收进 48px 以内（实测 45px）：旧版三行堆叠 + CkanCard 内边距是 58-60px，一屏只放得下 6 行；" +
                "放宽到 72 就等于允许退回旧密度，这条守卫也就白写了。");
            Assert.That(rows.All(row => row.Bounds.Width > 0), Is.True, "行宽不能被压成 0。");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>表头（「配置项 / 官方说明」那一行）在窗口坐标系里的顶边位置。</summary>
    private static double? HeaderTop(Window window)
    {
        var header = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.Text?.StartsWith("配置项", StringComparison.Ordinal) == true);
        return header?.TranslatePoint(new Point(0, 0), window)?.Y;
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 5_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            UiTestHost.PumpJobs();
            if (condition())
            {
                UiTestHost.PumpJobs();
                return;
            }

            Thread.Sleep(10);
        }

        Assert.Fail("等待页面状态超时。");
    }

    private static Button? ButtonWithText(IEnumerable<Button> buttons, string text)
        => buttons.FirstOrDefault(button => button.Content is string content
            && string.Equals(content, text, StringComparison.Ordinal));

    private static void RequireWindowAffinity()
    {
        if (!UiTestHost.CanCreateWindows)
        {
            Assert.Ignore("无头壳由全量套件里另一个测试程序集在别的线程建好，本线程没有窗口亲和性；单跑本程序集即可验证本条。");
        }
    }

    private static Window Show(KspConfigPageViewModel viewModel)
    {
        const double width = 1280;
        const double height = 860;

        var window = new Window
        {
            Width = width,
            Height = height,
            Content = new KspConfigPageView { DataContext = viewModel },
        };

        window.Show();
        UiTestHost.PumpJobs();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        UiTestHost.PumpJobs();
        window.UpdateLayout();
        UiTestHost.PumpJobs();
        return window;
    }

    private sealed class FakeKspConfigService : IKspConfigService
    {
        public static KspConfigDocument Build(IReadOnlyList<KspSettingItem>? settings = null)
        {
            settings ??= new List<KspSettingItem>
            {
                new()
                {
                    Key = "FULLSCREEN",
                    Value = "True",
                    OriginalValue = "True",
                    DisplayName = "全屏显示",
                    Description = "以全屏模式启动游戏。",
                    LocKey = "#autoLOC_149894",
                    Category = KspSettingCategory.Graphics,
                    SettingType = KspSettingType.Boolean,
                    Priority = 100
                },
                new()
                {
                    Key = "SCREEN_RESOLUTION_WIDTH",
                    Value = "1920",
                    OriginalValue = "1920",
                    DisplayName = "屏幕分辨率（宽）",
                    Category = KspSettingCategory.Graphics,
                    SettingType = KspSettingType.Integer,
                    MinValue = 800,
                    MaxValue = 3840,
                    Priority = 99
                },
                new()
                {
                    Key = "MASTER_VOLUME",
                    Value = "0.8",
                    OriginalValue = "0.8",
                    DisplayName = "主音量",
                    Category = KspSettingCategory.Audio,
                    SettingType = KspSettingType.Float,
                    MinValue = 0,
                    MaxValue = 1,
                    Step = 0.05
                },
                new()
                {
                    Key = "UI_SIZE",
                    Value = "1.0",
                    OriginalValue = "1.0",
                    DisplayName = "操作界面尺寸",
                    Category = KspSettingCategory.Gameplay,
                    SettingType = KspSettingType.Float,
                    MinValue = 0.5,
                    MaxValue = 2
                },
                new()
                {
                    Key = "ADVANCED_TWEAKABLES",
                    Value = "True",
                    OriginalValue = "True",
                    DisplayName = "高级部件调整",
                    Category = KspSettingCategory.Advanced,
                    SettingType = KspSettingType.Boolean
                }
            };

            return new KspConfigDocument
            {
                FilePath = @"D:KSPsettings.cfg",
                Language = "zh-cn",
                HasBackup = true,
                Settings = settings,
                RawLines = settings.Select(setting => setting.Key + " = " + setting.Value).ToArray(),
                LocalizationTable = new Dictionary<string, string> { ["#autoLOC_149894"] = "全屏显示" }
            };
        }

        public KspConfigDocument Document { get; set; } = Build();

        public Task<KspConfigDocument> LoadConfigAsync(string? instancePath = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Document);

        public Task<bool> SaveConfigAsync(KspConfigDocument document, IEnumerable<KspSettingItem> modifiedSettings, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyDictionary<string, string>> LoadOfficialLocalizationAsync(string? instancePath = null, string? language = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<bool> RestoreBackupAsync(string? instancePath = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public string? GetBackupPath(string? instancePath = null) => @"D:KSPsettings.cfg.bak";
    }
}
