using System;
using System.Collections.Generic;
using System.Windows.Input;

using CkanUI.App.Ai;

using CommunityToolkit.Mvvm.Input;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 界面自省器：反射出来的字段/命令必须只包含「一句话说得清」的东西，
/// 密钥要打码，命令失败要变成可读的失败结果而不是异常穿透到 AI 对话里。
/// </summary>
[TestFixture]
public sealed class AiUiIntrospectorTests
{
    private enum Density
    {
        Standard,
        Compact,
    }

    [Test]
    public void DescribeFields_ExposesScalarsAndMasksSecrets()
    {
        var viewModel = new FakePageViewModel();

        var fields = AiUiIntrospector.DescribeFields(viewModel);
        var byName = new Dictionary<string, string?>();
        foreach (var field in fields)
        {
            byName[field.Name] = field.Value;
        }

        Assert.Multiple(() =>
        {
            Assert.That(byName["SearchText"], Is.EqualTo("warp"));
            Assert.That(byName["Total"], Is.EqualTo("42"));
            Assert.That(byName["IsBusy"], Is.EqualTo("false"));
            Assert.That(byName["Density"], Is.EqualTo("Compact"));
            Assert.That(byName["Tags"], Is.EqualTo("3 项"), "集合只回条数，别把整页数据塞进上下文");
            Assert.That(byName["ApiKey"], Is.EqualTo("***"), "密钥字段必须打码，对话要进日志");
            Assert.That(byName.ContainsKey("ApplyCommand"), Is.False, "命令不是字段，混进来模型会当成值");
            Assert.That(byName.ContainsKey("Nested"), Is.False, "复杂对象不暴露，ToString 只会浪费上下文");
        });
    }

    [Test]
    public void DescribeFields_TruncatesLongTextAndToleratesThrowingGetter()
    {
        var viewModel = new FakePageViewModel { SearchText = new string('x', 400) };

        var fields = AiUiIntrospector.DescribeFields(viewModel);
        var text = fields.Single(field => field.Name == "SearchText").Value;

        Assert.Multiple(() =>
        {
            Assert.That(text, Has.Length.EqualTo(AiUiIntrospector.MaxValueLength + 1));
            Assert.That(text, Does.EndWith("…"));
            Assert.That(fields.Select(field => field.Name), Does.Not.Contain("Broken"), "getter 抛异常只丢该字段");
        });
    }

    [Test]
    public void DescribeFields_NullViewModelIsEmpty()
    {
        Assert.That(AiUiIntrospector.DescribeFields(null), Is.Empty);
        Assert.That(AiUiIntrospector.DescribeActions(null), Is.Empty);
    }

    [Test]
    public void DescribeActions_ReportsCommandNameLabelParameterAndAvailability()
    {
        var actions = AiUiIntrospector.DescribeActions(new FakePageViewModel());
        var byName = new Dictionary<string, CkanUI.Services.Abstractions.Ai.AiPageAction>();
        foreach (var action in actions)
        {
            byName[action.Name] = action;
        }

        Assert.Multiple(() =>
        {
            Assert.That(byName["ApplyCommand"].Label, Is.EqualTo("Apply"), "标签去掉 Command 后缀，和界面按钮文字一致");
            Assert.That(byName["ApplyCommand"].CanExecute, Is.True);
            Assert.That(byName["SelectCommand"].ParameterType, Is.EqualTo("Int32"));
            Assert.That(byName["DisabledCommand"].CanExecute, Is.False);
        });
    }

