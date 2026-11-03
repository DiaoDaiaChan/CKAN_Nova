using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Abstractions.Security;
using CkanUI.ViewModels.Pages.Logs;

using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// AI 面板的纪律：预览不能偷偷发请求、配置不全不许发、Key 写盘必须过加密、
/// 结果按置信度呈现且低置信度可识别。这些都在 VM 层钉死，视图只负责摆位置。
/// </summary>
[TestFixture]
public sealed class AiDiagnosisViewModelTests
{
    private static LogEvent Event(int seq, LogLevel level, string message) =>
        LogEvent.Create(LogSourceKind.Game, level, "game:测试实例", message) with { Seq = seq };

    [Test]
    public void Prepare_OnlyFillsPreview()
    {
        var llm = new RecordingLlm();
        var vm = new AiDiagnosisViewModel(llm, new FakeSettings(), new PassthroughSecretProtector());

        vm.Prepare(new[] { Event(1, LogLevel.Error, "boom") }, "测试实例", "1.12.5", "游戏日志");

        Assert.That(llm.Calls, Is.EqualTo(0));
        Assert.That(vm.PreviewText, Does.Contain("#1 "));
        Assert.That(vm.CanSend, Is.True);
    }

    [Test]
    public async Task Send_UsesThePreviewedPromptAndFillsResult()
    {
        var llm = new RecordingLlm();
        var vm = new AiDiagnosisViewModel(llm, new FakeSettings(), new PassthroughSecretProtector())
        {
            BaseUrl = "https://api.example.com/v1",
            Model = "m",
            ApiKey = "sk-1",
        };
        vm.Prepare(new[] { Event(3, LogLevel.Error, "boom") }, "i", null, "游戏日志");

        await vm.SendCommand.ExecuteAsync(null);

        Assert.That(llm.Calls, Is.EqualTo(1));
        Assert.That(llm.LastRequest!.Prompt, Is.EqualTo(vm.PreviewText));
        Assert.That(vm.Summary, Is.EqualTo("结论"));
        Assert.That(vm.Hypotheses, Has.Count.EqualTo(2));
        Assert.That(vm.Hypotheses[0].EvidenceText, Is.EqualTo("#3, #4"));
        Assert.That(vm.Hypotheses[0].IsLowConfidence, Is.False);
        Assert.That(vm.Hypotheses[1].IsLowConfidence, Is.True);
        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task Send_RefusesWhenConfigurationIsIncomplete()
    {
        var llm = new RecordingLlm();
        var vm = new AiDiagnosisViewModel(llm, new FakeSettings(), new PassthroughSecretProtector());
        vm.Prepare(new[] { Event(1, LogLevel.Info, "x") }, "i", null, "界面日志");

        await vm.SendCommand.ExecuteAsync(null);

        Assert.That(llm.Calls, Is.EqualTo(0));
        Assert.That(vm.StatusText, Does.Contain("接口地址"));
    }

    [Test]
    public void CanSend_IsFalseWithoutLlmService()
    {
        var vm = new AiDiagnosisViewModel();
        vm.Prepare(new[] { Event(1, LogLevel.Info, "x") }, "i", null, "界面日志");

        Assert.That(vm.CanSend, Is.False);
        Assert.That(vm.StatusText, Does.Contain("没接线"));
    }

    [Test]
    public void SaveConfiguration_ProtectsKeyAndReloadsIt()
    {
        var settings = new FakeSettings();
        var protector = new PrefixSecretProtector();
        var vm = new AiDiagnosisViewModel(null, settings, protector)
        {
            BaseUrl = " https://api.example.com/v1 ",
            Model = "m",
            ApiKey = "sk-secret",
        };

        vm.SaveConfiguration();

        Assert.That(settings.Saved, Has.Count.EqualTo(1));
        Assert.That(settings.Saved[0].AiBaseUrl, Is.EqualTo("https://api.example.com/v1"));
        Assert.That(settings.Saved[0].AiApiKeyProtected, Is.Not.Null);
        Assert.That(settings.Saved[0].AiApiKeyProtected, Is.Not.EqualTo("sk-secret"), "落盘的必须是密文");

        var reopened = new AiDiagnosisViewModel(null, settings, protector);
        Assert.That(reopened.ApiKey, Is.EqualTo("sk-secret"));
        Assert.That(reopened.Model, Is.EqualTo("m"));
    }

    [Test]
    public void SaveConfiguration_RefusesToStoreKeyWhenEncryptionFails()
    {
        var settings = new FakeSettings();
        var vm = new AiDiagnosisViewModel(null, settings, new BrokenSecretProtector())
        {
            ApiKey = "sk-secret",
        };

        vm.SaveConfiguration();

        Assert.That(settings.Saved, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("无法安全加密"));
    }

    [Test]
    public async Task Send_SurfacesFailureMessage()
    {
        var llm = new RecordingLlm { Failure = "接口返回 401：bad key" };
        var vm = new AiDiagnosisViewModel(llm, new FakeSettings(), new PassthroughSecretProtector())
        {
            BaseUrl = "https://api.example.com/v1",
            Model = "m",
            ApiKey = "sk-1",
        };
        vm.Prepare(new[] { Event(1, LogLevel.Info, "x") }, "i", null, "界面日志");

        await vm.SendCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Does.Contain("401"));
        Assert.That(vm.HasResult, Is.False);
    }

    private sealed class RecordingLlm : ILlmDiagnosticService
    {
        public int Calls { get; private set; }

        public LlmDiagnosisRequest? LastRequest { get; private set; }

        public string? Failure { get; init; }

        public Task<OperationResult<LlmDiagnosis>> DiagnoseAsync(LlmDiagnosisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            if (Failure is not null)
            {
                return Task.FromResult(OperationResult<LlmDiagnosis>.Fail(Failure));
            }

            return Task.FromResult(OperationResult<LlmDiagnosis>.Ok(new LlmDiagnosis(
                "结论",
                "high",
                new[]
                {
                    new LlmHypothesis("原因甲", 0.9, new[] { 3, 4 }, "依据", "修法"),
                    new LlmHypothesis("原因乙", 0.2, Array.Empty<int>(), "依据", "修法"),
                },
                Array.Empty<string>())));
        }
    }

    private sealed class FakeSettings : IUiSettingsStore
    {
        public UiSettings Current { get; private set; } = new();

        public List<UiSettings> Saved { get; } = new();

        public event Action<UiSettings>? Changed;

        public UiSettings Load() => Current;

        public OperationResult<bool> Save(UiSettings settings)
        {
            Saved.Add(settings);
            Current = settings;
            Changed?.Invoke(settings);
            return OperationResult<bool>.Ok(true);
        }
    }

    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string? Protect(string? plainText) => plainText is null ? null : "enc:" + plainText;

        public string? Unprotect(string? protectedText) =>
            protectedText is not null && protectedText.StartsWith("enc:", StringComparison.Ordinal)
                ? protectedText[4..]
                : null;
    }

    private sealed class BrokenSecretProtector : ISecretProtector
    {
        public string? Protect(string? plainText) => null;

        public string? Unprotect(string? protectedText) => null;
    }
}
