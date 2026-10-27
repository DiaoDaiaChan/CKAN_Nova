using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 启动命令编辑面板（F-06）：列表、增、改、删、排序、重置为默认、接受、取消。
/// </summary>
/// <remarks>
/// <para>对齐上游 <c>GUI/Dialogs/GameCommandLineOptionsDialog</c>：网格每行一条命令行，
/// 「重置为默认」在列表已等于默认值时不生效，**不能删到一条不剩**（上游对最后一行直接 <c>e.Cancel</c>）。</para>
/// <para>本类只收集与校验，不写盘、不弹窗：落盘由 <see cref="InstancesPageViewModel"/> 经
/// <see cref="SubmitRequested"/> 处理，文本输入经 <see cref="TextPrompt"/> 注入
/// （<c>IDialogService</c> 没有文本输入框，见 <see cref="IInstanceTextPrompt"/>）。</para>
/// </remarks>
public sealed partial class LaunchCommandsPanelViewModel : ObservableObject
{
    /// <summary>创建面板。</summary>
    /// <param name="instanceName">实例名（标题里显示）。</param>
    /// <param name="commands">当前生效的命令列表。</param>
    /// <param name="defaults">默认命令列表（「重置为默认」的目标）。</param>
    public LaunchCommandsPanelViewModel(
        string instanceName,
        IReadOnlyList<string> commands,
        IReadOnlyList<string> defaults)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(defaults);

        InstanceName = instanceName ?? string.Empty;
        Defaults = LaunchCommandLine.NormalizeList(defaults);
        Commands = new ObservableCollection<LaunchCommandRowViewModel>(
            LaunchCommandLine.NormalizeList(commands).Select(command => new LaunchCommandRowViewModel(command)));

        _selectedRow = Commands.Count > 0 ? Commands[0] : null;
        foreach (var row in Commands)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    /// <summary>实例名。</summary>
    public string InstanceName { get; }

    /// <summary>面板标题。</summary>
    public string Title => $"启动命令：{InstanceName}";

    /// <summary>说明文案。</summary>
    public string HintText =>
        "每行是一条完整的启动命令（可执行文件在前，参数在后）。启动游戏时如果有多条命令，会先让你选一条。";

    /// <summary>默认命令列表。</summary>
    public IReadOnlyList<string> Defaults { get; }

    /// <summary>当前编辑中的命令行。</summary>
    public ObservableCollection<LaunchCommandRowViewModel> Commands { get; }

    /// <summary>文本输入通道（由宿主注入；为空时「增 / 改」给出提示而不是崩溃）。</summary>
    public IInstanceTextPrompt? TextPrompt { get; set; }

    /// <summary>接受编辑时触发（参数为最终命令列表）；由页面负责落盘。</summary>
    public Func<IReadOnlyList<string>, Task>? SubmitRequested { get; set; }

    /// <summary>取消编辑时触发。</summary>
    public Action? CancelRequested { get; set; }

    /// <summary>当前选中的行。</summary>
    [ObservableProperty]
    private LaunchCommandRowViewModel? _selectedRow;

    /// <summary>校验 / 操作错误文案。</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>是否正在保存。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>是否有错误文案。</summary>
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    /// <summary>列表条数文案。</summary>
    public string CountText => Commands.Count == 0 ? "还没有启动命令" : $"共 {Commands.Count} 条命令";

