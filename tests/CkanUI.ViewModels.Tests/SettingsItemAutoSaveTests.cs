using CkanUI.ViewModels.Pages.Settings;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 设置项的即时保存（第二阶段 UI 升级）：整数走数值微调器、改完停顿即落盘、
/// 连续编辑只保存最后一次、非法值与关闭开关时不自动保存。
/// </summary>
[TestFixture]
public sealed class SettingsItemAutoSaveTests
{
    private static SettingItemViewModel Integer(long? min = null, long? max = null, Action<string>? onSave = null)
    {
        var item = new SettingItemViewModel(
            "cache_limit",
            "缓存容量上限",
            SettingItemKind.Integer,
            SettingPersistence.UiSettings,
            SettingGroupKey.DownloadCache,
            min: min,
            max: max,
            saveHandler: vm =>
            {
                onSave?.Invoke(vm.WireValue);
                vm.MarkSaved();
                return Task.CompletedTask;
            });

        item.ApplyLoaded("10");
        item.AutoSaveDelay = TimeSpan.FromMilliseconds(20);
        return item;
    }

    [Test]
    public void IntegerKind_UsesNumericEditorInsteadOfTextBox()
    {
        var item = Integer(min: 0, max: 2048);

        Assert.Multiple(() =>
        {
            Assert.That(item.IsNumericKind, Is.True);
            Assert.That(item.IsSingleLine, Is.False, "整数项不能同时露一个文本框");
            Assert.That(item.NumericValue, Is.EqualTo(10d));
            Assert.That(item.NumericMinimum, Is.EqualTo(0d));
            Assert.That(item.NumericMaximum, Is.EqualTo(2048d));
        });
    }

    [Test]
    public void NumericValue_RoundsToInvariantInteger()
    {
        var item = Integer(min: 0, max: 2048);

        item.NumericValue = 42.6;

        Assert.That(item.EditValue, Is.EqualTo("43"));
    }

    [Test]
    public void NumericValue_FallsBackToMinimumWhenTextIsNotANumber()
    {
        var item = Integer(min: 7);

        item.EditValue = "not-a-number";

        Assert.That(item.NumericValue, Is.EqualTo(7d));
    }

    [Test]
    public async Task AutoSave_WritesOnceAfterQuietPeriod()
    {
        var saved = new List<string>();
        var item = Integer(onSave: saved.Add);

        item.EditValue = "12";
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.EqualTo(new[] { "12" }));
            Assert.That(item.IsDirty, Is.False, "保存成功后应清掉脏标记");
        });
    }

    [Test]
    public async Task AutoSave_OnlyPersistsTheLastEditInABurst()
    {
        var saved = new List<string>();
        var item = Integer(onSave: saved.Add);

        item.EditValue = "11";
        item.EditValue = "12";
        item.EditValue = "13";
        await Task.Delay(200);

        Assert.That(saved, Is.EqualTo(new[] { "13" }));
    }

    [Test]
    public async Task AutoSave_DoesNotFireForInvalidValue()
    {
        var saved = new List<string>();
        var item = Integer(min: 0, max: 100, onSave: saved.Add);

        item.EditValue = "abc";
        await Task.Delay(150);

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.Empty);
            Assert.That(item.HasError, Is.True);
        });
    }

    [Test]
    public async Task AutoSave_DoesNotFireWhenDisabled()
    {
        var saved = new List<string>();
        var item = Integer(onSave: saved.Add);

        item.AutoSave = false;
        item.EditValue = "12";
        await Task.Delay(150);

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.Empty);
            Assert.That(item.IsDirty, Is.True);
        });
    }

    [Test]
    public async Task Revert_CancelsPendingAutoSave()
    {
        var saved = new List<string>();
        var item = Integer(onSave: saved.Add);
        item.AutoSaveDelay = TimeSpan.FromMilliseconds(80);

        item.EditValue = "12";
        item.Revert();
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.Empty);
            Assert.That(item.EditValue, Is.EqualTo("10"));
        });
    }
}
