using CKAN.Configuration;
using CKAN.Games;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Settings;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class SettingsServiceTests
{
    [Test]
    public async Task ConcurrentSets_PreserveEveryField_AndRaiseOneChangeEach()
    {
        using var temp = new TempWorkspace("settings-service");
        var config = new JsonConfiguration(temp.Combine("config.json"));
        using var service = new SettingsService(config, new InlineDispatcher());
        var changed = new List<string>();
        service.SettingChanged += (key, _) => changed.Add(key);
        var writes = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
            await service.SetAsync($"AuthToken:host{i}.example", $"token{i}"))).ToArray();

        var results = await Task.WhenAll(writes);
        Assert.That(results.All(result => result.IsSuccess), Is.True);
        var all = await service.GetAllAsync();
        Assert.That(all.IsSuccess, Is.True);
        for (var i = 0; i < 8; i++)
            Assert.That(all.Value![$"AuthToken:host{i}.example"], Is.EqualTo($"token{i}"));
        Assert.That(changed.Count, Is.EqualTo(8));
    }

    [Test]
    public async Task SetAndVerifyAsync_ReportsRejectedCoreValue()
    {
        using var temp = new TempWorkspace("settings-verify");
        var config = new JsonConfiguration(temp.Combine("config.json"));
        using var service = new SettingsService(config, new InlineDispatcher());
        var result = await service.SetAndVerifyAsync(SettingKeys.Language, "not-a-language");
        Assert.That(result.IsSuccess, Is.False);
        // Core 静默丢弃非法值属于「输入被拒绝」，原值重试永远不会成功 → 不可重试（§4.3 同款语义）。
        Assert.That(result.Retryable, Is.False);
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsOnUiThread => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }
}
