using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Shell;

using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 托盘菜单（12 篇 F-51 / UI-26）：七项顺序与文案、分组、缺宿主时的可用性、
/// 失败语义（中文错误不逸出）、以及「最小化/关闭到托盘」开关与托盘可用性的联动。
/// </summary>
/// <remarks>
/// 本文件不创建任何 <c>TrayIcon</c>：视图模型层不认识 Avalonia，
/// 图标与原生菜单的装配留在 App 层的 <c>TrayHost</c>（无 GUI 会话无法验证）。
/// </remarks>
[TestFixture]
public sealed class TrayMenuTests
{
    private static TrayMenuViewModel CreateMenu(ITrayHostActions? host = null) => new(host);

    [Test]
    public void Menu_HasSevenItemsInSpecOrder()
    {
        var menu = CreateMenu();

        Assert.That(
            menu.Items.Select(item => item.Id),
            Is.EqualTo(new[]
            {
                TrayMenuItemId.ShowWindow,
                TrayMenuItemId.HideWindow,
                TrayMenuItemId.OpenLogFolder,
                TrayMenuItemId.OpenCacheFolder,
                TrayMenuItemId.OpenInstanceFolder,
                TrayMenuItemId.RefreshRepositories,
                TrayMenuItemId.Exit,
            }));
        Assert.That(
            menu.Items.Select(item => item.Header),
            Is.EqualTo(new[]
            {
                "显示主窗口",
                "隐藏到托盘",
                "打开日志目录",
                "打开缓存目录",
                "打开当前实例目录",
                "刷新仓库",
                "退出",
            }));
        Assert.That(menu.Items.All(item => item.HasAction), Is.True);
    }

    [Test]
    public void Menu_MarksTheThreeGroupStarts()
    {
        var menu = CreateMenu();

        Assert.That(
            menu.Items.Where(item => item.StartsGroup).Select(item => item.Id),
            Is.EqualTo(new[]
            {
                TrayMenuItemId.OpenLogFolder,
                TrayMenuItemId.RefreshRepositories,
                TrayMenuItemId.Exit,
            }));
    }

    [Test]
    public void Menu_WithoutHost_DisablesEveryCommand()
    {
        var menu = CreateMenu();

        Assert.That(menu.HasHost, Is.False);
        Assert.That(menu.Items.All(item => !item.CanRun()), Is.True);
        Assert.That(menu.Items.All(item => !item.Command.CanExecute(null)), Is.True);
        Assert.That(menu.Items.All(item => item.HasAction), Is.True);
    }

    [Test]
    public void Menu_AttachingHost_EnablesEveryCommand()
    {
        var menu = CreateMenu();
        var host = new RecordingHost();

        menu.Host = host;

        Assert.That(menu.HasHost, Is.True);
        Assert.That(menu.Items.All(item => item.CanRun()), Is.True);
        Assert.That(menu.Items.All(item => item.Command.CanExecute(null)), Is.True);
    }

    [Test]
    public void Menu_DetachingHost_DisablesCommandsAndExplainsInChinese()
    {
        var menu = CreateMenu(new RecordingHost());
        Assert.That(menu.Items.All(item => item.CanRun()), Is.True);

        menu.Host = null;

        Assert.That(menu.HasHost, Is.False);
        Assert.That(menu.Items.All(item => !item.CanRun()), Is.True);
        Assert.That(menu.StatusText, Is.EqualTo("当前环境没有托盘宿主，菜单动作不可用"));
    }

    [Test]
    public async Task ShowWindow_InvokesHostAndReportsChineseStatus()
    {
        var host = new RecordingHost();
        var menu = CreateMenu(host);

        await menu.ShowWindowCommand.ExecuteAsync(null);

        Assert.That(host.ShowWindowCalls, Is.EqualTo(1));
        Assert.That(menu.StatusText, Is.EqualTo("已显示主窗口"));
        Assert.That(menu.FindItem(TrayMenuItemId.ShowWindow)!.LastError, Is.Null);
    }

