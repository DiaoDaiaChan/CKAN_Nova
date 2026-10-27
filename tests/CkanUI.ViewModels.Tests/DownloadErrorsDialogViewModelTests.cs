using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// F-38 / UI-35 / INT-08 下载失败处置对话框测试。
/// </summary>
[TestFixture]
public sealed class DownloadErrorsDialogViewModelTests
{
    private static List<DownloadErrorItemInfo> CreateSampleErrors()
    {
        return
        [
            new DownloadErrorItemInfo(
                FileName: "MechJeb2-2.14.3.0.zip",
                ModName: "MechJeb2",
                Category: "网络超时",
                SuggestedAction: "检查网络连接后重试",
                ErrorMessage: "HttpRequestException: Connection timed out",
                SourceUrl: "https://github.com/MuMech/MechJeb2/releases/download/2.14.3.0/MechJeb2-2.14.3.0.zip",
                IsRetryable: true),
            new DownloadErrorItemInfo(
                FileName: "ModuleManager-4.2.3.zip",
                ModName: "ModuleManager",
                Category: "HTTP 404",
                SuggestedAction: "跳过该模组或检查源地址",
                ErrorMessage: "WebException: The remote server returned an error: (404) Not Found",
                SourceUrl: "https://ksp.sarbian.com/jenkins/job/ModuleManager/168/artifact/ModuleManager-4.2.3.zip",
                IsRetryable: false),
            new DownloadErrorItemInfo(
                FileName: "scatterer-0.0838.zip",
                ModName: "Scatterer",
                Category: "SHA256 校验不匹配",
                SuggestedAction: "重新下载或跳过",
                ErrorMessage: "SHA256 校验不匹配：期望 a1b2c3d4，实际 11223344",
                SourceUrl: "https://spacedock.info/mod/141/scatterer/download/0.0838",
                IsRetryable: true)
        ];
    }

