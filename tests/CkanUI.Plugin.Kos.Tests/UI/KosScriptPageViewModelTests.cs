using CkanUI.Plugin.Kos.Scripts;
using CkanUI.Plugin.Kos.UI;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.Kos.Tests.UI;

/// <summary>
/// 「kOS 脚本」页的视图模型：加载/空态、脏标记、保存参数、新建/重命名/删除、
/// 未保存切换三选一、读取失败保留正文与 Dispose 幂等。
/// 全部走内存服务，不碰 Steam 下的真实 Ships/Script；视图由 XAML 编译校验。
/// </summary>
[TestFixture]
public sealed class KosScriptPageViewModelTests
{
    private static readonly DateTime FixedUtc = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private FakeKosScriptService _scripts = null!;
    private FakeInstanceService _instances = null!;

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeKosScriptService();
        _instances = new FakeInstanceService
        {
            Instances = [CreateInstance(_scripts.GameDir)],
            Current = CreateInstance(_scripts.GameDir),
        };
    }

    [Test]
    public async Task 初始加载_按名称排序选中首个脚本并载入正文()
    {
        _scripts.AddScript("zeta.ks", "print z.");
        _scripts.AddScript("boot.ks", "print boot.", isBoot: true, lastWriteUtc: FixedUtc);
        _scripts.AddScript("alpha.ks", "print a.\nprint aa.", lastWriteUtc: FixedUtc);

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.Scripts.Select(row => row.Name),
                Is.EqualTo(new[] { "alpha.ks", "boot.ks", "zeta.ks" }));
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo("print a.\nprint aa."));
            Assert.That(page.HasDocument, Is.True);
            Assert.That(page.HasSelection, Is.True);
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.DirtyText, Is.EqualTo("已保存"));
            Assert.That(page.LineCountText, Is.EqualTo("行数 2"));
            Assert.That(page.CharacterCountText, Is.EqualTo("字符 18"));
            Assert.That(page.DocumentName, Is.EqualTo("alpha.ks"));
            Assert.That(page.DocumentPath,
                Is.EqualTo(Path.Combine(_scripts.ScriptDirectory, "alpha.ks")));
            Assert.That(page.NewLineText, Is.EqualTo("换行 CRLF"));
            Assert.That(page.EncodingText, Is.EqualTo("编码 UTF-8"));
            Assert.That(page.Scripts[0].LineCountText, Is.EqualTo("2 行"));
            Assert.That(page.Scripts[0].DetailText, Does.Contain("2 行"));
            Assert.That(page.Scripts.Single(row => row.Name == "boot.ks").IsBoot, Is.True);
            Assert.That(page.Scripts.Single(row => row.Name == "alpha.ks").IsBoot, Is.False);
            Assert.That(_scripts.LoadCalls.Select(call => call.Name), Is.EqualTo(new[] { "alpha.ks" }));
        });
    }

    [Test]
    public void 空态_没有选择实例时标题与下一步独立()
    {
        _instances.Current = null;

        using var page = CreatePage();
        page.Start();

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.HasDocument, Is.False);
            Assert.That(page.EmptyTitle, Is.EqualTo("还没有选择游戏实例"));
            Assert.That(page.EmptyDetail, Does.Contain("「实例」页"));
            Assert.That(page.ShowEmptyAction, Is.False);
            Assert.That(page.Scripts, Is.Empty);
            Assert.That(_scripts.LoadCatalogCount, Is.Zero);
        });
    }

    [Test]
    public async Task 空态_未安装kOS时不能新建并指向GameData()
    {
        _scripts.ModInstalled = false;
        _scripts.DirectoryExists = false;

        using var page = CreatePage();
        page.Start();
        await WaitForEmptyAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.EmptyTitle, Is.EqualTo("这个实例没有安装 kOS"));
            Assert.That(page.EmptyDetail, Does.Contain("GameData"));
            Assert.That(page.EmptyDetail, Does.Contain("kOS"));
            Assert.That(page.ModStatusText, Does.Contain("未安装"));
            Assert.That(page.ShowEmptyAction, Is.False);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.HasDocument, Is.False);
        });
    }

    [Test]
    public async Task 空态_已安装但没有ShipsScript目录时可新建()
    {
        _scripts.ModInstalled = true;
        _scripts.DirectoryExists = false;

        using var page = CreatePage();
        page.Start();
        await WaitForEmptyAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.EmptyTitle, Is.EqualTo("kOS 已安装，但没有 Ships/Script 目录"));
            Assert.That(page.EmptyDetail, Does.Contain(_scripts.ScriptDirectory));
            Assert.That(page.EmptyDetail, Does.Contain("新建"));
            Assert.That(page.ModStatusText, Does.Contain("已安装"));
            Assert.That(page.ShowEmptyAction, Is.True);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.ScriptDirectory, Is.EqualTo(_scripts.ScriptDirectory));
        });
    }

    [Test]
    public async Task 空态_目录存在但没有脚本时可新建()
    {
        _scripts.ModInstalled = true;
        _scripts.DirectoryExists = true;

        using var page = CreatePage();
        page.Start();
        await WaitForEmptyAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.EmptyTitle, Is.EqualTo("Archive 卷里还没有脚本"));
            Assert.That(page.EmptyDetail, Does.Contain(_scripts.ScriptDirectory));
            Assert.That(page.EmptyDetail, Does.Contain(".ks"));
            Assert.That(page.ShowEmptyAction, Is.True);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.Scripts, Is.Empty);
            Assert.That(page.HasDocument, Is.False);
        });
    }

    [Test]
    public void 空态_服务缺失时明确提示而不是伪造脚本()
    {
        using var page = new KosScriptPageViewModel(_instances, null);
        page.Start();

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.EmptyTitle, Is.EqualTo("脚本服务未就绪"));
            Assert.That(page.EmptyDetail, Does.Contain("IKosScriptService"));
            Assert.That(page.ShowEmptyAction, Is.False);
            Assert.That(page.Scripts, Is.Empty);
            Assert.That(page.HasDocument, Is.False);
        });
    }

    [Test]
    public async Task 构造_只注册实例服务时页面也能工作()
    {
        // 宿主只注册 IInstanceService 时会走单参数构造；这里用真实 KosScriptService
        // 读一个不存在的临时目录，只做 Directory.Exists 探测，不写任何文件。
        using var page = new KosScriptPageViewModel(_instances);
        page.Start();
        await WaitForEmptyAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.EmptyTitle, Is.EqualTo("这个实例没有安装 kOS"));
            Assert.That(page.EmptyDetail, Does.Contain("GameData"));
            Assert.That(page.EmptyDetail, Does.Contain("kOS"));
            Assert.That(page.Scripts, Is.Empty);
        });
    }

    [Test]
    public async Task 编辑器脏标记_改回原样自动归零()
    {
        _scripts.AddScript("alpha.ks", "print a.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        page.EditorText = "print a.\nprint b.";
        Assert.Multiple(() =>
        {
            Assert.That(page.IsDirty, Is.True);
            Assert.That(page.HasUnsavedChanges, Is.True);
            Assert.That(page.DirtyText, Is.EqualTo("未保存"));
            Assert.That(page.LineCountText, Is.EqualTo("行数 2"));
            Assert.That(page.SaveCommand.CanExecute(null), Is.True);
            Assert.That(page.DiscardChangesCommand.CanExecute(null), Is.True);
        });

        page.EditorText = "print a.";
        Assert.Multiple(() =>
        {
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.DirtyText, Is.EqualTo("已保存"));
            Assert.That(page.SaveCommand.CanExecute(null), Is.False);
            Assert.That(page.DiscardChangesCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task 刷新_有未保存修改时只弹确认条不重扫目录()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        var loadsBefore = _scripts.LoadCatalogCount;
        const string draft = "print draft.";
        page.EditorText = draft;

        await page.RefreshCatalogAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowUnsavedPrompt, Is.True);
            Assert.That(page.UnsavedPromptText, Does.Contain("刷新"));
            Assert.That(page.UnsavedSaveButtonText, Is.EqualTo("保存并继续"));
            Assert.That(page.EditorText, Is.EqualTo(draft));
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.IsDirty, Is.True);
            Assert.That(_scripts.LoadCatalogCount, Is.EqualTo(loadsBefore),
                "有未保存修改时刷新只能挂起，不能先重扫目录");
        });

        page.CancelPendingAction();
        Assert.That(page.EditorText, Is.EqualTo(draft));
    }

    [Test]
    public async Task 重新挂载_有未保存修改时保留原稿并等待确认()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        const string draft = "print still here.";
        page.EditorText = draft;

        // 视图离开可视树再回来：Stop 不写盘、不清稿，Start 走受保护的 Refresh。
        page.Stop();
        page.Start();

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowUnsavedPrompt, Is.True);
            Assert.That(page.EditorText, Is.EqualTo(draft));
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.IsDirty, Is.True);
        });

        page.CancelPendingAction();
        Assert.That(page.EditorText, Is.EqualTo(draft));
    }

    [Test]
    public async Task 未保存时_新建重命名删除都拒绝执行且不丢正文()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        const string draft = "print draft.";
        page.EditorText = draft;

        page.NewScript();
        Assert.Multiple(() =>
        {
            Assert.That(page.IsNamePromptOpen, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("未保存"));
            Assert.That(_scripts.CreateCalls, Is.Empty);
        });

        page.RenameScript();
        Assert.Multiple(() =>
        {
            Assert.That(page.IsNamePromptOpen, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("未保存"));
            Assert.That(_scripts.RenameCalls, Is.Empty);
        });

        page.DeleteScript();
        Assert.Multiple(() =>
        {
            Assert.That(page.IsDeletePromptOpen, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("未保存"));
            Assert.That(_scripts.DeleteCalls, Is.Empty);
            Assert.That(page.EditorText, Is.EqualTo(draft));
            Assert.That(page.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task 刷新_目录变空时清掉旧列表与旧正文()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);
        Assert.That(page.Scripts, Has.Count.EqualTo(1));

        _scripts.ModInstalled = true;
        _scripts.DirectoryExists = false;
        await page.RefreshCatalogAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowEmptyState, Is.True);
            Assert.That(page.ShowContent, Is.False);
            Assert.That(page.EmptyTitle, Is.EqualTo("kOS 已安装，但没有 Ships/Script 目录"));
            Assert.That(page.Scripts, Is.Empty);
            Assert.That(page.ScriptCountText, Is.EqualTo("0 个脚本"));
            Assert.That(page.HasDocument, Is.False);
            Assert.That(page.EditorText, Is.Empty);
            Assert.That(page.IsDirty, Is.False);
        });
    }

    [Test]
    public async Task 保存_调用参数带名字正文与打开时的LastWriteUtc()
    {
        _scripts.AddScript("alpha.ks", "print a.", lastWriteUtc: FixedUtc);

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        page.EditorText = "print changed.\nprint next.";
        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_scripts.SaveCalls, Has.Count.EqualTo(1));
            var call = _scripts.SaveCalls[0];
            Assert.That(call.GameDir, Is.EqualTo(_scripts.GameDir));
            Assert.That(call.Name, Is.EqualTo("alpha.ks"));
            Assert.That(call.Content, Is.EqualTo("print changed.\nprint next."));
            Assert.That(call.ExpectedLastWriteUtc, Is.EqualTo(FixedUtc));
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.ErrorMessage, Is.Empty);
            Assert.That(page.StatusMessage, Does.Contain("已保存"));
            Assert.That(_scripts.LoadCalls, Has.Count.EqualTo(2),
                "保存后必须重新读一次，下一次 expectedLastWriteUtc 才有新基线");
        });
    }

    [Test]
    public async Task 保存失败_正文与脏标记都保留并显示可读错误()
    {
        _scripts.AddScript("alpha.ks", "print a.", lastWriteUtc: FixedUtc);
        _scripts.SaveFailureMessage = "脚本已被外部修改，已拒绝覆盖。";

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        const string draft = "print draft.";
        page.EditorText = draft;
        await page.SaveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.EditorText, Is.EqualTo(draft));
            Assert.That(page.IsDirty, Is.True);
            Assert.That(page.ErrorMessage, Does.Contain("保存失败"));
            Assert.That(page.ErrorMessage, Does.Contain("外部修改"));
            Assert.That(page.StatusMessage, Is.Empty);
            Assert.That(_scripts.SaveCalls[0].Content, Is.EqualTo(draft));
        });
    }

    [Test]
    public async Task 新建_名字自动补ks并选中新脚本()
    {
        using var page = CreatePage();
        page.Start();
        await WaitForEmptyAsync(page);

        page.NewScript();
        Assert.That(page.IsNamePromptOpen, Is.True);

        page.NamePromptText = "mission";
        await page.ConfirmNamePromptAsync();
        await WaitUntilAsync(
            () => !page.IsBusy
                && page.SelectedScript?.Name == "mission.ks"
                && page.HasDocument,
            "新建后应选中 mission.ks");

        Assert.Multiple(() =>
        {
            Assert.That(_scripts.CreateCalls, Has.Count.EqualTo(1));
            Assert.That(_scripts.CreateCalls[0].GameDir, Is.EqualTo(_scripts.GameDir));
            Assert.That(_scripts.CreateCalls[0].Name, Is.EqualTo("mission.ks"));
            Assert.That(_scripts.CreateCalls[0].Content, Is.Empty);
            Assert.That(page.IsNamePromptOpen, Is.False);
            Assert.That(page.EditorText, Is.Empty);
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.DocumentName, Is.EqualTo("mission.ks"));
            Assert.That(page.StatusMessage, Does.Contain("已新建"));
        });
    }

    [Test]
    public async Task 重命名冲突_提示已存在并保留输入与原稿()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");
        _scripts.AddScript("beta.ks", "print beta.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        page.RenameScript();
        page.NamePromptText = "beta.ks";
        await page.ConfirmNamePromptAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_scripts.RenameCalls, Has.Count.EqualTo(1));
            Assert.That(_scripts.RenameCalls[0].OldName, Is.EqualTo("alpha.ks"));
            Assert.That(_scripts.RenameCalls[0].NewName, Is.EqualTo("beta.ks"));
            Assert.That(page.IsNamePromptOpen, Is.True);
            Assert.That(page.NamePromptText, Is.EqualTo("beta.ks"));
            Assert.That(page.ErrorMessage, Does.Contain("重命名失败"));
            Assert.That(page.ErrorMessage, Does.Contain("已存在"));
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo("print alpha."));
            Assert.That(page.IsDirty, Is.False);
        });
    }

    [Test]
    public async Task 删除_确认之前不调用服务_确认后删除并选中下一个()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");
        _scripts.AddScript("beta.ks", "print beta.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        page.DeleteScript();
        Assert.Multiple(() =>
        {
            Assert.That(page.IsDeletePromptOpen, Is.True);
            Assert.That(page.DeletePromptText, Does.Contain("alpha.ks"));
            Assert.That(_scripts.DeleteCalls, Is.Empty);
        });

        page.CancelDelete();
        Assert.Multiple(() =>
        {
            Assert.That(page.IsDeletePromptOpen, Is.False);
            Assert.That(_scripts.DeleteCalls, Is.Empty);
            Assert.That(page.EditorText, Is.EqualTo("print alpha."));
        });

        page.DeleteScript();
        await page.ConfirmDeleteAsync();
        await WaitUntilAsync(
            () => !page.IsBusy && page.SelectedScript?.Name == "beta.ks",
            "删除后应选中 beta.ks");

        Assert.Multiple(() =>
        {
            Assert.That(_scripts.DeleteCalls, Has.Count.EqualTo(1));
            Assert.That(_scripts.DeleteCalls[0].Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.IsDeletePromptOpen, Is.False);
            Assert.That(page.EditorText, Is.EqualTo("print beta."));
            Assert.That(page.StatusMessage, Does.Contain("已删除"));
        });
    }

    [Test]
    public async Task 未保存切换_取消后原稿一字未动()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");
        _scripts.AddScript("beta.ks", "print beta.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        const string draft = "print alpha draft.";
        page.EditorText = draft;
        page.SelectedScript = page.Scripts.Single(row => row.Name == "beta.ks");

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowUnsavedPrompt, Is.True);
            Assert.That(page.UnsavedPromptText, Does.Contain("alpha.ks"));
            Assert.That(page.UnsavedSaveButtonText, Is.EqualTo("保存并切换"));
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo(draft));
            Assert.That(page.IsDirty, Is.True);
            Assert.That(_scripts.SaveCalls, Is.Empty);
        });

        page.CancelPendingAction();

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowUnsavedPrompt, Is.False);
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo(draft));
            Assert.That(page.IsDirty, Is.True);
            Assert.That(page.StatusMessage, Does.Contain("已取消"));
        });
    }

    [Test]
    public async Task 未保存切换_保存并切换先保存当前脚本再载入目标()
    {
        _scripts.AddScript("alpha.ks", "print alpha.", lastWriteUtc: FixedUtc);
        _scripts.AddScript("beta.ks", "print beta.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        const string draft = "print alpha draft.";
        page.EditorText = draft;
        page.SelectedScript = page.Scripts.Single(row => row.Name == "beta.ks");
        await page.SaveAndContinueAsync();
        await WaitUntilAsync(
            () => !page.IsBusy && page.SelectedScript?.Name == "beta.ks",
            "保存后应切到 beta.ks");

        Assert.Multiple(() =>
        {
            Assert.That(_scripts.SaveCalls, Has.Count.EqualTo(1));
            Assert.That(_scripts.SaveCalls[0].Name, Is.EqualTo("alpha.ks"));
            Assert.That(_scripts.SaveCalls[0].Content, Is.EqualTo(draft));
            Assert.That(_scripts.SaveCalls[0].ExpectedLastWriteUtc, Is.EqualTo(FixedUtc));
            Assert.That(page.ShowUnsavedPrompt, Is.False);
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("beta.ks"));
            Assert.That(page.EditorText, Is.EqualTo("print beta."));
            Assert.That(page.IsDirty, Is.False);
        });
    }

    [Test]
    public async Task 未保存切换_保存失败时留在确认条且原稿不丢()
    {
        _scripts.AddScript("alpha.ks", "print alpha.", lastWriteUtc: FixedUtc);
        _scripts.AddScript("beta.ks", "print beta.");
        _scripts.SaveFailureMessage = "脚本已被外部修改，已拒绝覆盖。";

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        const string draft = "print alpha draft.";
        page.EditorText = draft;
        page.SelectedScript = page.Scripts.Single(row => row.Name == "beta.ks");
        await page.SaveAndContinueAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowUnsavedPrompt, Is.True);
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo(draft));
            Assert.That(page.IsDirty, Is.True);
            Assert.That(page.ErrorMessage, Does.Contain("保存失败"));
            Assert.That(_scripts.SaveCalls, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task 未保存切换_放弃修改成功载入目标()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");
        _scripts.AddScript("beta.ks", "print beta.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        page.EditorText = "print alpha draft.";
        page.SelectedScript = page.Scripts.Single(row => row.Name == "beta.ks");
        await page.DiscardAndContinueAsync();
        await WaitUntilAsync(
            () => !page.IsBusy && page.SelectedScript?.Name == "beta.ks",
            "放弃修改后应切到 beta.ks");

        Assert.Multiple(() =>
        {
            Assert.That(_scripts.SaveCalls, Is.Empty);
            Assert.That(page.ShowUnsavedPrompt, Is.False);
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("beta.ks"));
            Assert.That(page.EditorText, Is.EqualTo("print beta."));
            Assert.That(page.IsDirty, Is.False);
        });
    }

    [Test]
    public async Task 未保存切换_放弃修改后目标读取失败也不会把草稿留回编辑器()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");
        _scripts.AddScript("beta.ks", "print beta.");
        _scripts.LoadFailures.Add("beta.ks");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        const string draft = "print alpha draft.";
        page.EditorText = draft;
        page.SelectedScript = page.Scripts.Single(row => row.Name == "beta.ks");
        await page.DiscardAndContinueAsync();
        await WaitUntilAsync(
            () => !page.IsBusy && page.ErrorMessage.Length > 0,
            "目标读取失败后应给出错误");

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowUnsavedPrompt, Is.False);
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo("print alpha."));
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("读取「beta.ks」失败"));
            Assert.That(page.ErrorMessage, Does.Contain("没有改动"));
        });
    }

    [Test]
    public async Task 读取失败_保留原脚本正文并显示可读错误()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");
        _scripts.AddScript("beta.ks", "print beta.");
        _scripts.LoadFailures.Add("beta.ks");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        page.SelectedScript = page.Scripts.Single(row => row.Name == "beta.ks");
        await WaitUntilAsync(
            () => !page.IsBusy && _scripts.LoadCalls.Count >= 2,
            "读取 beta.ks 的失败结果应回到页面");

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo("print alpha."));
            Assert.That(page.IsDirty, Is.False);
            Assert.That(page.ShowUnsavedPrompt, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("读取「beta.ks」失败"));
            Assert.That(page.ErrorMessage, Does.Contain("没有改动"));
        });
    }

    [Test]
    public async Task 搜索_过滤列表但保留当前选中行()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");
        _scripts.AddScript("beta.ks", "print beta.");
        _scripts.AddScript("zeta.ks", "print zeta.");

        using var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        page.SearchText = "zeta";

        Assert.Multiple(() =>
        {
            Assert.That(page.Scripts.Select(row => row.Name), Is.EqualTo(new[] { "alpha.ks", "zeta.ks" }));
            Assert.That(page.ScriptCountText, Is.EqualTo("显示 2 / 3 个"));
            Assert.That(page.SelectedScript?.Name, Is.EqualTo("alpha.ks"));
            Assert.That(page.EditorText, Is.EqualTo("print alpha."));
        });
    }

    [Test]
    public async Task Dispose_幂等并取消实例事件订阅()
    {
        _scripts.AddScript("alpha.ks", "print alpha.");

        var page = CreatePage();
        page.Start();
        await WaitForDocumentAsync(page);

        var loadsBeforeDispose = _scripts.LoadCatalogCount;
        page.Dispose();
        page.Dispose();
        _instances.RaiseCurrentChanged(CreateInstance(Path.Combine(_scripts.GameDir, "other")));
        await Task.Delay(50);
        page.Start();
        await Task.Delay(30);

        Assert.That(_scripts.LoadCatalogCount, Is.EqualTo(loadsBeforeDispose));
    }

    private KosScriptPageViewModel CreatePage() => KosScriptPageViewModel.CreateForTests(_scripts, _instances);

    private static InstanceInfo CreateInstance(string gameDir)
        => new("测试实例", gameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 10);

    private static async Task WaitForDocumentAsync(KosScriptPageViewModel page)
    {
        await WaitUntilAsync(() => !page.IsBusy && page.HasDocument, "初始脚本应载入正文");
    }

    private static async Task WaitForEmptyAsync(KosScriptPageViewModel page)
    {
        await WaitUntilAsync(
            () => !page.IsBusy
                && page.ShowEmptyState
                && page.EmptyTitle != "正在扫描 Archive 卷…",
            "空态应完成扫描并给出标题");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string because)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"超时（5 秒）：{because}");
    }
}
