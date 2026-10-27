using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.TestSupport;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 启动命令的 headless 测试：F-05（多命令选择）与 F-06（启动命令行编辑）。
/// </summary>
/// <remarks>
/// <para>对齐上游：命令列表按实例存放在 <c>&lt;GameDir&gt;/CKAN/GUIConfig.json</c> 的 <c>CommandLines</c> 键里
/// （<c>GUI/Model/GUIConfiguration.cs</c>），编辑对话框「不能删到一条不剩」、可「重置为默认」
/// （<c>GUI/Dialogs/GameCommandLineOptionsDialog.cs</c>），多条命令时先让用户选一条再启动
/// （<c>GUI/Controls/ManageMods.cs</c> 的启动下拉）。</para>
/// <para>所有依赖都用替身注入：不碰真实磁盘（除文件契约用例走 <see cref="TempWorkspace"/>）、
/// 不弹真实对话框、不创建真实进程。</para>
/// </remarks>
[TestFixture]
public sealed class InstancesLaunchCommandTests
{
    private const string MainDir = "D:/Games/KSP-Main";

    private const string DefaultCommand = "KSP_x64.exe -single-instance";

    private TempWorkspace? workspace;

    [SetUp]
    public void SetUp() => workspace = new TempWorkspace("instances-launch");

    [TearDown]
    public void TearDown()
    {
        workspace?.Dispose();
        workspace = null;
    }

    // ---------------------------------------------------------------- 命令行拆分与转义

    [Test]
    public void 命令行拆分_可执行文件与参数分离()
    {
        var (binary, arguments) = LaunchCommandLine.Split("KSP_x64.exe -single-instance -mm");

        Assert.That(binary, Is.EqualTo("KSP_x64.exe"));
        Assert.That(arguments, Is.EqualTo("-single-instance -mm"));
    }

    [Test]
    public void 命令行拆分_空命令得到两个空串()
    {
        var (binary, arguments) = LaunchCommandLine.Split("   ");

        Assert.That(binary, Is.Empty);
        Assert.That(arguments, Is.Empty);
    }

    [Test]
    public void 命令行拆分_带空格路径的参数保持完整()
    {
        var command = LaunchCommandLine.Join(new[] { "D:/Games/KSP Main/KSP_x64.exe", "-single-instance" });

        Assert.That(command, Is.EqualTo("\"D:/Games/KSP Main/KSP_x64.exe\" -single-instance"));

        var tokens = LaunchCommandLine.Tokenize(command);
        Assert.That(tokens, Is.EqualTo(new[] { "D:/Games/KSP Main/KSP_x64.exe", "-single-instance" }));

        var (binary, arguments) = LaunchCommandLine.Split(command);
        Assert.That(binary, Is.EqualTo("D:/Games/KSP Main/KSP_x64.exe"));
        Assert.That(arguments, Is.EqualTo("-single-instance"));
    }

    [Test]
    public void 命令行转义_含引号与反斜杠的参数往返不丢字符()
    {
        var tokens = new[] { "KSP.exe", "-mm", "路径带空格", "带\"引号", "尾部反斜杠\\" };
        var command = LaunchCommandLine.Join(tokens);

        Assert.That(LaunchCommandLine.Tokenize(command), Is.EqualTo(tokens));
    }

    [Test]
    public void 命令行引用_无空白不加引号_空串加空引号()
    {
        Assert.That(LaunchCommandLine.Quote("KSP.exe"), Is.EqualTo("KSP.exe"));
        Assert.That(LaunchCommandLine.Quote("a b"), Is.EqualTo("\"a b\""));
        Assert.That(LaunchCommandLine.Quote(string.Empty), Is.EqualTo("\"\""));
    }

    [Test]
    public void 命令列表归一化_去空白去空行去重复()
    {
        var normalized = LaunchCommandLine.NormalizeList(new[] { " KSP_x64.exe ", string.Empty, "   ", "ksp_x64.EXE", "KSP.exe" });

        Assert.That(normalized, Is.EqualTo(new[] { "KSP_x64.exe", "KSP.exe" }));
    }

