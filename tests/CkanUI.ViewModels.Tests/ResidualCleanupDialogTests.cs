using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Dialogs.ResidualCleanup;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 卸载残留清理对话框（12 篇 F-43 / 06 篇 UI-22）：勾选/全选、三个出口的返回值、中文错误文案。
/// </summary>
[TestFixture]
public sealed class ResidualCleanupDialogTests
{
    [Test]
    public async Task 加载后按可删除性默认勾选并给出汇总文案()
    {
        var service = new FakeCleanupService();
        service.Items.Add(Info("Leftover", files: 2, bytes: 120));
        service.Items.Add(Info("ManagedLeftover", blockReason: "目录内含有受管文件，请到模组页卸载对应模组。"));
        var vm = Vm(service);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.Rows, Has.Count.EqualTo(2));
        Assert.That(vm.Rows[0].IsSelected, Is.True, "可删除的默认勾上");
        Assert.That(vm.Rows[1].IsSelected, Is.False, "不可删除的不勾");
        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.CanDelete, Is.True);
        Assert.That(vm.HasRows, Is.True);
        Assert.That(vm.IsEmpty, Is.False);
        Assert.That(vm.SummaryText, Does.Contain("共 2 个残留目录"));
        Assert.That(vm.SummaryText, Does.Contain("1 个不能删除"));
        Assert.That(vm.SelectionText, Is.EqualTo("已勾选 1 个目录。"));
        Assert.That(vm.HasValidationError, Is.False);
    }

    [Test]
    public async Task 全选与取消全选只作用于可删除的行()
    {
        var service = new FakeCleanupService();
        service.Items.Add(Info("A"));
        service.Items.Add(Info("B"));
        service.Items.Add(Info("Blocked", blockReason: "这是符号链接，删它会连带删掉链接目标，已跳过。"));
        var vm = Vm(service);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ClearSelection();

        Assert.That(vm.SelectedCount, Is.EqualTo(0));
        Assert.That(vm.CanDelete, Is.False);
        Assert.That(vm.SelectionText, Is.EqualTo("尚未勾选任何目录。"));

        vm.SelectAll();

        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        Assert.That(vm.Rows[2].IsSelected, Is.False, "不可删除的行不许被全选勾上");
        Assert.That(vm.CanDelete, Is.True);
    }

    [Test]
    public async Task 删除勾选成功返回Delete出口并关窗()
    {
        var service = new FakeCleanupService();
        service.Items.Add(Info("Leftover"));
        service.Items.Add(Info("Other"));
        var vm = Vm(service);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(service.DeleteCalls, Has.Count.EqualTo(1));
        Assert.That(service.DeleteCalls[0], Is.EquivalentTo(new[] { "Leftover", "Other" }));
        Assert.That(vm.Result, Is.EqualTo(ResidualCleanupChoice.Delete));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.StatusText, Does.Contain("已删除 2 个目录"));
    }

    [Test]
    public async Task 删除勾选但一个都没勾时给出中文提示且不关窗()
    {
        var service = new FakeCleanupService();
        service.Items.Add(Info("Leftover"));
        var vm = Vm(service);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ClearSelection();

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(vm.ValidationError, Is.EqualTo("请先勾选要删除的残留目录。"));
        Assert.That(vm.IsClosed, Is.False);
        Assert.That(vm.Result, Is.EqualTo(ResidualCleanupChoice.Keep));
        Assert.That(service.DeleteCalls, Is.Empty, "没勾选就不该调用服务");
    }

    [Test]
    public async Task 全部保留返回Keep出口且不删任何东西()
    {
        var service = new FakeCleanupService();
        service.Items.Add(Info("Leftover"));
        var vm = Vm(service);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Result, Is.EqualTo(ResidualCleanupChoice.Keep));
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(service.DeleteCalls, Is.Empty);
    }

    [Test]
    public async Task 删除失败时给出中文可重试文案并刷新列表()
    {
        var service = new FakeCleanupService
        {
            DeleteResult = OperationResult<ResidualCleanupReport>.Fail(
                "有 1 个目录没能删除：Leftover",
                "可能被其它程序占用或权限不足，关闭占用后可以重试。",
                retryable: true),
        };
        service.Items.Add(Info("Leftover"));
        var vm = Vm(service);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(vm.IsClosed, Is.False, "失败不关窗，用户可以重试");
        Assert.That(vm.Result, Is.EqualTo(ResidualCleanupChoice.Keep));
        Assert.That(vm.ValidationError, Does.Contain("没能删除"));
        Assert.That(vm.ValidationError, Does.Contain("可以重试"));
        Assert.That(vm.HasErrorDetail, Is.True);
        Assert.That(vm.ErrorDetail, Does.Contain("占用"));
        Assert.That(service.ListCalls, Is.EqualTo(2), "失败后要重新扫一遍，别对着已删掉的目录再删一次");
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public async Task 打开目录在没有宿主实现时给出中文提示()
    {
        var service = new FakeCleanupService();
        service.Items.Add(Info("Leftover"));
        var vm = Vm(service);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.OpenDirectoryCommand.ExecuteAsync(vm.Rows[0]);

        Assert.That(vm.ValidationError, Is.EqualTo("当前宿主没有提供「打开目录」的能力。"));
        Assert.That(vm.IsClosed, Is.False);
    }

    [Test]
    public async Task 打开目录把绝对路径交给宿主委托()
    {
        var service = new FakeCleanupService();
        service.Items.Add(Info("Leftover"));
        var launcher = new FakeLauncher();
        var vm = Vm(service, gameDir: "C:/games/ksp-test");
        vm.ShellLauncher = launcher;
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.OpenDirectoryCommand.ExecuteAsync(vm.Rows[0]);

        Assert.That(launcher.Opened, Has.Count.EqualTo(1));
        Assert.That(
            launcher.Opened[0],
            Is.EqualTo(Path.Combine("C:/games/ksp-test", "GameData", "Leftover")));
        Assert.That(vm.StatusText, Does.Contain("已打开"));
        Assert.That(vm.HasValidationError, Is.False);
    }

    [Test]
    public async Task 没有残留时显示空态并禁止删除()
    {
        var service = new FakeCleanupService();
        var vm = Vm(service);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.HasRows, Is.False);
        Assert.That(vm.CanDelete, Is.False);
        Assert.That(vm.SummaryText, Is.EqualTo("没有发现可清理的残留目录。"));
    }

    [Test]
    public async Task 列出失败时保留中文错误与详情()
    {
        var service = new FakeCleanupService
        {
            ListOverride = OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Fail(
                "找不到 GameData 目录：C:/games/gone/GameData",
                "实例路径可能已被移动或删除，请先到实例页校正。",
                retryable: false),
        };
        var vm = Vm(service);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.HasValidationError, Is.True);
        Assert.That(vm.ValidationError, Does.Contain("找不到 GameData 目录"));
        Assert.That(vm.HasErrorDetail, Is.True);
        Assert.That(vm.ErrorDetail, Does.Contain("实例路径"));
        Assert.That(vm.Rows, Is.Empty);
    }

    [Test]
    public void 行视图模型把内容与体积写成中文预览()
    {
        var empty = new ResidualDirectoryRowViewModel(Info("Empty", files: 0, bytes: 0));
        var withFiles = new ResidualDirectoryRowViewModel(
            new ResidualDirectoryInfo("Leftover", 3, 1536, new[] { "a.cfg", "b.version" }));

        Assert.That(empty.ContentText, Is.EqualTo("空目录"));
        Assert.That(empty.HasSamples, Is.False);
        Assert.That(withFiles.ContentText, Is.EqualTo("3 个文件 · 1.5 KB"));
        Assert.That(withFiles.SampleText, Is.EqualTo("如：a.cfg、b.version"));
        Assert.That(withFiles.IsSelected, Is.True);
        Assert.That(withFiles.HasBlockReason, Is.False);
    }

    [Test]
    public void 体积格式化覆盖常见量级()
    {
        Assert.That(ResidualDirectoryRowViewModel.FormatSize(0), Is.EqualTo("0 B"));
        Assert.That(ResidualDirectoryRowViewModel.FormatSize(512), Is.EqualTo("512 B"));
        Assert.That(ResidualDirectoryRowViewModel.FormatSize(2048), Is.EqualTo("2 KB"));
        Assert.That(ResidualDirectoryRowViewModel.FormatSize(1536 * 1024), Is.EqualTo("1.5 MB"));
        Assert.That(ResidualDirectoryRowViewModel.FormatSize(3L * 1024 * 1024 * 1024), Is.EqualTo("3 GB"));
    }

    private static ResidualCleanupDialogViewModel Vm(
        FakeCleanupService service, string gameDir = "C:/games/ksp-test")
        => new(service, new InstanceInfo("测试实例", gameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 1));

    private static ResidualDirectoryInfo Info(
        string relative, int files = 1, long bytes = 16, string? blockReason = null)
        => new(relative, files, bytes, files == 0 ? Array.Empty<string>() : new[] { "settings.cfg" })
        {
            BlockReason = blockReason,
        };

    private sealed class FakeCleanupService : IResidualCleanupService
    {
        public List<ResidualDirectoryInfo> Items { get; } = new();

        public OperationResult<IReadOnlyList<ResidualDirectoryInfo>>? ListOverride { get; set; }

        public OperationResult<ResidualCleanupReport>? DeleteResult { get; set; }

        public List<IReadOnlyCollection<string>> DeleteCalls { get; } = new();

        public int ListCalls { get; private set; }

        public Task<OperationResult<IReadOnlyList<ResidualDirectoryInfo>>> ListAsync(
            InstanceInfo instance,
            IReadOnlyCollection<string>? reportedByUninstall = null,
            CancellationToken ct = default)
        {
            ListCalls++;
            return Task.FromResult(
                ListOverride ?? OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Ok(Items.ToArray()));
        }

        public Task<OperationResult<IReadOnlyList<ResidualDirectoryInfo>>> PreviewAsync(
            InstanceInfo instance,
            IReadOnlyCollection<string> relativePaths,
            CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<ResidualDirectoryInfo>>.Ok(Items.ToArray()));

        public Task<OperationResult<ResidualCleanupReport>> DeleteAsync(
            InstanceInfo instance,
            IReadOnlyCollection<string> relativePaths,
            CancellationToken ct = default)
        {
            DeleteCalls.Add(relativePaths.ToArray());
            var report = new ResidualCleanupReport(relativePaths.Count, 0, 0)
            {
                Deleted = relativePaths.ToArray(),
                FreedBytes = 32,
            };
            return Task.FromResult(DeleteResult ?? OperationResult<ResidualCleanupReport>.Ok(report));
        }
    }

    private sealed class FakeLauncher : IResidualCleanupShellLauncher
    {
        public List<string> Opened { get; } = new();

        public OperationResult? Result { get; set; }

        public Task<OperationResult> OpenFolderAsync(string absolutePath)
        {
            Opened.Add(absolutePath);
            return Task.FromResult(Result ?? OperationResult.Ok());
        }
    }
}