    [Test]
    public async Task HideWindow_InvokesHostAndReportsChineseStatus()
    {
        var host = new RecordingHost();
        var menu = CreateMenu(host);

        await menu.HideWindowCommand.ExecuteAsync(null);

        Assert.That(host.HideWindowCalls, Is.EqualTo(1));
        Assert.That(menu.StatusText, Is.EqualTo("已隐藏到托盘"));
    }

    [Test]
    public async Task OpenLogFolder_Success_ReportsOpened()
    {
        var host = new RecordingHost { LogFolder = OperationResult.Ok() };
        var menu = CreateMenu(host);

        await menu.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.That(host.OpenLogFolderCalls, Is.EqualTo(1));
        Assert.That(menu.StatusText, Is.EqualTo("已打开日志目录"));
    }

    [Test]
    public async Task OpenCacheFolder_Failure_ReportsChineseError()
    {
        var host = new RecordingHost
        {
            CacheFolder = OperationResult.Fail("目录不存在：D:/CKAN/cache（不可重试）"),
        };
        var menu = CreateMenu(host);

        await menu.OpenCacheFolderCommand.ExecuteAsync(null);

        Assert.That(
            menu.StatusText,
            Is.EqualTo("缓存目录打开失败：目录不存在：D:/CKAN/cache（不可重试）"));
    }

    [Test]
    public async Task OpenInstanceFolder_WithoutHost_ReportsNoChannel()
    {
        var menu = CreateMenu();

        await menu.OpenInstanceFolderCommand.ExecuteAsync(null);

        Assert.That(menu.StatusText, Is.EqualTo("当前环境没有托盘宿主，无法打开实例目录"));
    }

    [Test]
    public async Task RefreshRepositories_Success_ReportsRefreshed()
    {
        var host = new RecordingHost { Refresh = OperationResult.Ok() };
        var menu = CreateMenu(host);

        await menu.RefreshRepositoriesCommand.ExecuteAsync(null);

        Assert.That(host.RefreshCalls, Is.EqualTo(1));
        Assert.That(menu.StatusText, Is.EqualTo("仓库索引已刷新"));
        Assert.That(menu.FindItem(TrayMenuItemId.RefreshRepositories)!.IsBusy, Is.False);
    }

    [Test]
    public async Task RefreshRepositories_Failure_KeepsServiceMessageAndRetryHint()
    {
        var host = new RecordingHost
        {
            Refresh = OperationResult.Fail("仓库元数据下载失败：连接被重置", retryable: true),
        };
        var menu = CreateMenu(host);

        await menu.RefreshRepositoriesCommand.ExecuteAsync(null);

        Assert.That(menu.StatusText, Is.EqualTo("刷新仓库失败：仓库元数据下载失败：连接被重置"));
    }

    [Test]
    public async Task RefreshRepositories_ThrowingHost_CapturesChineseErrorOnItem()
    {
        var host = new RecordingHost { RefreshException = new InvalidOperationException("磁盘只读") };
        var menu = CreateMenu(host);
        var item = menu.FindItem(TrayMenuItemId.RefreshRepositories)!;

        Assert.DoesNotThrowAsync(async () => await item.Command.ExecuteAsync(null));

        Assert.That(item.LastError, Is.EqualTo("执行「刷新仓库」失败：磁盘只读"));
        Assert.That(item.IsBusy, Is.False);
        Assert.That(menu.StatusText, Is.EqualTo("尚未执行托盘动作"));
    }

    [Test]
    public async Task Exit_InvokesHostAndReportsExiting()
    {
        var host = new RecordingHost();
        var menu = CreateMenu(host);

        await menu.ExitCommand.ExecuteAsync(null);

        Assert.That(host.ExitCalls, Is.EqualTo(1));
        Assert.That(menu.StatusText, Is.EqualTo("正在退出…"));
    }

