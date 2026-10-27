using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using CkanUI.Services.Abstractions;

using IDispatcher = CkanUI.Services.Abstractions.IDispatcher;

namespace CkanUI.Platform.Dialogs;

/// <summary>
/// <see cref="IDialogService"/> 的 Avalonia 实现（04 篇 §3 / 05 篇 §2）。
/// 窗口全部用代码构建，不引入 axaml，避免与 App 层争抢视图文件。
/// </summary>
/// <remarks>
/// 调用方可能是后台线程（Core 的 IUser.RaiseYesNoDialog 由安装线程发起），
/// 所以每个方法都先用 <see cref="IDispatcher.InvokeAsync(Action)"/> 把「建窗 + ShowDialog」搬到 UI 线程，
/// 再 await 结果；绝不在 UI 线程上同步等待后台，也不假定调用方在 UI 线程。
/// </remarks>
public sealed class DialogService : IDialogService
{
    private const double WindowWidth = 460;
    private const double Spacing = 12;

    private readonly IDispatcher _dispatcher;
    private readonly Func<Window?>? _ownerProvider;
    private readonly IDialogPresenter? _presenter;

    /// <param name="dispatcher">UI 线程封送入口。</param>
    /// <param name="ownerProvider">
    /// 返回 owner 窗口的函数（通常取桌面主窗口）。返回 null 时降级为非模态 Show()——
    /// 设计时预览与启动早期（主窗口还没建）没有 owner，此时仍要能把错误显示出来。
    /// </param>
    /// <param name="presenter">
    /// 可选的真实视图呈现者（App 层实现）。为 null 或某类对话框返回 null 时，退回本类内置的代码建窗版本。
    /// </param>
    public DialogService(IDispatcher dispatcher, Func<Window?>? ownerProvider = null, IDialogPresenter? presenter = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _ownerProvider = ownerProvider;
        _presenter = presenter;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, string? detail = null)
    {
        if (_presenter is { } presenter)
        {
            var delegated = await _dispatcher
                .InvokeAsync(() => presenter.ConfirmAsync(title, message, detail))
                .ConfigureAwait(false);
            if (await delegated.ConfigureAwait(false) is { } confirmed)
            {
                return confirmed;
            }
        }

        var pending = await _dispatcher.InvokeAsync(() => ShowChoiceCore(title, message, detail))
            .ConfigureAwait(false);
        return await pending.ConfigureAwait(false) == 0;
    }

