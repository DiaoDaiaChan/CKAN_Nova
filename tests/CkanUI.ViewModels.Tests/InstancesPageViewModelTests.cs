using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;
using CkanUI.ViewModels.Dialogs.InstanceDialogs;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 实例页视图模型的 headless 测试（06 篇 VM-04「各页面 headless 可测」、UI-04 / UI-05 / UI-32）。
/// </summary>
/// <remarks>
/// 全部依赖都用 <see cref="InstancesFakeService"/> 等替身注入，不碰真实磁盘、真实对话框与真实进程，
/// 覆盖：列表投影、切换成功/失败/锁定/异常、遗忘与克隆的二次确认、表单校验、Steam 导入、外壳动作。
/// </remarks>
[TestFixture]
public sealed class InstancesPageViewModelTests
{
    private const string MainDir = "D:/Games/KSP-Main";

    private const string SecondDir = "D:/Games/KSP-Second";

    [Test]
    public void 空列表_显示空状态与添加引导()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());

        Assert.That(h.Vm.HasRows, Is.False);
        Assert.That(h.Vm.ShowEmptyState, Is.True);
        Assert.That(h.Vm.CountText, Is.EqualTo("共 0 个实例"));
        Assert.That(h.Vm.EmptyStateTitle, Is.EqualTo("还没有游戏实例"));
        Assert.That(h.Vm.EmptyStateActionText, Is.EqualTo("添加实例"));
        Assert.That(h.Vm.SelectedRow, Is.Null);
        Assert.That(h.Vm.CurrentSummary, Is.EqualTo("当前实例：未选择"));
    }

    [Test]
    public void 列表投影_当前实例置顶并默认选中()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Second", SecondDir, null, false, false, null),
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 42),
            },
            "Main");

        Assert.That(h.Vm.Rows.Count, Is.EqualTo(2));
        Assert.That(h.Vm.Rows[0].Name, Is.EqualTo("Main"));
        Assert.That(h.Vm.SelectedRow?.Name, Is.EqualTo("Main"));
        Assert.That(h.Vm.SelectedRow?.IsCurrent, Is.True);
        Assert.That(h.Vm.CurrentSummary, Is.EqualTo("当前实例：Main (1.12.5)"));
        Assert.That(h.Vm.CountText, Is.EqualTo("共 2 个实例"));
        Assert.That(h.Vm.SelectionVersionText, Is.EqualTo("1.12.5"));
        Assert.That(h.Vm.SelectionModuleText, Is.EqualTo("42 个模组"));
    }

    [Test]
    public async Task 切换成功_更新当前实例并提示()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3),
            },
            "Main");

        var target = h.Vm.Rows.First(row => row.Name == "Second");
        await h.Vm.SwitchToAsync(target);

        Assert.That(h.Service.SwitchCalls, Is.EqualTo(1));
        Assert.That(h.Service.LastSwitchName, Is.EqualTo("Second"));
        Assert.That(h.Service.Current?.Name, Is.EqualTo("Second"));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已切换到「Second」。"));
        Assert.That(h.Vm.HasError, Is.False);
        Assert.That(h.Vm.CurrentSummary, Is.EqualTo("当前实例：Second (1.12.5)"));
    }

    [Test]
    public async Task 切换失败_错误收口且停留原实例()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3),
            },
            "Main");
        h.Service.SwitchResult = OperationResult<InstanceInfo>.Fail("注册表写入被拒绝。");

        var target = h.Vm.Rows.First(row => row.Name == "Second");
        await h.Vm.SwitchToAsync(target);

        Assert.That(h.Vm.ErrorText, Is.EqualTo("注册表写入被拒绝。（不可重试）"));
        Assert.That(h.Vm.StatusText, Is.EqualTo("切换失败，仍停留在原实例。"));
        Assert.That(h.Service.Current?.Name, Is.EqualTo("Main"));
        Assert.That(h.Vm.CurrentSummary, Is.EqualTo("当前实例：Main (1.12.5)"));
    }

    [Test]
    public async Task 切换锁定实例_不发服务调用且错误不可重试()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Locked", SecondDir, "1.12.5", false, true, 3),
            },
            "Main");

        var target = h.Vm.Rows.First(row => row.Name == "Locked");
        await h.Vm.SwitchToAsync(target);

        Assert.That(h.Service.SwitchCalls, Is.EqualTo(0));
        Assert.That(h.Vm.HasError, Is.True);
        Assert.That(h.Vm.ErrorText, Does.Contain("被其他进程锁定"));
        Assert.That(h.Vm.ErrorText, Does.Contain("不可重试"));
        Assert.That(h.Vm.ErrorDetail, Is.EqualTo(SecondDir));
    }

    [Test]
    public async Task 切换当前实例_只提示不调用服务()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");

        await h.Vm.SwitchToAsync(h.Vm.SelectedRow);

        Assert.That(h.Service.SwitchCalls, Is.EqualTo(0));
        Assert.That(h.Vm.StatusText, Is.EqualTo("「Main」已经是当前实例。"));
    }

    [Test]
    public async Task 遗忘_确认对话框返回否_不做任何动作()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3),
            },
            "Main");
        h.Dialogs.ConfirmResult = false;

        var target = h.Vm.Rows.First(row => row.Name == "Second");
        await h.Vm.ForgetSelectedAsync(target);

        Assert.That(h.Dialogs.ConfirmCalls, Is.EqualTo(1));
        Assert.That(h.Dialogs.ConfirmTitles[0], Is.EqualTo("遗忘实例"));
        Assert.That(h.Service.RemoveCalls, Is.EqualTo(0));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已取消遗忘。"));
        Assert.That(h.Vm.Rows.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task 遗忘当前实例_被阻止且不弹确认()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");

        await h.Vm.ForgetSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.Dialogs.ConfirmCalls, Is.EqualTo(0));
        Assert.That(h.Service.RemoveCalls, Is.EqualTo(0));
        Assert.That(h.Vm.ErrorText, Does.Contain("不能遗忘当前正在使用的实例"));
    }

    [Test]
    public async Task 遗忘_确认后移除记录并提示不删文件()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3),
            },
            "Main");

        var target = h.Vm.Rows.First(row => row.Name == "Second");
        await h.Vm.ForgetSelectedAsync(target);

        Assert.That(h.Service.RemoveCalls, Is.EqualTo(1));
        Assert.That(h.Service.LastRemoveName, Is.EqualTo("Second"));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已遗忘实例「Second」，游戏目录未做任何改动。"));
        Assert.That(h.Vm.Rows.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task 克隆_目标目录已存在且用户取消_不调用克隆()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");
        h.Dialogs.ConfirmResult = false;

        h.Vm.BeginCloneCommand.Execute(null);
        Assert.That(h.Vm.HasForm, Is.True);
        Assert.That(h.Vm.Form!.Mode, Is.EqualTo(InstanceFormMode.Clone));

        await h.Vm.SubmitFormAsync(h.Vm.Form!);

        Assert.That(h.Dialogs.ConfirmCalls, Is.EqualTo(1));
        Assert.That(h.Dialogs.ConfirmTitles[0], Is.EqualTo("目标目录已存在"));
        Assert.That(h.Service.CloneCalls, Is.EqualTo(0));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已取消克隆：目标目录已存在。"));
        Assert.That(h.Vm.HasForm, Is.True);
    }

    [Test]
    public async Task 克隆_目标目录已存在但用户确认_执行克隆()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");

        h.Vm.BeginCloneCommand.Execute(null);
        await h.Vm.SubmitFormAsync(h.Vm.Form!);

        Assert.That(h.Service.CloneCalls, Is.EqualTo(1));
        Assert.That(h.Service.LastCloneSource, Is.EqualTo("Main"));
        Assert.That(h.Service.LastCloneName, Is.EqualTo("Main - 副本"));
        Assert.That(h.Vm.HasForm, Is.False);
        Assert.That(h.Vm.StatusText, Is.EqualTo("已克隆为「Main - 副本」。"));
    }

    [Test]
    public async Task 克隆_目标目录不存在_不打扰用户直接克隆()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");
        h.Probe.Missing.Add(InstancesProjection.CloneDestination(MainDir, "Main - 副本"));

        h.Vm.BeginCloneCommand.Execute(null);
        await h.Vm.SubmitFormAsync(h.Vm.Form!);

        Assert.That(h.Dialogs.ConfirmCalls, Is.EqualTo(0));
        Assert.That(h.Service.CloneCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task 克隆_名称为空_表单校验拦截且不发请求()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");

        h.Vm.BeginCloneCommand.Execute(null);
        var form = h.Vm.Form!;
        form.Name = string.Empty;
        await form.SubmitCommand.ExecuteAsync(null);

        Assert.That(form.ValidationError, Is.EqualTo("实例名称不能为空。"));
        Assert.That(h.Service.CloneCalls, Is.EqualTo(0));
        Assert.That(h.Vm.HasForm, Is.True);
    }

    [Test]
    public async Task 克隆_名称与其他实例重名_表单校验拦截()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3),
            },
            "Main");

        h.Vm.BeginCloneCommand.Execute(null);
        var form = h.Vm.Form!;
        form.Name = "second";
        await form.SubmitCommand.ExecuteAsync(null);

        Assert.That(form.ValidationError, Does.Contain("已存在同名实例"));
        Assert.That(h.Service.CloneCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task 切换抛异常_收口到错误条并上报()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3),
            },
            "Main");
        h.Service.SwitchThrows = new InvalidOperationException("注册表句柄被拒绝。");

        await h.Vm.SwitchToAsync(h.Vm.Rows.First(row => row.Name == "Second"));

        Assert.That(h.Errors.ReportCount, Is.EqualTo(1));
        Assert.That(h.Errors.LastContext, Is.EqualTo("切换实例"));
        Assert.That(h.Vm.ErrorText, Does.Contain("切换实例"));
        Assert.That(h.Vm.ErrorText, Does.Contain("不可重试"));
        Assert.That(h.Vm.IsBusy, Is.False);
        Assert.That(h.Service.Current?.Name, Is.EqualTo("Main"));
    }

    [Test]
    public void 添加_目录已注册_给出可读错误()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");

        h.Vm.AddFromPath(MainDir + "/");

        Assert.That(h.Vm.ErrorText, Does.Contain("该目录已经是实例列表中的实例"));
        Assert.That(h.Vm.HasForm, Is.False);
    }

    [Test]
    public void 添加_目录不存在_给出不可重试错误()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());
        h.Probe.Missing.Add("D:/Games/Nope");

        h.Vm.AddFromPath("D:/Games/Nope");

        Assert.That(h.Vm.ErrorText, Is.EqualTo("目录不存在：D:/Games/Nope（不可重试）"));
        Assert.That(h.Vm.HasForm, Is.False);
    }

    [Test]
    public void 添加_有效目录_打开表单且名称默认取目录名()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());

        h.Vm.AddFromPath("D:/Games/KSP-New");

        Assert.That(h.Vm.HasForm, Is.True);
        Assert.That(h.Vm.Form!.Mode, Is.EqualTo(InstanceFormMode.Add));
        Assert.That(h.Vm.Form!.Name, Is.EqualTo("KSP-New"));
        Assert.That(h.Vm.Form!.Path, Is.EqualTo("D:/Games/KSP-New"));
    }

    [Test]
    public async Task 添加表单提交_调用服务并刷新列表()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());

        h.Vm.AddFromPath("D:/Games/KSP-New");
        await h.Vm.SubmitFormAsync(h.Vm.Form!);

        Assert.That(h.Service.AddCalls, Is.EqualTo(1));
        Assert.That(h.Service.LastAddPath, Is.EqualTo("D:/Games/KSP-New"));
        Assert.That(h.Vm.Rows.Count, Is.EqualTo(1));
        Assert.That(h.Vm.HasForm, Is.False);
        Assert.That(h.Vm.StatusText, Does.Contain("已添加实例"));
    }

    [Test]
    public void 服务事件_实例列表变化时自动重载()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");
        h.Vm.Activate();

        h.Service.Seed(
            new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
            new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3));
        h.Service.RaiseInstancesChanged();
        Assert.That(h.Vm.Rows.Count, Is.EqualTo(2));

        h.Service.RaiseCurrentChanged();
        Assert.That(h.Vm.Rows[0].Name, Is.EqualTo("Main"));

        h.Vm.Deactivate();
        h.Service.Seed(new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10));
        h.Service.RaiseInstancesChanged();
        Assert.That(h.Vm.Rows.Count, Is.EqualTo(2), "停用后不应再响应服务事件。");
    }

    [Test]
    public async Task 重命名_确认后给出契约缺失的阻塞提示()
    {
        var h = new Harness(
            new[]
            {
                new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10),
                new InstanceInfo("Second", SecondDir, "1.12.5", false, false, 3),
            },
            "Main");

        h.Vm.SelectedRow = h.Vm.Rows.First(row => row.Name == "Second");
        h.Vm.BeginRenameCommand.Execute(null);
        Assert.That(h.Vm.Form!.Mode, Is.EqualTo(InstanceFormMode.Rename));
        Assert.That(h.Vm.Form!.Name, Is.EqualTo("Second"));

        h.Vm.Form!.Name = "SecondRenamed";
        await h.Vm.SubmitFormAsync(h.Vm.Form!);

        Assert.That(h.Dialogs.ConfirmTitles[0], Is.EqualTo("重命名实例"));
        Assert.That(h.Service.AddCalls, Is.EqualTo(0));
        Assert.That(h.Service.RemoveCalls, Is.EqualTo(0));
        Assert.That(h.Vm.HasForm, Is.False);
        Assert.That(h.Vm.ErrorText, Does.Contain("RenameAsync"));
    }

    [Test]
    public async Task Steam导入_多候选时按选择结果添加()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());
        h.Steam.Found = new[] { "D:/Steam/A", "D:/Steam/B" };
        h.Dialogs.SelectResult = 1;

        await h.Vm.ImportFromSteamCommand.ExecuteAsync(null);

        Assert.That(h.Steam.Calls, Is.EqualTo(1));
        Assert.That(h.Dialogs.SelectCalls, Is.EqualTo(1));
        Assert.That(h.Service.AddCalls, Is.EqualTo(1));
        Assert.That(h.Service.LastAddPath, Is.EqualTo("D:/Steam/B"));
        Assert.That(h.Vm.StatusText, Does.Contain("已从 Steam 库导入"));
    }

    [Test]
    public async Task Steam导入_确认取消_不添加实例()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());
        h.Steam.Found = new[] { "D:/Steam/A" };
        h.Dialogs.ConfirmResult = false;

        await h.Vm.ImportFromSteamCommand.ExecuteAsync(null);

        Assert.That(h.Dialogs.SelectCalls, Is.EqualTo(0));
        Assert.That(h.Service.AddCalls, Is.EqualTo(0));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已取消导入。"));
    }

    [Test]
    public async Task Steam导入_未找到候选_给出可读提示()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());

        await h.Vm.ImportFromSteamCommand.ExecuteAsync(null);

        Assert.That(h.Service.AddCalls, Is.EqualTo(0));
        Assert.That(h.Vm.ErrorText, Is.EqualTo("未在 Steam 库中找到新的 KSP 安装目录。"));
    }

    [Test]
    public async Task 打开目录_调用外壳并提示路径()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");

        await h.Vm.OpenSelectedFolderAsync(h.Vm.SelectedRow);

        Assert.That(h.Shell.OpenFolderCalls, Is.EqualTo(1));
        Assert.That(h.Shell.LastOpened, Is.EqualTo(MainDir));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已在文件管理器中打开：" + MainDir));
    }

    [Test]
    public async Task 启动游戏_锁定实例被拦截()
    {
        var h = new Harness(new[] { new InstanceInfo("Locked", MainDir, "1.12.5", true, true, 10) }, "Locked");

        await h.Vm.LaunchSelectedAsync(h.Vm.SelectedRow);

        Assert.That(h.Shell.LaunchCalls, Is.EqualTo(0));
        Assert.That(h.Vm.ErrorText, Does.Contain("不能重复启动"));
    }

    [Test]
    public async Task 刷新_调用服务并更新状态()
    {
        var h = new Harness(new[] { new InstanceInfo("Main", MainDir, "1.12.5", true, false, 10) }, "Main");

        await h.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(h.Service.RefreshCalls, Is.EqualTo(1));
        Assert.That(h.Vm.StatusText, Is.EqualTo("实例列表已刷新。"));
        Assert.That(h.Vm.HasError, Is.False);
    }

    [Test]
    public async Task 无选中项_切换与遗忘给出提示()
    {
        var h = new Harness(Array.Empty<InstanceInfo>());

        await h.Vm.SwitchToAsync(null);
        Assert.That(h.Vm.ErrorText, Is.EqualTo("请先选择一个实例。"));
        Assert.That(h.Service.SwitchCalls, Is.EqualTo(0));

        await h.Vm.ForgetSelectedAsync(null);
        Assert.That(h.Vm.ErrorText, Is.EqualTo("请先选择要遗忘的实例。"));
        Assert.That(h.Dialogs.ConfirmCalls, Is.EqualTo(0));
    }

    /// <summary>测试装配：把替身注入页面视图模型并完成一次投影。</summary>
    private sealed class Harness
    {
        public Harness(InstanceInfo[] items, string? current = null)
        {
            Service.Seed(items);
            if (current is not null)
            {
                Service.Current = items.FirstOrDefault(item => item.Name == current);
            }

            Vm = new InstancesPageViewModel(Service, Dialogs, Errors)
            {
                PathProbe = Probe,
                FolderPicker = Picker,
                ShellLauncher = Shell,
                SteamLibraries = Steam,
            };

            // 宿主能力是在构造之后注入的，因此注入完必须重新投影一次，路径标记才算数。
            Vm.Reload();
        }

        public InstancesFakeService Service { get; } = new();

        public InstancesFakeDialogs Dialogs { get; } = new();

        public InstancesFakeErrorReporter Errors { get; } = new();

        public InstancesFakePathProbe Probe { get; } = new();

        public InstancesFakeShellLauncher Shell { get; } = new();

        public InstancesFakeSteamSource Steam { get; } = new();

        public InstancesFakeFolderPicker Picker { get; } = new();

        public InstancesPageViewModel Vm { get; }
    }
}
