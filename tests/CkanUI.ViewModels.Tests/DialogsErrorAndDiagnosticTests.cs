using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Dialogs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>错误/崩溃/诊断对话框：错误条目渲染与"复制诊断信息"拼装（03 篇 §8 D-03/D-08，06 篇 UI-09）。</summary>
[TestFixture]
public sealed class DialogsErrorAndDiagnosticTests
{
    private static ErrorEntry SampleEntry() => new(
        new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Local),
        "加载索引",
        "索引下载失败",
        "HTTP 404",
        new InvalidOperationException("boom"));

    [Test]
    public void Error_渲染错误条目的各字段()
    {
        var vm = new ErrorDialogViewModel(SampleEntry());

        Assert.That(vm.Title, Is.EqualTo("出错了"));
        Assert.That(vm.UserMessage, Is.EqualTo("索引下载失败"));
        Assert.That(vm.ContextText, Is.EqualTo("加载索引"));
        Assert.That(vm.Detail, Is.EqualTo("HTTP 404"));
        Assert.That(vm.HasDetail, Is.True);
        Assert.That(vm.HasException, Is.True);
        Assert.That(vm.ExceptionText, Does.Contain("boom"));
        Assert.That(vm.TimestampText, Is.EqualTo("2024-05-06 07:08:09"));
    }

    [Test]
    public void Error_诊断文本按固定字段顺序拼装()
    {
        var text = new ErrorDialogViewModel(SampleEntry()).DiagnosticText;

        var timeIndex = text.IndexOf("时间: 2024-05-06", StringComparison.Ordinal);
        var contextIndex = text.IndexOf("上下文: 加载索引", StringComparison.Ordinal);
        var messageIndex = text.IndexOf("用户消息: 索引下载失败", StringComparison.Ordinal);
        var detailIndex = text.IndexOf("详情: HTTP 404", StringComparison.Ordinal);
        var exceptionIndex = text.IndexOf("异常:", StringComparison.Ordinal);

        Assert.That(timeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(contextIndex, Is.GreaterThan(timeIndex));
        Assert.That(messageIndex, Is.GreaterThan(contextIndex));
        Assert.That(detailIndex, Is.GreaterThan(messageIndex));
        Assert.That(exceptionIndex, Is.GreaterThan(detailIndex));
        Assert.That(text, Does.Contain("System.InvalidOperationException"));
    }

    [Test]
    public void Error_空上下文与无异常时的占位()
    {
        var entry = new ErrorEntry(DateTime.Now, "  ", "出错了", null, null);
        var vm = new ErrorDialogViewModel(entry);

        Assert.That(vm.ContextText, Is.EqualTo("未标注上下文"));
        Assert.That(vm.HasDetail, Is.False);
        Assert.That(vm.HasException, Is.False);
        Assert.That(vm.DiagnosticText, Does.Not.Contain("异常:"));
    }

    [Test]
    public async Task Error_复制诊断信息调用注入的剪贴板委托且不关闭窗口()
    {
        string? copied = null;
        var vm = new ErrorDialogViewModel(SampleEntry(), text =>
        {
            copied = text;
            return Task.CompletedTask;
        });

        await vm.CopyDetailsCommand.ExecuteAsync(null);

        Assert.That(copied, Is.EqualTo(vm.DiagnosticText));
        Assert.That(vm.CopyStatus, Is.EqualTo("已复制到剪贴板"));
        Assert.That(vm.HasCopyStatus, Is.True);
        Assert.That(vm.IsClosed, Is.False, "复制按钮不应关闭对话框");
    }

    [Test]
    public async Task Error_没有剪贴板通道时只提示不抛异常()
    {
        var vm = new ErrorDialogViewModel(SampleEntry());

        await vm.CopyDetailsCommand.ExecuteAsync(null);

        Assert.That(vm.CopyStatus, Is.EqualTo("当前环境没有剪贴板通道，无法复制"));
        Assert.That(vm.IsClosed, Is.False);
    }

    [Test]
    public async Task Error_剪贴板写入失败时给出失败提示()
    {
        var vm = new ErrorDialogViewModel(SampleEntry(), _ => throw new InvalidOperationException("剪贴板被占用"));

        await vm.CopyDetailsCommand.ExecuteAsync(null);

        Assert.That(vm.CopyStatus, Is.EqualTo("复制失败: 剪贴板被占用"));
    }

    [Test]
    public void Crash_诊断文本包含日志路径且默认返回程序()
    {
        var vm = new CrashDialogViewModel(new InvalidOperationException("崩溃了"), "logs/ckan-ui.log");

        Assert.That(vm.HasLogPath, Is.True);
        Assert.That(vm.LogPathText, Is.EqualTo("logs/ckan-ui.log"));
        Assert.That(vm.DiagnosticText, Does.Contain("日志文件: logs/ckan-ui.log"));
        Assert.That(vm.DiagnosticText, Does.Contain("崩溃了"));
        Assert.That(vm.IsDetailExpanded, Is.True);
        Assert.That(vm.Result, Is.EqualTo(CrashDialogResult.ReturnToApp));
    }

    [Test]
    public void Crash_无日志路径时占位且退出按钮返回Exit()
    {
        var vm = new CrashDialogViewModel(new InvalidOperationException("崩溃了"));

        Assert.That(vm.HasLogPath, Is.False);
        Assert.That(vm.LogPathText, Is.EqualTo("本次未记录日志路径"));

        vm.PrimaryCommand.Execute(null);
        Assert.That(vm.Result, Is.EqualTo(CrashDialogResult.Exit));

        var back = new CrashDialogViewModel(new InvalidOperationException("崩溃了"));
        back.SecondaryCommand.Execute(null);
        Assert.That(back.Result, Is.EqualTo(CrashDialogResult.ReturnToApp));
    }

    [Test]
    public void Diagnostic_空状态有文案且复制文本标明无内容()
    {
        var vm = new DiagnosticDialogViewModel("诊断信息", []);

        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.EmptyText, Is.EqualTo("暂无诊断信息"));
        Assert.That(vm.DiagnosticText, Does.Contain("（无）"));
    }

    [Test]
    public async Task Diagnostic_复制文本按顺序拼装键值并调用委托()
    {
        string? copied = null;
        var vm = new DiagnosticDialogViewModel(
            "诊断信息",
            [new DiagnosticItem("实例", "默认"), new DiagnosticItem("CKAN 版本", "1.34.6")],
            text =>
            {
                copied = text;
                return Task.CompletedTask;
            });

        Assert.That(vm.IsEmpty, Is.False);
        await vm.CopyAllCommand.ExecuteAsync(null);

        Assert.That(copied, Is.EqualTo(vm.DiagnosticText));
        Assert.That(vm.DiagnosticText, Does.Contain("实例: 默认"));
        Assert.That(vm.DiagnosticText.IndexOf("实例: 默认", StringComparison.Ordinal),
            Is.LessThan(vm.DiagnosticText.IndexOf("CKAN 版本: 1.34.6", StringComparison.Ordinal)));
        Assert.That(vm.CopyStatus, Is.EqualTo("已复制到剪贴板"));
    }
}
