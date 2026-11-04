using System;
using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Home;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>主页数据部件的纯格式化：有数据/没数据/千字节与排序都要说人话，且不许用 0 冒充未知。</summary>
[TestFixture]
public sealed class HomeWidgetFeedTests
{
    [Test]
    public void ModUpdates_NoSnapshot_ExplainsHowToLoadIt()
    {
        var feed = HomeWidgetFeeds.ModUpdates(null);

        Assert.That(feed.Metrics, Is.Null, "没数据时不要画 0 个已安装。");
        Assert.That(feed.Footer, Does.Contain("仓库还没加载"));
    }

    [Test]
    public void ModUpdates_CountsInstalledAndOrdersCompatibleFirst()
    {
        var snapshot = new RepositorySnapshot(
            new[]
            {
                Mod("Incompatible", "1.0", "2.0", downloads: 999, installed: true, update: true, compatible: false),
                Mod("Compatible", "1.0", "2.0", downloads: 10, installed: true, update: true, compatible: true),
                Mod("Current", "1.0", "1.0", downloads: 50, installed: true, update: false, compatible: true),
                Mod("NotInstalled", null, "1.0", downloads: 1, installed: false, update: false, compatible: true),
            },
            new[] { "repo" },
            new DateTime(2026, 11, 4, 7, 10, 0, DateTimeKind.Utc),
            IsFromCache: false);

        var feed = HomeWidgetFeeds.ModUpdates(snapshot);

        Assert.That(feed.Metrics!.Select(metric => metric.Value), Is.EqualTo(new[] { "3", "2", "1" }));
        Assert.That(feed.Lines!.Select(line => line.Text), Is.EqualTo(new[] { "Compatible", "Incompatible" }));
        Assert.That(feed.Subtitle, Does.Contain("2 个模组可以更新"));
        Assert.That(feed.Lines![1].Badge, Is.EqualTo("不兼容"));
    }

    [Test]
    public void InstallHistory_TakesFiveAndCountsFailuresSeparately()
    {
        var entries = Enumerable.Range(0, 6)
            .Select(index => new InstallHistoryEntry(
                DateTimeOffset.UtcNow.AddMinutes(-index),
                "Main",
                InstallHistoryOperation.Upgrade,
                index == 0 ? InstallHistoryOutcome.Succeeded : InstallHistoryOutcome.Failed,
                new[] { new InstallHistoryChange("mod", ChangeAction.Upgrade, "1.0", "2.0") }))
            .ToArray();

        var feed = HomeWidgetFeeds.InstallHistory(entries);

        Assert.That(feed.Lines, Has.Count.EqualTo(5), "主页只放最近 5 条。");
        Assert.That(feed.Metrics!.Select(metric => metric.Value), Is.EqualTo(new[] { "6", "1", "5" }));
        Assert.That(feed.Lines![1].Badge, Is.EqualTo("失败"));
        Assert.That(feed.Lines![0].Detail, Does.Contain("Main"));
    }

    [Test]
    public void Cache_ShowsLimitAndRemaining_OrSaysUnlimited()
    {
        var limited = HomeWidgetFeeds.Cache(new CacheSnapshot(12, 1536, 4096, @"D:\cache"));
        var unlimited = HomeWidgetFeeds.Cache(new CacheSnapshot(3, 1024, null));

        Assert.That(limited.Metrics!.Select(metric => metric.Value), Is.EqualTo(new[] { "12", "1.5 KB", "4 KB" }));
        Assert.That(limited.Subtitle, Does.Contain("37.5%").And.Contain("2.5 KB"));
        Assert.That(limited.Footer, Does.Contain(@"D:\cache"));

        Assert.That(unlimited.Metrics![2].Value, Is.EqualTo("不限"));
        Assert.That(unlimited.Subtitle, Does.Contain("没有设置容量上限"));
        Assert.That(unlimited.Footer, Is.Null);
    }

    [Test]
    public void Saves_CountsBackupsAndSortsByWriteTime()
    {
        var older = Save("old", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), backup: false);
        var newer = Save("new", new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), backup: true);

        var feed = HomeWidgetFeeds.Saves(new[] { older, newer });

        Assert.That(feed.Lines!.Select(line => line.Text), Is.EqualTo(new[] { "new", "old" }));
        Assert.That(feed.Lines![0].Badge, Is.EqualTo("备份"));
        Assert.That(feed.Metrics!.Select(metric => metric.Value), Is.EqualTo(new[] { "1", "1", "0" }));
    }

    [Test]
    public void Operations_PutsRunningBeforePending_AndLabelsState()
    {
        var operations = new[]
        {
            new OperationProgress("2", "install", "排队中", null, 0, false, OperationState.Pending),
            new OperationProgress("1", "download", "正在下载", "已下载 42%", 42, true, OperationState.Running),
            new OperationProgress("3", "install", "已完成", null, 100, false, OperationState.Succeeded),
        };

        var feed = HomeWidgetFeeds.Operations(operations);

        Assert.That(feed.Lines!.Select(line => line.Text), Is.EqualTo(new[] { "正在下载", "排队中", "已完成" }));
        Assert.That(feed.Lines![0].Badge, Is.EqualTo("42%"));
        Assert.That(feed.Lines![1].Badge, Is.EqualTo("排队"));
        Assert.That(feed.Metrics!.Select(metric => metric.Value), Is.EqualTo(new[] { "1", "1", "3" }));
    }

    [Test]
    public void EmptyCollections_ExplainWhatWillAppear_InsteadOfShowingZeros()
    {
        Assert.That(HomeWidgetFeeds.InstallHistory(Array.Empty<InstallHistoryEntry>()).Footer, Does.Contain("还没有"));
        Assert.That(HomeWidgetFeeds.Saves(Array.Empty<SaveGameInfo>()).Footer, Does.Contain("还没有存档"));
        Assert.That(HomeWidgetFeeds.Operations(Array.Empty<OperationProgress>()).Footer, Does.Contain("没有后台任务"));
    }

    private static ModRowData Mod(
        string identifier,
        string? installedVersion,
        string? latestVersion,
        long downloads,
        bool installed,
        bool update,
        bool compatible)
        => new(
            Identifier: identifier,
            Name: identifier,
            Author: null,
            InstalledVersion: installedVersion,
            LatestVersion: latestVersion,
            DownloadCount: downloads,
            Tags: Array.Empty<string>(),
            SizeBytes: null,
            IsInstalled: installed,
            HasUpdate: update,
            IsCompatible: compatible,
            IsAutoInstalled: false,
            IsSelected: false,
            IsImplicit: false);

    private static SaveGameInfo Save(string slot, DateTimeOffset written, bool backup)
        => new(
            SlotName: slot,
            FilePath: slot + ".sfs",
            SizeBytes: 1024,
            LastWriteTime: written,
            Title: slot,
            Mode: "CAREER",
            GameVersion: "1.12.5",
            Scene: "FLIGHT",
            UniversalTime: "1",
            VesselCount: 2,
            Funds: "1",
            SnapshotCount: 0,
            IsBackup: backup,
            HasLoadMeta: true);
}