    [Test]
    public void Constructor_InitializesItemsAndDefaultDispositions()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors);

        Assert.That(vm.Title, Is.EqualTo("下载失败"));
        Assert.That(vm.Items, Has.Count.EqualTo(3));
        Assert.That(vm.TotalCount, Is.EqualTo(3));
        Assert.That(vm.HasItems, Is.True);

        // 第 1 项可重试，默认 Retry
        Assert.That(vm.Items[0].IsRetry, Is.True);
        Assert.That(vm.Items[0].IsSkip, Is.False);
        Assert.That(vm.Items[0].ModName, Is.EqualTo("MechJeb2"));
        Assert.That(vm.Items[0].Category, Is.EqualTo("网络超时"));

        // 第 2 项不可重试，默认 Skip
        Assert.That(vm.Items[1].IsRetry, Is.False);
        Assert.That(vm.Items[1].IsSkip, Is.True);
        Assert.That(vm.Items[1].ModName, Is.EqualTo("ModuleManager"));
        Assert.That(vm.Items[1].Category, Is.EqualTo("HTTP 404"));

        // 第 3 项可重试，默认 Retry
        Assert.That(vm.Items[2].IsRetry, Is.True);
        Assert.That(vm.Items[2].IsSkip, Is.False);

        // 摘要统计：2项重试，1项跳过
        Assert.That(vm.RetryCount, Is.EqualTo(2));
        Assert.That(vm.SkipCount, Is.EqualTo(1));
        Assert.That(vm.SummaryText, Does.Contain("共 3 项失败"));
        Assert.That(vm.SummaryText, Does.Contain("2 项重试"));
        Assert.That(vm.SummaryText, Does.Contain("1 项跳过"));
    }

    [Test]
    public void RetryAll_SetsAllItemsToRetry_SetsResult_AndCloses()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors);

        vm.RetryAllCommand.Execute(null);

        Assert.That(vm.Items.All(i => i.IsRetry), Is.True);
        Assert.That(vm.RetryCount, Is.EqualTo(3));
        Assert.That(vm.SkipCount, Is.EqualTo(0));
        Assert.That(vm.Result, Is.EqualTo(DownloadErrorsDialogResult.RetryAll));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void SkipFailed_SetsAllItemsToSkip_SetsResult_AndCloses()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors);

        vm.SkipFailedCommand.Execute(null);

        Assert.That(vm.Items.All(i => i.IsSkip), Is.True);
        Assert.That(vm.RetryCount, Is.EqualTo(0));
        Assert.That(vm.SkipCount, Is.EqualTo(3));
        Assert.That(vm.Result, Is.EqualTo(DownloadErrorsDialogResult.SkipFailed));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
    }

    [Test]
    public void SecondaryCommand_SetsResultToCancel_AndCloses()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors);

        vm.SecondaryCommand.Execute(null);

        Assert.That(vm.Result, Is.EqualTo(DownloadErrorsDialogResult.Cancel));
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.IsConfirmed, Is.False);
    }

    [Test]
    public void ItemDispositionChange_UpdatesSummaryTextAndCounts()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors);

        Assert.That(vm.RetryCount, Is.EqualTo(2));
        Assert.That(vm.SkipCount, Is.EqualTo(1));

        // 将第 1 项设为跳过
        vm.Items[0].IsSkip = true;

        Assert.That(vm.RetryCount, Is.EqualTo(1));
        Assert.That(vm.SkipCount, Is.EqualTo(2));
        Assert.That(vm.SummaryText, Does.Contain("1 项重试"));
        Assert.That(vm.SummaryText, Does.Contain("2 项跳过"));

        // 将第 2 项设为重试
        vm.Items[1].IsRetry = true;

        Assert.That(vm.RetryCount, Is.EqualTo(2));
        Assert.That(vm.SkipCount, Is.EqualTo(1));
    }

    [Test]
    public void ConfirmCustom_RetainsIndividualDispositions_SetsResultCustom_AndCloses()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors);

        // 修改第一项为跳过
        vm.Items[0].IsSkip = true;

        vm.ConfirmCustomCommand.Execute(null);

        Assert.That(vm.Result, Is.EqualTo(DownloadErrorsDialogResult.Custom));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.Items[0].IsSkip, Is.True);
        Assert.That(vm.Items[1].IsSkip, Is.True);
        Assert.That(vm.Items[2].IsRetry, Is.True);
    }

    [Test]
    public void PrimaryCommand_DefaultsToRetryAll()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors);

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.Result, Is.EqualTo(DownloadErrorsDialogResult.RetryAll));
        Assert.That(vm.IsConfirmed, Is.True);
        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.Items.All(i => i.IsRetry), Is.True);
    }

    [Test]
    public async Task CopyErrorsCommand_WithChannel_CopiesFormattedErrorText()
    {
        var errors = CreateSampleErrors();
        string? copied = null;
        var vm = new DownloadErrorsDialogViewModel(errors, text =>
        {
            copied = text;
            return Task.CompletedTask;
        });

        await vm.CopyErrorsCommand.ExecuteAsync(null);

        Assert.That(copied, Is.Not.Null);
        Assert.That(copied, Does.Contain("=== 下载失败报告（共 3 项）==="));
        Assert.That(copied, Does.Contain("模组: MechJeb2"));
        Assert.That(copied, Does.Contain("文件: MechJeb2-2.14.3.0.zip"));
        Assert.That(copied, Does.Contain("分类: 网络超时"));
        Assert.That(copied, Does.Contain("建议: 检查网络连接后重试"));
        Assert.That(copied, Does.Contain("模组: ModuleManager"));
        Assert.That(copied, Does.Contain("模组: Scatterer"));
        Assert.That(vm.CopyStatus, Is.EqualTo("已复制全部错误信息到剪贴板"));
        Assert.That(vm.HasCopyStatus, Is.True);
        Assert.That(vm.IsClosed, Is.False);
    }

    [Test]
    public async Task CopyErrorsCommand_WithoutChannel_ShowsNoChannelWarning()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors, copyTextAsync: null);

        await vm.CopyErrorsCommand.ExecuteAsync(null);

        Assert.That(vm.CopyStatus, Does.Contain("没有剪贴板通道"));
        Assert.That(vm.HasCopyStatus, Is.True);
    }

    [Test]
    public async Task CopyErrorsCommand_WhenChannelThrows_CatchesAndShowsErrorMessage()
    {
        var errors = CreateSampleErrors();
        var vm = new DownloadErrorsDialogViewModel(errors, _ => throw new IOException("剪贴板被占用"));

        await vm.CopyErrorsCommand.ExecuteAsync(null);

        Assert.That(vm.CopyStatus, Does.Contain("复制失败"));
        Assert.That(vm.CopyStatus, Does.Contain("剪贴板被占用"));
        Assert.That(vm.HasCopyStatus, Is.True);
    }

    [Test]
    public void Constructor_WithEmptyList_InitializesSafely()
    {
        var vm = new DownloadErrorsDialogViewModel(Array.Empty<DownloadErrorItemInfo>());

        Assert.That(vm.Items, Is.Empty);
        Assert.That(vm.HasItems, Is.False);
        Assert.That(vm.TotalCount, Is.EqualTo(0));
        Assert.That(vm.RetryCount, Is.EqualTo(0));
        Assert.That(vm.SkipCount, Is.EqualTo(0));
        Assert.That(vm.FormattedErrorText, Does.Contain("共 0 项"));
    }
}
