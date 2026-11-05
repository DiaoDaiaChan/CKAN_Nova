using CkanUI.Plugin.Soundtrack.Playlists;
using CkanUI.Plugin.Soundtrack.UI;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.UI;

/// <summary>
/// 独立乐曲库窗口的状态：筛选、排序与“加入后立刻从可加入集合里消失”。
/// </summary>
[TestFixture]
public sealed class SoundtrackLibraryPickerViewModelTests
{
    private static SoundtrackLibraryRowViewModel Row(string name, string extension, long bytes, bool added = false)
    {
        var row = new SoundtrackLibraryRowViewModel(
            new SoundtrackMusicFile(name, Path.Combine("/music", extension, name), bytes, extension));
        row.IsAdded = added;
        return row;
    }

    private static List<SoundtrackLibraryRowViewModel> CreateRows() =>
    [
        Row("launch.ogg", ".ogg", 100),
        Row("ambient.mp3", ".mp3", 300),
        Row("space.ogg", ".ogg", 200, added: true),
    ];

    [Test]
    public void 搜索同时匹配文件名和完整路径()
    {
        var picker = new SoundtrackLibraryPickerViewModel(CreateRows(), _ => true);

        picker.SearchText = "launch";
        Assert.Multiple(() =>
        {
            Assert.That(picker.Rows.Select(row => row.Name), Is.EqualTo(new[] { "launch.ogg" }));
            Assert.That(picker.SummaryText, Does.Contain("当前显示 1 首"));
        });

        picker.SearchText = "ambient";
        Assert.That(picker.Rows.Select(row => row.Name), Is.EqualTo(new[] { "ambient.mp3" }));
    }

    [Test]
    public void 按扩展名筛选并且只看未加入()
    {
        var picker = new SoundtrackLibraryPickerViewModel(CreateRows(), _ => true);

        picker.ExtensionFilter = ".ogg";
        Assert.That(picker.Rows.Select(row => row.Name), Is.EqualTo(new[] { "launch.ogg", "space.ogg" }));

        picker.OnlyUnadded = true;
        Assert.Multiple(() =>
        {
            Assert.That(picker.Rows.Select(row => row.Name), Is.EqualTo(new[] { "launch.ogg" }));
            Assert.That(picker.HasRows, Is.True);
        });
    }

    [Test]
    public void 排序按大小升降切换()
    {
        var picker = new SoundtrackLibraryPickerViewModel(CreateRows(), _ => true);

        picker.SortMode = "大小 小→大";
        Assert.That(picker.Rows.Select(row => row.Name),
            Is.EqualTo(new[] { "launch.ogg", "space.ogg", "ambient.mp3" }));

        picker.SortMode = "大小 大→小";
        Assert.That(picker.Rows.Select(row => row.Name),
            Is.EqualTo(new[] { "ambient.mp3", "space.ogg", "launch.ogg" }));
    }

    [Test]
    public void 添加成功后写回行状态并把它移出只看未加入的结果()
    {
        var rows = CreateRows();
        var picker = new SoundtrackLibraryPickerViewModel(rows, row =>
        {
            row.IsAdded = true;
            return true;
        });
        picker.OnlyUnadded = true;
        var target = picker.Rows.Single(row => row.Name == "launch.ogg");

        picker.AddCommand.Execute(target);

        Assert.Multiple(() =>
        {
            Assert.That(target.IsAdded, Is.True);
            Assert.That(picker.StatusText, Does.Contain("已加入"));
            Assert.That(picker.Rows.Select(row => row.Name), Does.Not.Contain("launch.ogg"));
            Assert.That(picker.SummaryText, Does.Contain("当前显示 1 首"));
        });
    }

    [Test]
    public void 已经加入的行不会重复调用添加回调()
    {
        var calls = 0;
        var row = Row("space.ogg", ".ogg", 200, added: true);
        var picker = new SoundtrackLibraryPickerViewModel([row], _ =>
        {
            calls++;
            return true;
        });

        picker.AddCommand.Execute(row);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Zero);
            Assert.That(picker.StatusText, Does.Contain("已经在"));
        });
    }
}
