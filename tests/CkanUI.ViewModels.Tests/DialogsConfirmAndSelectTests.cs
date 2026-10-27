using CkanUI.ViewModels.Dialogs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>确认/单选对话框：返回值语义与按钮文案（03 篇 §8 D-01/D-02，02 篇 §4）。</summary>
[TestFixture]
public sealed class DialogsConfirmAndSelectTests
{
    [Test]
    public void Confirm_主按钮返回True并关闭()
    {
        var vm = new ConfirmDialogViewModel("确认卸载", "确定要卸载吗？");

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.Result, Is.True);
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void Confirm_取消返回False并关闭()
    {
        var vm = new ConfirmDialogViewModel("确认卸载", "确定要卸载吗？");

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Result, Is.False);
        Assert.That(vm.IsConfirmed, Is.False);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void Confirm_按钮文案可定制且默认不显示不再询问()
    {
        var vm = new ConfirmDialogViewModel("覆盖安装", "继续？", confirmText: "覆盖", cancelText: "放弃");

        Assert.That(vm.PrimaryButtonText, Is.EqualTo("覆盖"));
        Assert.That(vm.SecondaryButtonText, Is.EqualTo("放弃"));
        Assert.That(vm.ShowDoNotAskAgain, Is.False);
        Assert.That(vm.DoNotAskAgain, Is.False);
    }

    [Test]
    public void Confirm_空白详情视为没有详情()
    {
        var vm = new ConfirmDialogViewModel("标题", "消息", detail: "   ");

        Assert.That(vm.HasDetail, Is.False);
        Assert.That(vm.Detail, Is.Null);
    }

    [Test]
    public void Select_确定返回0基索引()
    {
        var vm = new SelectDialogViewModel("选择实例", "请选择", ["默认", "测试", "生产"], defaultIndex: 1);

        Assert.That(vm.SelectedIndex, Is.EqualTo(1));
        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.Result, Is.EqualTo(1));
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void Select_取消返回负一()
    {
        var vm = new SelectDialogViewModel("选择实例", "请选择", ["默认", "测试"]);

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Result, Is.EqualTo(-1));
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void Select_空列表主按钮不提交并报校验错误()
    {
        var vm = new SelectDialogViewModel("选择实例", "请选择", []);

        Assert.That(vm.HasOptions, Is.False);
        Assert.That(vm.SelectedIndex, Is.EqualTo(-1));

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.ValidationError, Is.EqualTo("没有可选项"));
        Assert.That(vm.HasValidationError, Is.True);
        Assert.That(vm.IsClosed, Is.False);
        Assert.That(vm.Result, Is.EqualTo(-1));
    }

    [Test]
    public void Select_默认索引越界时夹到合法区间()
    {
        var tooBig = new SelectDialogViewModel("选择", "请选择", ["甲", "乙", "丙"], defaultIndex: 99);
        var tooSmall = new SelectDialogViewModel("选择", "请选择", ["甲", "乙", "丙"], defaultIndex: -5);

        Assert.That(tooBig.SelectedIndex, Is.EqualTo(2));
        Assert.That(tooSmall.SelectedIndex, Is.EqualTo(0));
    }

    [Test]
    public void Select_双击条目等价于选中并确定()
    {
        var vm = new SelectDialogViewModel("选择", "请选择", ["甲", "乙", "丙"]);

        vm.ActivateCommand.Execute("丙");

        Assert.That(vm.SelectedIndex, Is.EqualTo(2));
        Assert.That(vm.SelectedText, Is.EqualTo("丙"));
        Assert.That(vm.Result, Is.EqualTo(2));
        Assert.That(vm.IsClosed, Is.True);
    }
}
