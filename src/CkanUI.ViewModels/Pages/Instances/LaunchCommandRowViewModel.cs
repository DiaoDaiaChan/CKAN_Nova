using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 启动命令编辑列表里的一行（F-06）：整条命令行原文 + 派生出来的可读描述。
/// </summary>
/// <remarks>
/// 上游 <c>GUI/Dialogs/GameCommandLineOptionsDialog</c> 的每一行只有一个 <c>string CmdLine</c>（整条命令行，
/// 不是拆开的单个参数），这里保持一致；拆解只用于展示（可执行文件名 / 参数 / 是否 Steam 链接）。
/// </remarks>
public sealed partial class LaunchCommandRowViewModel : ObservableObject
{
    /// <summary>用一条命令行创建行。</summary>
    /// <param name="command">命令行原文（会去首尾空白）。</param>
    public LaunchCommandRowViewModel(string? command)
    {
        _command = LaunchCommandLine.Normalize(command);
    }

    /// <summary>命令行原文。</summary>
    [ObservableProperty]
    private string _command;

    /// <summary>是否 Steam 启动链接（<c>steam://rungameid/…</c>）。</summary>
    public bool IsSteam => LaunchCommandLine.IsSteamCommand(Command);

    /// <summary>可执行文件名部分（Steam 链接时为整条命令）。</summary>
    public string Binary => LaunchCommandLine.Split(Command).Binary;

    /// <summary>参数部分（无参数时为空串）。</summary>
    public string Arguments => LaunchCommandLine.Split(Command).Arguments;

    /// <summary>参数是否为空。</summary>
    public bool HasArguments => Arguments.Length > 0;

    /// <summary>展示用文本（空命令给占位文案，避免列表里出现空白行）。</summary>
    public string DisplayText => Command.Length == 0 ? "（空命令）" : Command;

    /// <summary>类型徽标文案。</summary>
    public string KindText => IsSteam ? "Steam 启动链接" : "可执行文件";

    /// <summary>悬浮说明（对齐上游 <c>CmdLineHelp</c>：Steam 版不统计游戏时长）。</summary>
    public string HelpText => IsSteam
        ? "通过 Steam 启动，CKAN 不统计游戏时长。"
        : HasArguments
            ? $"可执行文件：{Binary}；参数：{Arguments}"
            : $"可执行文件：{Binary}；不带参数。";

    /// <summary>把本行还原成命令行字符串。</summary>
    public string ToCommand() => LaunchCommandLine.Normalize(Command);

    partial void OnCommandChanged(string value)
    {
        OnPropertyChanged(nameof(IsSteam));
        OnPropertyChanged(nameof(Binary));
        OnPropertyChanged(nameof(Arguments));
        OnPropertyChanged(nameof(HasArguments));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(HelpText));
    }
}
