using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CkanUI.Platform.Dialogs;
using CkanUI.Services.Abstractions;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 对话框呈现者接缝（03 篇 §8）：<see cref="DialogService"/> 必须把已接管的对话框交给
/// <see cref="IDialogPresenter"/>，并把返回值语义原样透出（确认 true/false、单选 0 基索引/-1）。
/// 只测"接管"路径：退回内置代码建窗那条路要真 Avalonia 应用，留给手工烟测。
/// </summary>
[TestFixture]
public sealed class DialogServicePresenterTests
{
    [Test]
    public async Task Confirm_is_delegated_and_returns_presenter_result()
    {
        var presenter = new RecordingPresenter { ConfirmResult = true };
        var dispatcher = new ImmediateDispatcher();
        var service = new DialogService(dispatcher, ownerProvider: null, presenter);

        var confirmed = await service.ConfirmAsync("确认卸载", "确定要卸载吗？", "详情");

        Assert.That(confirmed, Is.True);
        Assert.That(presenter.ConfirmCalls, Is.EqualTo(1));
        Assert.That(presenter.LastTitle, Is.EqualTo("确认卸载"));
        Assert.That(presenter.LastDetail, Is.EqualTo("详情"));
        Assert.That(dispatcher.Invocations, Is.EqualTo(1), "呈现者必须在 UI 线程封送里被调用");
    }

    [Test]
    public async Task Confirm_declined_by_user_returns_false()
    {
        var service = new DialogService(new ImmediateDispatcher(), null, new RecordingPresenter { ConfirmResult = false });

        Assert.That(await service.ConfirmAsync("t", "m"), Is.False);
    }

    [Test]
    public async Task Select_is_delegated_and_keeps_zero_based_index()
    {
        var presenter = new RecordingPresenter { SelectResult = 2 };
        var service = new DialogService(new ImmediateDispatcher(), null, presenter);

        var index = await service.SelectAsync("选择实例", "请选择", new[] { "a", "b", "c" }, defaultIndex: 1);

        Assert.That(index, Is.EqualTo(2));
        Assert.That(presenter.LastOptions, Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(presenter.LastDefaultIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task Select_cancelled_stays_minus_one()
    {
        var service = new DialogService(new ImmediateDispatcher(), null, new RecordingPresenter { SelectResult = -1 });

        Assert.That(await service.SelectAsync("t", "m", new[] { "a" }), Is.EqualTo(-1));
    }

    [Test]
    public async Task Error_notice_is_delegated_as_error()
    {
        var presenter = new RecordingPresenter { NoticeResult = true };
        var service = new DialogService(new ImmediateDispatcher(), null, presenter);

        await service.ShowErrorAsync("出错了", "安装失败", "堆栈");

        Assert.That(presenter.NoticeCalls, Is.EqualTo(1));
        Assert.That(presenter.LastNoticeWasError, Is.True);
    }

    [Test]
    public void Service_without_presenter_constructs()
    {
        Assert.DoesNotThrow(() => _ = new DialogService(new ImmediateDispatcher()));
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public int Invocations { get; private set; }

        public bool IsOnUiThread => true;

        public void Post(Action action)
        {
            Invocations++;
            action();
        }

        public Task InvokeAsync(Action action)
        {
            Invocations++;
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            Invocations++;
            return Task.FromResult(func());
        }
    }

    private sealed class RecordingPresenter : IDialogPresenter
    {
        public bool ConfirmResult { get; init; }

        public int SelectResult { get; init; } = -1;

        public bool? NoticeResult { get; init; } = true;

        public int ConfirmCalls { get; private set; }

        public int NoticeCalls { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastDetail { get; private set; }

        public IReadOnlyList<string>? LastOptions { get; private set; }

        public int LastDefaultIndex { get; private set; }

        public bool LastNoticeWasError { get; private set; }

        public Task<bool?> ConfirmAsync(string title, string message, string? detail)
        {
            ConfirmCalls++;
            LastTitle = title;
            LastDetail = detail;
            return Task.FromResult<bool?>(ConfirmResult);
        }

        public Task<int?> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex)
        {
            LastOptions = options;
            LastDefaultIndex = defaultIndex;
            return Task.FromResult<int?>(SelectResult);
        }

        public Task<bool?> ShowNoticeAsync(bool isError, string title, string message, string? detail)
        {
            NoticeCalls++;
            LastNoticeWasError = isError;
            return Task.FromResult(NoticeResult);
        }
    }
}