    /// <inheritdoc />
    public async Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_presenter is { } presenter)
        {
            var delegated = await _dispatcher
                .InvokeAsync(() => presenter.SelectAsync(title, message, options, defaultIndex))
                .ConfigureAwait(false);
            if (await delegated.ConfigureAwait(false) is { } selected)
            {
                return selected;
            }
        }

        var pending = await _dispatcher.InvokeAsync(() => ShowSelectionCore(title, message, options, defaultIndex))
            .ConfigureAwait(false);
        return await pending.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ShowMessageAsync(string title, string message)
    {
        if (await TryShowNoticeAsync(isError: false, title, message, detail: null).ConfigureAwait(false))
        {
            return;
        }

        var pending = await _dispatcher.InvokeAsync(() => ShowNoticeCore(title, message, detail: null))
            .ConfigureAwait(false);
        await pending.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(string title, string message, string? detail = null)
    {
        if (await TryShowNoticeAsync(isError: true, title, message, detail).ConfigureAwait(false))
        {
            return;
        }

        var pending = await _dispatcher.InvokeAsync(() => ShowNoticeCore(title, message, detail))
            .ConfigureAwait(false);
        await pending.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RunWithProgressAsync(
        string title,
        Func<IProgress<double>, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var ui = await _dispatcher.InvokeAsync(() => ShowProgressCore(title)).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        ui.CancelRequested += (_, _) => CancelQuietly(cancellation);

        try
        {
            var progress = new DispatcherProgress(_dispatcher, ui.Bar);
            await Task.Run(() => work(progress, cancellation.Token), cancellation.Token).ConfigureAwait(false);
            ui.Close();
            return OperationResult<bool>.Ok(true);
        }
        catch (OperationCanceledException)
        {
            ui.Close();
            return OperationResult<bool>.Fail("操作已取消", retryable: true);
        }
        catch (Exception exception)
        {
            ui.Close();
            // 对话框自己也要保证异常不穿透接口（契约 §3），现场记日志由调用方的 IErrorReporter 负责
            return OperationResult<bool>.Fail("操作失败", exception.Message, retryable: true);
        }
    }

    /// <summary>把通知类对话框交给呈现者；返回 false 表示不接管，调用方退回内置实现。</summary>
    private async Task<bool> TryShowNoticeAsync(bool isError, string title, string message, string? detail)
    {
        if (_presenter is not { } presenter)
        {
            return false;
        }

        var delegated = await _dispatcher
            .InvokeAsync(() => presenter.ShowNoticeAsync(isError, title, message, detail))
            .ConfigureAwait(false);
        return await delegated.ConfigureAwait(false) is not null;
    }

    private static void CancelQuietly(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 对话框已经关了，取消请求无意义
        }
    }

    // ---- UI 线程上执行的部分：以下方法全部要求已在 UI 线程 ----

    private async Task<int> ShowChoiceCore(string title, string message, string? detail)
    {
        var window = CreateWindow(title);
        var primaryButton = new Button { Content = "确定", IsDefault = true, MinWidth = 88 };
        var secondaryButton = new Button { Content = "取消", IsCancel = true, MinWidth = 88 };

        var result = -1;
        primaryButton.Click += (_, _) => { result = 0; window.Close(); };
        secondaryButton.Click += (_, _) => { result = 1; window.Close(); };

        window.Content = BuildLayout(message, detail, primaryButton, secondaryButton);
        await ShowCore(window).ConfigureAwait(true);
        return result;
    }

    private async Task<int> ShowSelectionCore(string title, string message, IReadOnlyList<string> options, int defaultIndex)
    {
        var window = CreateWindow(title);
        var list = new ListBox
        {
            ItemsSource = options,
            SelectedIndex = defaultIndex >= 0 && defaultIndex < options.Count ? defaultIndex : 0,
            MinHeight = 160,
            MaxHeight = 320,
        };

        var confirmButton = new Button { Content = "确定", IsDefault = true, MinWidth = 88 };
        var cancelButton = new Button { Content = "取消", IsCancel = true, MinWidth = 88 };

        var result = -1;
        confirmButton.Click += (_, _) => { result = list.SelectedIndex; window.Close(); };
        cancelButton.Click += (_, _) => { result = -1; window.Close(); };

        var content = BuildLayout(message, detail: null, confirmButton, cancelButton);
        if (content is StackPanel panel)
        {
            panel.Children.Insert(1, list);
        }

        window.Content = content;
        await ShowCore(window).ConfigureAwait(true);

        // 用户直接点关闭按钮：列表项已被取消语义化处理
        return result;
    }

    private async Task ShowNoticeCore(string title, string message, string? detail)
    {
        var window = CreateWindow(title);
        var closeButton = new Button { Content = "关闭", IsDefault = true, IsCancel = true, MinWidth = 88 };
        closeButton.Click += (_, _) => window.Close();
        window.Content = BuildLayout(message, detail, closeButton, null);
        await ShowCore(window).ConfigureAwait(true);
    }

    private ProgressDialog ShowProgressCore(string title)
    {
        var window = CreateWindow(title);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 18 };
        var cancelButton = new Button { Content = "取消", MinWidth = 88 };
        var dialog = new ProgressDialog(window, bar);

        cancelButton.Click += (_, _) =>
        {
            cancelButton.IsEnabled = false;
            dialog.RequestCancel();
        };

        window.Content = BuildLayout("正在处理，请稍候……", detail: null, cancelButton, null, bar);
        window.Closing += (_, args) =>
        {
            // 用户点右上角关闭 = 请求取消，但不让窗口先关，否则 work 还在跑会留下孤儿任务
            if (!dialog.IsFinished)
            {
                args.Cancel = true;
                cancelButton.IsEnabled = false;
                dialog.RequestCancel();
            }
        };

        ShowCore(window);
        return dialog;
    }

    private Window CreateWindow(string title) => new()
    {
        Title = title,
        Width = WindowWidth,
        SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false,
    };

    private Control BuildLayout(string message, string? detail, Button? primary, Button? secondary, Control? extra = null)
    {
        var stack = new StackPanel { Spacing = Spacing, Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        if (extra is not null)
        {
            stack.Children.Add(extra);
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                FontSize = 12,
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        if (secondary is not null)
        {
            buttons.Children.Add(secondary);
        }

        if (primary is not null)
        {
            buttons.Children.Add(primary);
        }

        stack.Children.Add(buttons);
        return stack;
    }

    /// <summary>显示窗口并等它关闭。owner 存在时严格模态，否则降级非模态。</summary>
    private Task ShowCore(Window window)
    {
        var owner = _ownerProvider?.Invoke();
        if (owner is not null && owner.IsVisible)
        {
            return window.ShowDialog(owner);
        }

        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        return closed.Task;
    }

    /// <summary>带进度条的对话框句柄：把「跑完了」与「用户点了取消」两个状态分开，避免 Closing 里误判。</summary>
    private sealed class ProgressDialog
    {
        private bool _cancelRequested;

        public ProgressDialog(Window window, ProgressBar bar)
        {
            Window = window;
            Bar = bar;
            Window.Closed += (_, _) => IsFinished = true;
        }

        public Window Window { get; }

        public ProgressBar Bar { get; }

        public bool IsFinished { get; private set; }

        public event EventHandler? CancelRequested;

        public void RequestCancel()
        {
            if (_cancelRequested)
            {
                return;
            }

            _cancelRequested = true;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Close()
        {
            IsFinished = true;
            Window.Close();
        }
    }

    /// <summary>把 ProgressBar 的更新从任意线程安全地送回 UI 线程。</summary>
    private sealed class DispatcherProgress : IProgress<double>
    {
        private readonly IDispatcher _dispatcher;
        private readonly ProgressBar _bar;

        public DispatcherProgress(IDispatcher dispatcher, ProgressBar bar)
        {
            _dispatcher = dispatcher;
            _bar = bar;
        }

        public void Report(double value)
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_dispatcher.IsOnUiThread)
            {
                _bar.Value = clamped;
                return;
            }

            // Post 而不是 InvokeAsync：进度是高频、可丢的，不需要等 UI 线程应答
            _dispatcher.Post(() => _bar.Value = clamped);
        }
    }
}