    [Test]
    public void IsDestructive_FlagsDiskAndProcessSideEffectsOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AiUiIntrospector.IsDestructive("UninstallCommand"), Is.True);
            Assert.That(AiUiIntrospector.IsDestructive("ApplyChangesCommand"), Is.True);
            Assert.That(AiUiIntrospector.IsDestructive("LaunchGameCommand"), Is.True);
            Assert.That(AiUiIntrospector.IsDestructive("ApplySortCommand"), Is.False, "排序是纯视图操作，不该每次点都弹确认");
            Assert.That(AiUiIntrospector.IsDestructive("FilterCommand"), Is.False);
            Assert.That(AiUiIntrospector.IsDestructive(""), Is.False);
        });
    }

    [Test]
    public void TryFindAction_AcceptsNameWithoutCommandSuffix()
    {
        var viewModel = new FakePageViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(AiUiIntrospector.TryFindAction(viewModel, "apply", out var command, out var name), Is.True);
            Assert.That(command, Is.Not.Null);
            Assert.That(name, Is.EqualTo("ApplyCommand"));
            Assert.That(AiUiIntrospector.TryFindAction(viewModel, "NoSuchThing", out _, out _), Is.False);
        });
    }

    [Test]
    public async Task InvokeAsync_ConvertsArgumentToCommandParameterType()
    {
        var viewModel = new FakePageViewModel();

        var result = await AiUiIntrospector.InvokeAsync(viewModel, "SelectCommand", "3");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Does.Contain("SelectCommand"));
            Assert.That(viewModel.LastPayload, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task InvokeAsync_UnknownActionListsAvailableCommands()
    {
        var result = await AiUiIntrospector.InvokeAsync(new FakePageViewModel(), "Nope", null);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("Nope"));
            Assert.That(result.Detail, Does.Contain("ApplyCommand"));
        });
    }

    [Test]
    public async Task InvokeAsync_DisabledCommandDoesNotExecute()
    {
        var viewModel = new FakePageViewModel();

        var result = await AiUiIntrospector.InvokeAsync(viewModel, "DisabledCommand", null);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不可执行"));
            Assert.That(viewModel.DisabledExecutions, Is.Zero);
        });
    }

    [Test]
    public async Task InvokeAsync_CommandThatThrowsBecomesFailureResult()
    {
        var viewModel = new FakePageViewModel();

        var result = await AiUiIntrospector.InvokeAsync(viewModel, "ExplodingCommand", null);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("执行失败"));
            Assert.That(result.Detail, Does.Contain("磁盘满了"));
        });
    }

    [Test]
    public void TryConvert_RejectsInvalidEnumWithChoices()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AiUiIntrospector.TryConvert("compact", typeof(Density), out var value, out _), Is.True);
            Assert.That(value, Is.EqualTo(Density.Compact));
            Assert.That(AiUiIntrospector.TryConvert("huge", typeof(Density), out _, out var error), Is.False);
            Assert.That(error, Does.Contain("Compact"));
        });
    }

    [Test]
    public void TrySetField_WritesScalarAndReportsOldToNew()
    {
        var viewModel = new FakePageViewModel();

        var ok = AiUiIntrospector.TrySetField(viewModel, "searchtext", "realchute", out var result);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(viewModel.SearchText, Is.EqualTo("realchute"));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Does.Contain("warp"));
            Assert.That(result.Value, Does.Contain("realchute"));
        });
    }

    [Test]
    public void TrySetField_ConvertsBoolAndEnum()
    {
        var viewModel = new FakePageViewModel();

        var busy = AiUiIntrospector.TrySetField(viewModel, "IsBusy", "true", out _);
        var density = AiUiIntrospector.TrySetField(viewModel, "Density", "Standard", out _);

        Assert.Multiple(() =>
        {
            Assert.That(busy, Is.True);
            Assert.That(viewModel.IsBusy, Is.True);
            Assert.That(density, Is.True);
            Assert.That(viewModel.Density, Is.EqualTo(Density.Standard));
        });
    }

    [Test]
    public void TrySetField_RejectsSecretsUnknownAndCollection()
    {
        var viewModel = new FakePageViewModel();

        var secret = AiUiIntrospector.TrySetField(viewModel, "ApiKey", "leak", out var secretResult);
        var unknown = AiUiIntrospector.TrySetField(viewModel, "NoSuchField", "1", out var unknownResult);
        var collection = AiUiIntrospector.TrySetField(viewModel, "Tags", "a,b", out var collectionResult);

        Assert.Multiple(() =>
        {
            Assert.That(secret, Is.False);
            Assert.That(viewModel.ApiKey, Is.EqualTo("super-secret"), "密钥不许被对话改写");
            Assert.That(secretResult.Message, Does.Contain("密钥"));
            Assert.That(unknown, Is.False);
            Assert.That(unknownResult.Detail, Does.Contain("SearchText"), "失败要告诉模型有哪些可写字段");
            Assert.That(collection, Is.False);
            Assert.That(collectionResult.Message, Does.Contain("不支持"));
        });
    }

    [Test]
    public void TrySetField_RejectsUnconvertibleValue()
    {
        var viewModel = new FakePageViewModel();

        var ok = AiUiIntrospector.TrySetField(viewModel, "Total", "many", out var result);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(viewModel.Total, Is.EqualTo(42));
            Assert.That(result.Message, Does.Contain("无法转换"));
        });
    }

    private sealed class FakePageViewModel
    {
        public string SearchText { get; set; } = "warp";

        public int Total { get; set; } = 42;

        public bool IsBusy { get; set; }

        public Density Density { get; set; } = Density.Compact;

        public List<string> Tags { get; set; } = new() { "a", "b", "c" };

        public string ApiKey { get; set; } = "super-secret";

        public FakePageViewModel Nested => this;

        public string Broken => throw new InvalidOperationException("插件页 getter 炸了");

        public RelayCommand ApplyCommand { get; }

        public RelayCommand<int> SelectCommand { get; }

        public RelayCommand DisabledCommand { get; }

        public RelayCommand ExplodingCommand { get; }

        public int DisabledExecutions { get; private set; }

        public object? LastPayload { get; private set; }

        public FakePageViewModel()
        {
            ApplyCommand = new RelayCommand(() => { });
            SelectCommand = new RelayCommand<int>(payload => LastPayload = payload);
            DisabledCommand = new RelayCommand(
                () => DisabledExecutions++,
                () => false);
            ExplodingCommand = new RelayCommand(() => throw new InvalidOperationException("磁盘满了"));
        }
    }
}