    [Test]
    public void ApplySettings_ProjectsBothTogglesIntoHidePolicies()
    {
        var menu = CreateMenu(new RecordingHost());

        menu.ApplySettings(new UiSettings { MinimizeToTray = true, CloseToTray = false });

        Assert.That(menu.MinimizeToTray, Is.True);
        Assert.That(menu.CloseToTray, Is.False);
        Assert.That(menu.HideOnMinimize, Is.True);
        Assert.That(menu.HideOnClose, Is.False);
    }

    [Test]
    public void ApplySettings_Null_DisablesBothToggles()
    {
        var menu = CreateMenu(new RecordingHost());
        menu.ApplySettings(new UiSettings { MinimizeToTray = true, CloseToTray = true });

        menu.ApplySettings(null);

        Assert.That(menu.MinimizeToTray, Is.False);
        Assert.That(menu.CloseToTray, Is.False);
        Assert.That(menu.HideOnMinimize, Is.False);
        Assert.That(menu.HideOnClose, Is.False);
    }

    [Test]
    public void WhenTrayUnavailable_HidePoliciesStayOffAndToolTipSaysSo()
    {
        var menu = CreateMenu(new RecordingHost());
        menu.ApplySettings(new UiSettings { MinimizeToTray = true, CloseToTray = true });
        Assert.That(menu.ToolTipText, Is.EqualTo("CKAN"));

        menu.SetTrayAvailable(false);

        // 开关本身仍是 true（设置页照常显示已勾选），但生效判定必须是 false：
        // 否则用户会既看不到窗口、也点不到托盘图标。
        Assert.That(menu.MinimizeToTray, Is.True);
        Assert.That(menu.CloseToTray, Is.True);
        Assert.That(menu.HideOnMinimize, Is.False);
        Assert.That(menu.HideOnClose, Is.False);
        Assert.That(menu.ToolTipText, Is.EqualTo("CKAN（托盘不可用）"));

        menu.SetTrayAvailable(true);
        Assert.That(menu.HideOnMinimize, Is.True);
        Assert.That(menu.HideOnClose, Is.True);
    }

    /// <summary>记录调用的假宿主：默认全部成功，可逐项编排返回值或抛异常。</summary>
    private sealed class RecordingHost : ITrayHostActions
    {
        public int ShowWindowCalls { get; private set; }

        public int HideWindowCalls { get; private set; }

        public int OpenLogFolderCalls { get; private set; }

        public int OpenCacheFolderCalls { get; private set; }

        public int OpenInstanceFolderCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public int ExitCalls { get; private set; }

        public OperationResult LogFolder { get; set; } = OperationResult.Ok();

        public OperationResult CacheFolder { get; set; } = OperationResult.Ok();

        public OperationResult InstanceFolder { get; set; } = OperationResult.Ok();

        public OperationResult Refresh { get; set; } = OperationResult.Ok();

        public Exception? ShowWindowException { get; set; }

        public Exception? RefreshException { get; set; }

        public Task ShowWindow()
        {
            ShowWindowCalls++;
            return ShowWindowException is { } ex ? Task.FromException(ex) : Task.CompletedTask;
        }

        public Task HideWindow()
        {
            HideWindowCalls++;
            return Task.CompletedTask;
        }

        public Task<OperationResult> OpenLogFolder()
        {
            OpenLogFolderCalls++;
            return Task.FromResult(LogFolder);
        }

        public Task<OperationResult> OpenCacheFolder()
        {
            OpenCacheFolderCalls++;
            return Task.FromResult(CacheFolder);
        }

        public Task<OperationResult> OpenInstanceFolder()
        {
            OpenInstanceFolderCalls++;
            return Task.FromResult(InstanceFolder);
        }

        public Task<OperationResult> RefreshRepositories()
        {
            RefreshCalls++;
            return RefreshException is { } ex ? Task.FromException<OperationResult>(ex) : Task.FromResult(Refresh);
        }

        public Task ExitAsync()
        {
            ExitCalls++;
            return Task.CompletedTask;
        }
    }
}