    [Test]
    public void Steam命令_按前缀识别()
    {
        Assert.That(LaunchCommandLine.IsSteamCommand("steam://rungameid/220200"), Is.True);
        Assert.That(LaunchCommandLine.IsSteamCommand("KSP_x64.exe -single-instance"), Is.False);
        Assert.That(LaunchCommandLine.IsSteamCommand(null), Is.False);
    }

    // ---------------------------------------------------------------- 默认命令推导

    [Test]
    public void 默认命令_Windows下每个存在的可执行文件各一条()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KSP_x64.exe", "KSP.exe" };

        var commands = LaunchCommandDefaults.For(
            "D:/Games/KSP",
            isWindows: true,
            isMac: false,
            path => existing.Contains(Path.GetFileName(path)));

        Assert.That(commands, Is.EqualTo(new[] { "KSP_x64.exe -single-instance", "KSP.exe -single-instance" }));
    }

    [Test]
    public void 默认命令_锚点都不存在时退回第一个候选()
    {
        var commands = LaunchCommandDefaults.For("D:/Games/KSP", isWindows: true, isMac: false, _ => false);

        Assert.That(commands, Is.EqualTo(new[] { DefaultCommand }));
    }

    [Test]
    public void 默认命令_Steam库目录追加rungameid链接()
    {
        var command = LaunchCommandDefaults.SteamCommandFor(
            "D:/Steam/steamapps/common/Kerbal Space Program",
            path => string.Equals(Path.GetFileName(path), "appmanifest_220200.acf", StringComparison.OrdinalIgnoreCase));

        Assert.That(command, Is.EqualTo("steam://rungameid/220200"));

        var commands = LaunchCommandDefaults.For(
            "D:/Steam/steamapps/common/Kerbal Space Program",
            isWindows: true,
            isMac: false,
            path => string.Equals(Path.GetFileName(path), "appmanifest_1740390.acf", StringComparison.OrdinalIgnoreCase));

        Assert.That(commands, Does.Contain("steam://rungameid/1740390"));
    }

    // ---------------------------------------------------------------- 文件持久化（<GameDir>/CKAN/GUIConfig.json）

    [Test]
    public async Task 命令文件_写入后读回一致()
    {
        var store = new LaunchCommandFile();
        var commands = new[] { DefaultCommand, "steam://rungameid/220200" };

        var write = await store.WriteAsync(workspace!.Path, commands);
        Assert.That(write.IsSuccess, Is.True);
        Assert.That(File.Exists(LaunchCommandFile.PathFor(workspace.Path)), Is.True);

        var read = await store.ReadAsync(workspace.Path);
        Assert.That(read.IsSuccess, Is.True);
        Assert.That(read.Value, Is.EqualTo(commands));
    }

    [Test]
    public async Task 命令文件_写入时保留其它字段()
    {
        var path = LaunchCommandFile.PathFor(workspace!.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\n  \"HideEpochs\": true,\n  \"CommandLines\": [\"old.exe\"]\n}");

        var write = await new LaunchCommandFile().WriteAsync(workspace.Path, new[] { "KSP_x64.exe" });
        Assert.That(write.IsSuccess, Is.True);

        var text = await File.ReadAllTextAsync(path);
        Assert.That(text, Does.Contain("HideEpochs"));
        Assert.That(text, Does.Contain("KSP_x64.exe"));
        Assert.That(text, Does.Not.Contain("old.exe"));
    }

    [Test]
    public async Task 命令文件_缺文件表示还没保存过()
    {
        var read = await new LaunchCommandFile().ReadAsync(workspace!.Path);

        Assert.That(read.IsSuccess, Is.True);
        Assert.That(read.Value, Is.Null);
    }

    [Test]
    public async Task 命令文件_非法JSON读取与写入都给出中文失败文案()
    {
        var path = LaunchCommandFile.PathFor(workspace!.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ 这不是 JSON");

        var read = await new LaunchCommandFile().ReadAsync(workspace.Path);
        Assert.That(read.IsSuccess, Is.False);
        Assert.That(read.Message, Is.EqualTo("启动命令文件不是合法 JSON"));

        var write = await new LaunchCommandFile().WriteAsync(workspace.Path, new[] { "KSP.exe" });
        Assert.That(write.IsSuccess, Is.False);
        Assert.That(write.Message, Is.EqualTo("启动命令文件不是合法 JSON，已拒绝覆盖"));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("{ 这不是 JSON"));
    }

    [Test]
    public async Task 命令文件_路径落在实例的CKAN目录下()
    {
        Assert.That(
            LaunchCommandFile.PathFor("D:/KSP"),
            Is.EqualTo(Path.Combine("D:/KSP", "CKAN", "GUIConfig.json")));

        var store = new LaunchCommandFile();
        await store.WriteAsync(workspace!.Path, new[] { "KSP.exe" });

        Assert.That(File.Exists(Path.Combine(workspace.Path, "CKAN", "GUIConfig.json")), Is.True);
    }

    // ---------------------------------------------------------------- F-05 多命令选择

    [Test]
    public async Task 启动游戏_只有一条命令时不弹选择框直接启动()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand });

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Dialogs.SelectCalls, Is.EqualTo(0));
        Assert.That(h.Shell.LaunchCalls, Is.EqualTo(1));
        Assert.That(h.Shell.LastLaunchDir, Is.EqualTo(MainDir));
        Assert.That(h.Shell.LastLaunchCommand, Is.EqualTo(DefaultCommand));
        Assert.That(h.Vm.StatusText, Does.Contain("已启动"));
    }

    [Test]
    public async Task 启动游戏_多条命令时弹选择框并列出全部命令()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand, "KSP.exe -single-instance", "steam://rungameid/220200" });

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Dialogs.SelectCalls, Is.EqualTo(1));
        Assert.That(h.Dialogs.SelectTitles[0], Does.Contain("启动"));
        Assert.That(h.Dialogs.SelectOptions[0], Is.EqualTo(new[] { DefaultCommand, "KSP.exe -single-instance", "steam://rungameid/220200" }));
        Assert.That(h.Shell.LastLaunchCommand, Is.EqualTo(DefaultCommand));
    }

    [Test]
    public async Task 启动游戏_多条命令取消选择时不启动()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand, "KSP.exe -single-instance" });
        h.Dialogs.SelectResult = -1;

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Dialogs.SelectCalls, Is.EqualTo(1));
        Assert.That(h.Shell.LaunchCalls, Is.EqualTo(0));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已取消启动。"));
        Assert.That(h.Vm.HasError, Is.False);
    }

    [Test]
    public async Task 启动游戏_选中第二条命令时用第二条启动()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand, "KSP.exe -single-instance" });
        h.Dialogs.SelectResult = 1;

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Shell.LastLaunchCommand, Is.EqualTo("KSP.exe -single-instance"));
        Assert.That(h.Vm.StatusText, Does.Contain("KSP.exe -single-instance"));
    }

    [Test]
    public async Task 启动游戏_没有可用命令时提示先编辑启动命令()
    {
        var h = new LaunchHarness();
        h.Store.ReadResult = OperationResult<IReadOnlyList<string>?>.Ok(null);
        h.Store.FallbackDefaults = Array.Empty<string>();

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Dialogs.SelectCalls, Is.EqualTo(0));
        Assert.That(h.Shell.LaunchCalls, Is.EqualTo(0));
        Assert.That(h.Vm.ErrorText, Does.Contain("没有可用的启动命令"));
    }

    [Test]
    public async Task 启动游戏_读取命令失败时给出读取失败文案()
    {
        var h = new LaunchHarness();
        h.Store.ReadResult = OperationResult<IReadOnlyList<string>?>.Fail("启动命令文件不是合法 JSON", "第 1 行", retryable: false);

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Shell.LaunchCalls, Is.EqualTo(0));
        Assert.That(h.Vm.ErrorText, Does.Contain("启动命令文件不是合法 JSON"));
        Assert.That(h.Vm.ErrorText, Does.Contain("不可重试"));
        Assert.That(h.Vm.ErrorDetail, Is.EqualTo("第 1 行"));
    }

    [Test]
    public async Task 启动游戏_启动器失败时给出启动失败文案()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand });
        h.Shell.LaunchResult = OperationResult.Fail("启动游戏失败", "拒绝访问", retryable: false);

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Vm.HasError, Is.True);
        Assert.That(h.Vm.ErrorText, Is.EqualTo("启动游戏失败（不可重试）"));
        Assert.That(h.Vm.ErrorDetail, Is.EqualTo("拒绝访问"));
    }

    [Test]
    public async Task 启动游戏_启动器未接入时给出占位文案()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand }, withLauncher: false);

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Vm.StatusText, Does.Contain("尚未接入"));
        Assert.That(h.Vm.HasError, Is.False);
    }

    [Test]
    public async Task 启动游戏_锁定实例时不弹选择框也不启动()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand, "KSP.exe" }, locked: true);

        await h.Vm.LaunchSelectedAsync(h.Row);

        Assert.That(h.Dialogs.SelectCalls, Is.EqualTo(0));
        Assert.That(h.Shell.LaunchCalls, Is.EqualTo(0));
        Assert.That(h.Vm.ErrorText, Does.Contain("不能重复启动"));
    }

    [Test]
    public async Task 解析命令_未保存时回落到该实例的默认命令()
    {
        var h = new LaunchHarness(defaults: new[] { "KSP.exe -single-instance" });

        var resolved = await h.Vm.ResolveCommandsAsync(h.Row);

        Assert.That(resolved.IsSuccess, Is.True);
        Assert.That(resolved.Value, Is.EqualTo(new[] { "KSP.exe -single-instance" }));
        Assert.That(h.Store.ReadCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task 解析命令_已保存时优先用保存值()
    {
        var h = new LaunchHarness(saved: new[] { "KSP.exe -mm" }, defaults: new[] { DefaultCommand });

        var resolved = await h.Vm.ResolveCommandsAsync(h.Row);

        Assert.That(resolved.Value, Is.EqualTo(new[] { "KSP.exe -mm" }));
    }

    // ---------------------------------------------------------------- F-06 编辑面板接线

    [Test]
    public async Task 编辑命令_打开面板并注入文本输入通道()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand });

        await h.Vm.EditSelectedCommandsAsync(h.Row);

        Assert.That(h.Vm.HasCommandsPanel, Is.True);
        Assert.That(h.Vm.CommandsPanel!.TextPrompt, Is.SameAs(h.Prompt));
        Assert.That(h.Vm.CommandsPanel.ToCommands(), Is.EqualTo(new[] { DefaultCommand }));
        Assert.That(h.Vm.HasForm, Is.False);
    }

    [Test]
    public async Task 编辑命令_未选实例时提示先选择()
    {
        var h = new LaunchHarness();
        h.Vm.SelectedRow = null;

        await h.Vm.EditSelectedCommandsAsync(null);

        Assert.That(h.Vm.HasCommandsPanel, Is.False);
        Assert.That(h.Vm.ErrorText, Is.EqualTo("请先选择要编辑启动命令的实例。"));
    }

    [Test]
    public async Task 编辑命令_接受后写盘并关闭面板()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand });
        await h.Vm.EditSelectedCommandsAsync(h.Row);
        var panel = h.Vm.CommandsPanel!;

        h.Prompt.Enqueue("KSP.exe -single-instance");
        await panel.AddCommand.ExecuteAsync(null);
        await panel.AcceptCommand.ExecuteAsync(null);

        Assert.That(h.Vm.HasCommandsPanel, Is.False);
        Assert.That(h.Store.WriteCalls, Is.EqualTo(1));
        Assert.That(h.Store.LastWritten, Is.EqualTo(new[] { DefaultCommand, "KSP.exe -single-instance" }));
        Assert.That(h.Vm.StatusText, Does.Contain("已保存"));
    }

    [Test]
    public async Task 编辑命令_取消后不写盘()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand });
        await h.Vm.EditSelectedCommandsAsync(h.Row);

        h.Vm.CommandsPanel!.CancelCommand.Execute(null);

        Assert.That(h.Vm.HasCommandsPanel, Is.False);
        Assert.That(h.Store.WriteCalls, Is.EqualTo(0));
        Assert.That(h.Vm.StatusText, Is.EqualTo("已取消编辑启动命令。"));
    }

    [Test]
    public async Task 编辑命令_保存失败时面板保留并显示失败文案()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand });
        h.Store.WriteResult = OperationResult.Fail("保存启动命令失败", "拒绝访问", retryable: true);
        await h.Vm.EditSelectedCommandsAsync(h.Row);

        await h.Vm.CommandsPanel!.AcceptCommand.ExecuteAsync(null);

        Assert.That(h.Vm.HasCommandsPanel, Is.True);
        Assert.That(h.Vm.CommandsPanel!.ValidationError, Is.EqualTo("保存启动命令失败（可重试）"));
    }

    [Test]
    public async Task 编辑命令_存储未接入时给出接入提示()
    {
        var h = new LaunchHarness(saved: new[] { DefaultCommand }, withStore: false);
        var panel = new LaunchCommandsPanelViewModel("KSP", new[] { DefaultCommand }, new[] { DefaultCommand });

        await h.Vm.SaveCommandsAsync(h.Row, panel, new[] { DefaultCommand });

        Assert.That(panel.ValidationError, Is.EqualTo("启动命令存储尚未接入，无法保存。"));
        Assert.That(h.Vm.HasCommandsPanel, Is.False);
    }

    // ---------------------------------------------------------------- F-06 面板编辑动作

    [Test]
    public async Task 命令面板_添加一条命令()
    {
        var panel = NewPanel(new[] { DefaultCommand });
        panel.TextPrompt = new InstancesFakeTextPrompt();
        ((InstancesFakeTextPrompt)panel.TextPrompt).Enqueue("KSP.exe -single-instance");

        await panel.AddCommand.ExecuteAsync(null);

        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand, "KSP.exe -single-instance" }));
        Assert.That(panel.HasValidationError, Is.False);
    }

    [Test]
    public async Task 命令面板_添加重复命令被拒绝()
    {
        var panel = NewPanel(new[] { DefaultCommand });
        var prompt = new InstancesFakeTextPrompt();
        prompt.Enqueue("ksp_x64.exe -single-instance");
        panel.TextPrompt = prompt;

        await panel.AddCommand.ExecuteAsync(null);

        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand }));
        Assert.That(panel.ValidationError, Is.EqualTo("该命令已在列表中。"));
    }

    [Test]
    public async Task 命令面板_修改选中的命令()
    {
        var panel = NewPanel(new[] { DefaultCommand, "KSP.exe" });
        var prompt = new InstancesFakeTextPrompt();
        prompt.Enqueue("KSP.exe -popupwindow");
        panel.TextPrompt = prompt;
        panel.SelectedRow = panel.Commands[1];

        await panel.EditCommand.ExecuteAsync(null);

        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand, "KSP.exe -popupwindow" }));
        Assert.That(prompt.InitialValues[0], Is.EqualTo("KSP.exe"));
    }

    [Test]
    public async Task 命令面板_修改时取消输入不改变列表()
    {
        var panel = NewPanel(new[] { DefaultCommand });
        var prompt = new InstancesFakeTextPrompt();
        prompt.Enqueue((string?)null);
        panel.TextPrompt = prompt;
        panel.SelectedRow = panel.Commands[0];

        await panel.EditCommand.ExecuteAsync(null);

        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand }));
    }

    [Test]
    public void 命令面板_删除命令但至少保留一条()
    {
        var panel = NewPanel(new[] { DefaultCommand, "KSP.exe" });

        panel.SelectedRow = panel.Commands[1];
        panel.RemoveCommand.Execute(null);
        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand }));

        panel.SelectedRow = panel.Commands[0];
        panel.RemoveCommand.Execute(null);
        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand }));
        Assert.That(panel.ValidationError, Is.EqualTo("至少要保留一条启动命令。"));
    }

    [Test]
    public void 命令面板_上移下移改变顺序()
    {
        var panel = NewPanel(new[] { "a.exe", "b.exe", "c.exe" });

        panel.SelectedRow = panel.Commands[2];
        panel.MoveUpCommand.Execute(null);
        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { "a.exe", "c.exe", "b.exe" }));

        panel.MoveDownCommand.Execute(null);
        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { "a.exe", "b.exe", "c.exe" }));

        panel.SelectedRow = panel.Commands[0];
        panel.MoveUpCommand.Execute(null);
        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { "a.exe", "b.exe", "c.exe" }));
    }

    [Test]
    public void 命令面板_重置为默认()
    {
        var panel = NewPanel(new[] { "KSP.exe" }, defaults: new[] { DefaultCommand });

        Assert.That(panel.HasChanges, Is.True);
        Assert.That(panel.CanResetToDefaults, Is.True);

        panel.ResetToDefaultsCommand.Execute(null);

        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand }));
        Assert.That(panel.HasChanges, Is.False);
        Assert.That(panel.CanResetToDefaults, Is.False);
    }

    [Test]
    public async Task 命令面板_接受时把最终列表交给页面()
    {
        var panel = NewPanel(new[] { DefaultCommand });
        IReadOnlyList<string>? submitted = null;
        var cancelled = 0;
        panel.SubmitRequested = commands =>
        {
            submitted = commands;
            return Task.CompletedTask;
        };
        panel.CancelRequested = () => cancelled++;

        await panel.AcceptCommand.ExecuteAsync(null);
        panel.CancelCommand.Execute(null);

        Assert.That(submitted, Is.EqualTo(new[] { DefaultCommand }));
        Assert.That(cancelled, Is.EqualTo(1));
    }

    [Test]
    public async Task 命令面板_没有文本输入通道时给出提示而不是崩溃()
    {
        var panel = NewPanel(new[] { DefaultCommand });
        panel.TextPrompt = null;

        await panel.AddCommand.ExecuteAsync(null);

        Assert.That(panel.ValidationError, Is.EqualTo("文本输入框尚未接入，暂时无法编辑启动命令。"));
        Assert.That(panel.ToCommands(), Is.EqualTo(new[] { DefaultCommand }));
    }

    // ---------------------------------------------------------------- 文本输入对话框

    [Test]
    public void 文本输入对话框_确定返回去空白文本()
    {
        var vm = new TextInputDialogViewModel("添加启动命令", "输入一条命令：", DefaultCommand);

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.Result, Is.EqualTo(DefaultCommand));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void 文本输入对话框_空白内容校验失败()
    {
        var vm = new TextInputDialogViewModel("添加启动命令", "输入一条命令：", "   ");

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.ValidationError, Is.EqualTo("内容不能为空"));
        Assert.That(vm.HasValidationError, Is.True);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.IsClosed, Is.False);
        Assert.That(vm.Result, Is.Null);
    }

    [Test]
    public void 文本输入对话框_取消返回空结果()
    {
        var vm = new TextInputDialogViewModel("添加启动命令", "输入一条命令：", DefaultCommand);

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Result, Is.Null);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.IsClosed, Is.True);
    }

    // ---------------------------------------------------------------- 装配

    private static LaunchCommandsPanelViewModel NewPanel(
        IReadOnlyList<string> commands,
        IReadOnlyList<string>? defaults = null) =>
        new("KSP 主实例", commands, defaults ?? commands);

    private sealed class LaunchHarness
    {
        public LaunchHarness(
            string[]? saved = null,
            string[]? defaults = null,
            bool withStore = true,
            bool withLauncher = true,
            bool locked = false)
        {
            Service.Seed(new InstanceInfo("KSP 主实例", MainDir, "1.12.5", true, locked, 7));
            Service.Current = Service.Instances[0];

            Store.FallbackDefaults = defaults ?? new[] { DefaultCommand };
            if (saved is not null)
            {
                Store.Seed(MainDir, saved);
            }

            Vm = new InstancesPageViewModel(Service, Dialogs, Errors)
            {
                PathProbe = Probe,
                FolderPicker = Picker,
                SteamLibraries = Steam,
                ShellLauncher = withLauncher ? Shell : null,
                CommandStore = withStore ? Store : null,
                TextPrompt = Prompt,
            };

            // 宿主能力在构造之后注入，注入完重新投影一次选中项才算数。
            Vm.Reload();
        }

        public InstancesFakeService Service { get; } = new();

        public InstancesFakeDialogs Dialogs { get; } = new();

        public InstancesFakeErrorReporter Errors { get; } = new();

        public InstancesFakePathProbe Probe { get; } = new();

        public InstancesFakeShellLauncher Shell { get; } = new();

        public InstancesFakeSteamSource Steam { get; } = new();

        public InstancesFakeFolderPicker Picker { get; } = new();

        public InstancesFakeCommandStore Store { get; } = new();

        public InstancesFakeTextPrompt Prompt { get; } = new();

        public InstancesPageViewModel Vm { get; }

        public InstanceRowViewModel Row => Vm.Rows[0];
    }
}