    /// <summary>列表是否已与默认值一致（决定「重置为默认」是否可用）。</summary>
    public bool HasChanges
    {
        get
        {
            var current = ToCommands();
            if (current.Count != Defaults.Count)
            {
                return true;
            }

            for (var i = 0; i < current.Count; i++)
            {
                if (!string.Equals(current[i], Defaults[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>是否可以编辑选中的行。</summary>
    public bool CanEdit => SelectedRow is not null && !IsBusy;

    /// <summary>是否可以删除选中的行（至少保留一条，对齐上游行为）。</summary>
    public bool CanRemove => SelectedRow is not null && Commands.Count > 1 && !IsBusy;

    /// <summary>是否可以上移。</summary>
    public bool CanMoveUp => SelectedRow is not null && !IsBusy && IndexOfSelected() > 0;

    /// <summary>是否可以下移。</summary>
    public bool CanMoveDown
    {
        get
        {
            var index = IndexOfSelected();
            return index >= 0 && index < Commands.Count - 1 && !IsBusy;
        }
    }

    /// <summary>是否可以重置为默认。</summary>
    public bool CanResetToDefaults => !IsBusy && Defaults.Count > 0 && HasChanges;

    /// <summary>是否可以接受（保存）。</summary>
    public bool CanAccept => !IsBusy && Commands.Count > 0;

    /// <summary>添加一条命令（弹文本输入框）。</summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        var command = await TryPromptAsync("添加启动命令", "输入一条完整的启动命令（可执行文件在前，参数在后）：", null)
            .ConfigureAwait(true);
        if (command is null)
        {
            return;
        }

        if (IndexOf(command) >= 0)
        {
            ValidationError = "该命令已在列表中。";
            return;
        }

        var row = new LaunchCommandRowViewModel(command);
        row.PropertyChanged += OnRowPropertyChanged;
        Commands.Add(row);
        SelectedRow = row;
        ValidationError = null;
        OnCommandsChanged();
    }

    /// <summary>修改选中的命令（弹文本输入框，预填当前值）。</summary>
    [RelayCommand]
    private async Task EditAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            ValidationError = "请先选择要修改的命令。";
            return;
        }

        var command = await TryPromptAsync("修改启动命令", "修改这条启动命令：", row.Command).ConfigureAwait(true);
        if (command is null)
        {
            return;
        }

        var existing = IndexOf(command);
        if (existing >= 0 && !ReferenceEquals(Commands[existing], row))
        {
            ValidationError = "该命令已在列表中。";
            return;
        }

        row.Command = command;
        ValidationError = null;
        OnCommandsChanged();
    }

    /// <summary>删除选中的命令。</summary>
    [RelayCommand]
    private void Remove()
    {
        var row = SelectedRow;
        if (row is null)
        {
            ValidationError = "请先选择要删除的命令。";
            return;
        }

        if (Commands.Count <= 1)
        {
            ValidationError = "至少要保留一条启动命令。";
            return;
        }

        var index = Commands.IndexOf(row);
        row.PropertyChanged -= OnRowPropertyChanged;
        Commands.Remove(row);
        SelectedRow = Commands.Count == 0 ? null : Commands[Math.Clamp(index, 0, Commands.Count - 1)];
        ValidationError = null;
        OnCommandsChanged();
    }

    /// <summary>上移选中的命令。</summary>
    [RelayCommand]
    private void MoveUp() => Move(-1);

    /// <summary>下移选中的命令。</summary>
    [RelayCommand]
    private void MoveDown() => Move(1);

    /// <summary>重置为默认命令列表。</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        if (Defaults.Count == 0)
        {
            ValidationError = "没有可用的默认命令。";
            return;
        }

        foreach (var row in Commands)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Commands.Clear();
        foreach (var command in Defaults)
        {
            var row = new LaunchCommandRowViewModel(command);
            row.PropertyChanged += OnRowPropertyChanged;
            Commands.Add(row);
        }

        SelectedRow = Commands.Count > 0 ? Commands[0] : null;
        ValidationError = null;
        OnCommandsChanged();
    }

    /// <summary>接受编辑（交给页面落盘）。</summary>
    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (Commands.Count == 0)
        {
            ValidationError = "至少要保留一条启动命令。";
            return;
        }

        var commands = ToCommands();
        if (commands.Count == 0)
        {
            ValidationError = "至少要保留一条启动命令。";
            return;
        }

        ValidationError = null;
        if (SubmitRequested is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await SubmitRequested(commands).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>取消编辑（不落盘）。</summary>
    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();

    /// <summary>当前编辑结果（按列表顺序，去空白去空行）。</summary>
    public IReadOnlyList<string> ToCommands() =>
        LaunchCommandLine.NormalizeList(Commands.Select(row => row.ToCommand()));

    private async Task<string?> TryPromptAsync(string title, string message, string? initialValue)
    {
        var prompt = TextPrompt;
        if (prompt is null)
        {
            ValidationError = "文本输入框尚未接入，暂时无法编辑启动命令。";
            return null;
        }

        var value = await prompt.PromptAsync(title, message, initialValue, "例如 KSP_x64.exe -single-instance")
            .ConfigureAwait(true);
        if (value is null)
        {
            return null;
        }

        var normalized = LaunchCommandLine.Normalize(value);
        if (normalized.Length == 0)
        {
            ValidationError = "命令不能为空。";
            return null;
        }

        return normalized;
    }

    private void Move(int delta)
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        var index = Commands.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Commands.Count)
        {
            return;
        }

        Commands.Move(index, target);
        SelectedRow = row;
        ValidationError = null;
        OnCommandsChanged();
    }

    private int IndexOf(string command)
    {
        for (var i = 0; i < Commands.Count; i++)
        {
            if (string.Equals(Commands[i].ToCommand(), command, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int IndexOfSelected() => SelectedRow is null ? -1 : Commands.IndexOf(SelectedRow);

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LaunchCommandRowViewModel.Command))
        {
            OnCommandsChanged();
        }
    }

    private void OnCommandsChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanResetToDefaults));
        OnPropertyChanged(nameof(CanAccept));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    partial void OnSelectedRowChanged(LaunchCommandRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    partial void OnValidationErrorChanged(string? value) => OnPropertyChanged(nameof(HasValidationError));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanResetToDefaults));
        OnPropertyChanged(nameof(CanAccept));
    }
}
