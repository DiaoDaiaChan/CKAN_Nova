using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CKAN;
using CkanUI.TestSupport;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ChangeSets;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// INT-07 <see cref="ChangeSetService"/> 的离线测试：用 fake 的 <see cref="IChangeSetPlanner"/>
/// 覆盖排序、体积汇总、无实例语义与异常上报，不依赖真实仓库索引。
/// </summary>
[TestFixture]
public sealed class ChangeSetServiceTests
{
    private const string NoInstanceMessage = "当前没有选中的游戏实例";

    [Test]
    public async Task BuildAsync_EmptyRequests_ReturnsEmptyPreviewWithoutFailure()
    {
        var planner = new ChangeSetFakePlanner { HasActiveInstance = false };
        var service = new ChangeSetService(planner);

        var result = await service.BuildAsync(Array.Empty<ChangeRequest>()).ConfigureAwait(false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.IsEmpty, Is.True);
        Assert.That(result.Value!.EstimatedDownloadBytes, Is.Zero);
        Assert.That(planner.PlanCalls, Is.Zero, "空请求不应该触达 Core");
    }

    [Test]
    public async Task BuildAsync_NoActiveInstance_ReturnsRetryableFailure()
    {
        var planner = new ChangeSetFakePlanner { HasActiveInstance = false };
        var service = new ChangeSetService(planner);

        var result = await service.BuildAsync(new[] { new ChangeRequest("alpha", ChangeAction.Install) })
                                .ConfigureAwait(false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Message, Is.EqualTo(NoInstanceMessage));
        Assert.That(planner.PlanCalls, Is.Zero);
    }

    [Test]
    public void BuildLocal_NoActiveInstance_ReturnsEmptyPreview()
    {
        var planner = new ChangeSetFakePlanner { HasActiveInstance = false };
        var service = new ChangeSetService(planner);

        var result = service.BuildLocal(new[] { new ChangeRequest("alpha", ChangeAction.Install) });

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsEmpty, Is.True);
    }

    [Test]
    public void BuildLocal_InstallRequest_MapsToInstallItem()
    {
        var target = CreateModule("alpha", "2.0", 2048);
        var planner = new ChangeSetFakePlanner
        {
            Resolution = Resolution(new[]
            {
                new ResolvedChangeRequest("alpha", ChangeAction.Install, ChangeOrigin.UserRequested,
                                          null, target, 2048),
            }),
        };
        var service = new ChangeSetService(planner);

        var result = service.BuildLocal(new[] { new ChangeRequest("alpha", ChangeAction.Install) });

        Assert.That(result.IsSuccess, Is.True);
        var item = result.Value!.Items.Single();
        Assert.That(item.Identifier, Is.EqualTo("alpha"));
        Assert.That(item.Name, Is.EqualTo("alpha 模组"));
        Assert.That(item.Action, Is.EqualTo(ChangeAction.Install));
        Assert.That(item.FromVersion, Is.Null);
        Assert.That(item.ToVersion, Is.EqualTo(target.version.ToString()));
        Assert.That(item.Origin, Is.EqualTo(ChangeOrigin.UserRequested));
        Assert.That(item.DownloadBytes, Is.EqualTo(2048L));
        Assert.That(result.Value!.EstimatedDownloadBytes, Is.EqualTo(2048L));
    }

    [Test]
    public void BuildLocal_UpgradeRequest_SetsFromAndToVersions()
    {
        var installed = CreateModule("beta", "1.0");
        var target = CreateModule("beta", "2.0", 512);
        var planner = new ChangeSetFakePlanner
        {
            Resolution = Resolution(new[]
            {
                new ResolvedChangeRequest("beta", ChangeAction.Upgrade, ChangeOrigin.UserRequested,
                                          installed, target, 512),
            }),
        };
        var service = new ChangeSetService(planner);

        var result = service.BuildLocal(new[] { new ChangeRequest("beta", ChangeAction.Upgrade) });

        var item = result.Value!.Items.Single();
        Assert.That(item.Action, Is.EqualTo(ChangeAction.Upgrade));
        Assert.That(item.FromVersion, Is.EqualTo(installed.version.ToString()));
        Assert.That(item.ToVersion, Is.EqualTo(target.version.ToString()));
        Assert.That(item.DownloadBytes, Is.EqualTo(512L));
    }

    [Test]
    public void BuildLocal_RemoveRequest_HasNoTargetVersionAndNoDownloadBytes()
    {
        var installed = CreateModule("gamma", "1.5");
        var planner = new ChangeSetFakePlanner
        {
            Resolution = Resolution(new[]
            {
                new ResolvedChangeRequest("gamma", ChangeAction.Remove, ChangeOrigin.UserRequested,
                                          installed, null, 0),
            }),
        };
        var service = new ChangeSetService(planner);

        var result = service.BuildLocal(new[] { new ChangeRequest("gamma", ChangeAction.Remove) });

        var item = result.Value!.Items.Single();
        Assert.That(item.Action, Is.EqualTo(ChangeAction.Remove));
        Assert.That(item.ToVersion, Is.Null);
        Assert.That(item.DownloadBytes, Is.Null);
        Assert.That(result.Value!.EstimatedDownloadBytes, Is.Zero);
    }

    [Test]
    public async Task BuildAsync_ConflictsPresent_SetsHasConflicts()
    {
        var target = CreateModule("alpha", "1.0");
        var planner = new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                new[]
                {
                    new ResolvedChangeRequest("alpha", ChangeAction.Install, ChangeOrigin.UserRequested,
                                              null, target, 128),
                },
                Array.Empty<ResolvedModule>(),
                new[] { "alpha 1.0 与 beta 2.0 冲突" },
                Array.Empty<string>()),
        };
        var service = new ChangeSetService(planner);

        var result = await service.BuildAsync(new[] { new ChangeRequest("alpha", ChangeAction.Install) })
                                .ConfigureAwait(false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.HasConflicts, Is.True);
        Assert.That(result.Value!.Conflicts, Is.Not.Empty);
        Assert.That(result.Value!.Conflicts.Single(), Does.Contain("alpha"));
    }

    [Test]
    public async Task BuildAsync_MixedOrigins_ItemsSortedByOriginThenIdentifier()
    {
        var service = new ChangeSetService(new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                new[]
                {
                    new ResolvedChangeRequest("zzz", ChangeAction.Install, ChangeOrigin.UserRequested,
                                              null, CreateModule("zzz", "1.0"), 1),
                    new ResolvedChangeRequest("aaa", ChangeAction.Upgrade, ChangeOrigin.UserRequested,
                                              CreateModule("aaa", "1.0"), CreateModule("aaa", "2.0"), 2),
                },
                new[]
                {
                    new ResolvedModule(CreateModule("mmm", "1.0"), null, ChangeOrigin.AutoInstalled, 4),
                    new ResolvedModule(CreateModule("ccc", "1.0"), null, ChangeOrigin.Dependency, 8),
                },
                Array.Empty<string>(),
                Array.Empty<string>()),
        });

        var result = await service.BuildAsync(new[]
        {
            new ChangeRequest("aaa", ChangeAction.Upgrade),
            new ChangeRequest("zzz", ChangeAction.Install),
        }).ConfigureAwait(false);

        var identifiers = result.Value!.Items.Select(i => i.Identifier).ToArray();
        Assert.That(identifiers, Is.EqualTo(new[] { "aaa", "zzz", "ccc", "mmm" }),
                    "先按来源优先级（UserRequested < Dependency < Recommendation < AutoInstalled），再按标识");
        Assert.That(result.Value!.EstimatedDownloadBytes, Is.EqualTo(15L));
    }

    [Test]
    public async Task BuildAsync_SameInputTwice_ProducesIdenticalOrder()
    {
        var planner = new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                new[]
                {
                    new ResolvedChangeRequest("delta", ChangeAction.Install, ChangeOrigin.UserRequested,
                                              null, CreateModule("delta", "3.0"), 1),
                    new ResolvedChangeRequest("bravo", ChangeAction.Install, ChangeOrigin.UserRequested,
                                              null, CreateModule("bravo", "1.0"), 1),
                },
                new[] { new ResolvedModule(CreateModule("echo", "1.0"), null, ChangeOrigin.Dependency, 1) },
                Array.Empty<string>(),
                Array.Empty<string>()),
        };
        var service = new ChangeSetService(planner);
        var requests = new[]
        {
            new ChangeRequest("delta", ChangeAction.Install),
            new ChangeRequest("bravo", ChangeAction.Install),
        };

        var first = await service.BuildAsync(requests).ConfigureAwait(false);
        var second = await service.BuildAsync(requests).ConfigureAwait(false);

        Assert.That(first.Value!.Items.Select(i => i.Identifier),
                    Is.EqualTo(second.Value!.Items.Select(i => i.Identifier)));
        Assert.That(first.Value!.Items.Select(i => i.Identifier),
                    Is.EqualTo(new[] { "bravo", "delta", "echo" }));
    }

    [Test]
    public async Task BuildAsync_DuplicateIdentifier_KeepsHighestPriorityOnce()
    {
        var target = CreateModule("alpha", "1.0", 4096);
        var service = new ChangeSetService(new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                new[]
                {
                    new ResolvedChangeRequest("alpha", ChangeAction.Install, ChangeOrigin.UserRequested,
                                              null, target, 4096),
                },
                new[] { new ResolvedModule(target, null, ChangeOrigin.Dependency, 4096) },
                Array.Empty<string>(),
                Array.Empty<string>()),
        });

        var result = await service.BuildAsync(new[] { new ChangeRequest("alpha", ChangeAction.Install) })
                                .ConfigureAwait(false);

        var item = result.Value!.Items.Single();
        Assert.That(item.Origin, Is.EqualTo(ChangeOrigin.UserRequested));
        Assert.That(result.Value!.EstimatedDownloadBytes, Is.EqualTo(4096L), "同一模组只能计一次下载");
    }

    [Test]
    public async Task BuildAsync_ExtraModules_DeriveActionFromVersions()
    {
        var service = new ChangeSetService(new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                Array.Empty<ResolvedChangeRequest>(),
                new[]
                {
                    new ResolvedModule(CreateModule("fresh", "1.0"), null, ChangeOrigin.Dependency, 1),
                    new ResolvedModule(CreateModule("upper", "2.0"),
                                       CreateModule("upper", "1.0"), ChangeOrigin.Dependency, 1),
                    new ResolvedModule(CreateModule("lower", "1.0"),
                                       CreateModule("lower", "2.0"), ChangeOrigin.AutoInstalled, 1),
                },
                Array.Empty<string>(),
                Array.Empty<string>()),
        });

        var result = await service.BuildAsync(new[] { new ChangeRequest("fresh", ChangeAction.Install) })
                                .ConfigureAwait(false);

        var actions = result.Value!.Items.ToDictionary(i => i.Identifier, i => i.Action);
        Assert.That(actions["fresh"], Is.EqualTo(ChangeAction.Install));
        Assert.That(actions["upper"], Is.EqualTo(ChangeAction.Upgrade));
        Assert.That(actions["lower"], Is.EqualTo(ChangeAction.Downgrade));
    }

    [Test]
    public async Task BuildAsync_WarningsFromPlanner_ArePreserved()
    {
        var service = new ChangeSetService(new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                Array.Empty<ResolvedChangeRequest>(),
                Array.Empty<ResolvedModule>(),
                Array.Empty<string>(),
                new[] { "仓库索引里没有「ghost」的可安装版本，已忽略这次请求。" }),
        });

        var result = await service.BuildAsync(new[] { new ChangeRequest("ghost", ChangeAction.Install) })
                                .ConfigureAwait(false);

        Assert.That(result.Value!.Warnings.Single(), Does.Contain("ghost"));
    }

    [Test]
    public async Task BuildAsync_PlannerThrows_ReturnsFailureAndReports()
    {
        var planner = new ChangeSetFakePlanner { Failure = new InvalidOperationException("核心炸了") };
        var errors = new ChangeSetFakeErrorReporter();
        var service = new ChangeSetService(planner, errors);

        var result = await service.BuildAsync(new[] { new ChangeRequest("alpha", ChangeAction.Install) })
                                .ConfigureAwait(false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False);
        Assert.That(result.Message, Is.Not.Empty);
        Assert.That(errors.Entries, Has.Count.EqualTo(1));
        Assert.That(errors.Entries[0].Context, Is.EqualTo("ChangeSetService.BuildAsync"));
        Assert.That(errors.Entries[0].Exception, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void BuildLocal_PlannerThrows_ReturnsFailureAndReports()
    {
        var planner = new ChangeSetFakePlanner { Failure = new InvalidOperationException("核心炸了") };
        var errors = new ChangeSetFakeErrorReporter();
        var service = new ChangeSetService(planner, errors);

        var result = service.BuildLocal(new[] { new ChangeRequest("alpha", ChangeAction.Install) });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False);
        Assert.That(errors.Entries, Has.Count.EqualTo(1));
        Assert.That(errors.Entries[0].Context, Is.EqualTo("ChangeSetService.BuildLocal"));
    }

    [Test]
    public async Task BuildAsync_RequestsRecommendations_BuildLocalDoesNot()
    {
        var planner = new ChangeSetFakePlanner();
        var service = new ChangeSetService(planner);
        var requests = new[] { new ChangeRequest("alpha", ChangeAction.Install) };

        await service.BuildAsync(requests).ConfigureAwait(false);
        Assert.That(planner.LastIncludeRecommendations, Is.True, "完整计算要算推荐项");

        service.BuildLocal(requests);
        Assert.That(planner.LastIncludeRecommendations, Is.False, "快速预览不跑推荐项");
    }

    [Test]
    public async Task BuildAsync_StructuredConflicts_KeepConflictsAndEntriesInSync()
    {
        var alpha = CreateModule("alpha", "1.0");
        var beta = CreateModule("beta", "2.0");
        var service = new ChangeSetService(new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                new[]
                {
                    new ResolvedChangeRequest("alpha", ChangeAction.Install, ChangeOrigin.UserRequested,
                                              null, alpha, 1),
                },
                Array.Empty<ResolvedModule>(),
                new[] { "alpha 1.0 与 beta 2.0 冲突", "beta 2.0 与 alpha 1.0 冲突" },
                Array.Empty<string>())
            {
                ConflictEntries = new[]
                {
                    new ConflictEntry("alpha 1.0 与 beta 2.0 冲突", "alpha", "alpha 模组"),
                },
            },
        });

        var result = await service.BuildAsync(new[] { new ChangeRequest("alpha", ChangeAction.Install) })
                                .ConfigureAwait(false);

        var preview = result.Value!;
        Assert.That(preview.Conflicts, Has.Count.EqualTo(preview.ConflictEntries.Count),
                    "Conflicts 与 ConflictEntries 必须一一对应");
        Assert.That(preview.ConflictEntries.Single().ModuleIdentifier, Is.EqualTo("alpha"));
        Assert.That(preview.ConflictEntries.Single().ModuleName, Is.EqualTo("alpha 模组"));
        Assert.That(preview.Conflicts.Single(), Is.EqualTo("alpha 1.0 与 beta 2.0 冲突"));
    }

    [Test]
    public async Task BuildAsync_TextOnlyConflicts_SynthesizesEntriesWithNullIdentifier()
    {
        var service = new ChangeSetService(new ChangeSetFakePlanner
        {
            Resolution = new ChangeResolution(
                Array.Empty<ResolvedChangeRequest>(),
                Array.Empty<ResolvedModule>(),
                new[] { "alpha 1.0 与 beta 2.0 冲突", "alpha 1.0 与 beta 2.0 冲突" },
                Array.Empty<string>()),
        });

        var result = await service.BuildAsync(new[] { new ChangeRequest("alpha", ChangeAction.Install) })
                                .ConfigureAwait(false);

        var preview = result.Value!;
        Assert.That(preview.Conflicts, Has.Count.EqualTo(1), "旧式字符串仍按 Ordinal 去重");
        Assert.That(preview.ConflictEntries, Has.Count.EqualTo(1));
        Assert.That(preview.ConflictEntries.Single().ModuleIdentifier, Is.Null);
        Assert.That(preview.ConflictEntries.Single().Description, Is.EqualTo(preview.Conflicts.Single()));
    }

    private static ChangeResolution Resolution(IReadOnlyList<ResolvedChangeRequest> requests)
        => new ChangeResolution(requests, Array.Empty<ResolvedModule>(),
                                Array.Empty<string>(), Array.Empty<string>());

    private static CkanModule CreateModule(string identifier, string version, long downloadSize = 1024)
        => CkanModule.FromJson($$"""
            {
              "spec_version": 1,
              "identifier": "{{identifier}}",
              "name": "{{identifier}} 模组",
              "version": "{{version}}",
              "download_size": {{downloadSize}},
              "download": "https://example.com/{{identifier}}.zip"
            }
            """);
}

/// <summary>
/// 真实 <see cref="CoreChangeSetPlanner"/> 的最小覆盖：不造游戏实例与仓库索引，
/// 只锁死"无活动实例"这一条路径确实由 Core 规划器返回可重试失败。
/// </summary>
[TestFixture]
public sealed class ChangeSetCorePlannerTests
{
    [Test]
    public void HasActiveInstance_NoConfiguredInstances_IsFalse()
    {
        using var workspace = new TempWorkspace();
        var planner = CreatePlanner(workspace);

        Assert.That(planner.HasActiveInstance, Is.False);
    }

    [Test]
    public void Plan_NoActiveInstance_ReturnsRetryableFailure()
    {
        using var workspace = new TempWorkspace();
        var planner = CreatePlanner(workspace);

        var result = planner.Plan(new[] { new ChangeRequest("alpha", ChangeAction.Install) },
                                  includeRecommendations: true, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Message, Is.EqualTo(CoreChangeSetPlanner.NoInstanceMessage));
    }

    private static CoreChangeSetPlanner CreatePlanner(TempWorkspace workspace)
        => new CoreChangeSetPlanner(
               new GameInstanceManager(new FakeUser(), new FakeConfiguration()),
               new RepositoryDataManager(workspace.Combine("repos")));
}

/// <summary>
/// B0 去重口径：Core 会按 A→B、B→A 各写一条冲突描述，必须按「identifier 排序后的二元组」
/// 折叠成一条，同时把模组标识/名称带出来给界面（B2）。
/// </summary>
[TestFixture]
public sealed class ConflictEntryDedupTests
{
    [Test]
    public void BuildConflictEntries_OppositeDirections_KeepsOne()
    {
        var alpha = CreateModule("alpha", "1.0");
        var beta = CreateModule("beta", "2.0");

        var entries = CoreChangeSetPlanner.BuildConflictEntries(
            new[]
            {
                "alpha 1.0 与 beta 2.0 冲突",
                "beta 2.0 与 alpha 1.0 冲突",
            },
            new[] { alpha, beta });

        Assert.That(entries, Has.Count.EqualTo(1), "正反两条只保留一条");
        Assert.That(entries[0].Description, Is.EqualTo("alpha 1.0 与 beta 2.0 冲突"), "Description 原样保留 Core 本地化串");
        Assert.That(entries[0].ModuleIdentifier, Is.EqualTo("alpha"));
        Assert.That(entries[0].ModuleName, Is.EqualTo("alpha 模组"));
    }

    [Test]
    public void BuildConflictEntries_DistinctPairs_AreNotCollapsed()
    {
        var alpha = CreateModule("alpha", "1.0");
        var beta = CreateModule("beta", "2.0");
        var gamma = CreateModule("gamma", "3.0");

        var entries = CoreChangeSetPlanner.BuildConflictEntries(
            new[]
            {
                "alpha 1.0 与 beta 2.0 冲突",
                "gamma 3.0 与 alpha 1.0 冲突",
            },
            new[] { alpha, beta, gamma });

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries.Select(e => e.ModuleIdentifier), Is.EqualTo(new[] { "alpha", "gamma" }));
    }

    [Test]
    public void BuildConflictEntries_UnknownModules_FallBackToDistinctText()
    {
        var entries = CoreChangeSetPlanner.BuildConflictEntries(
            new[]
            {
                "无法识别的冲突描述",
                "无法识别的冲突描述",
                "另一种无法识别的冲突",
            },
            Array.Empty<CkanModule>());

        Assert.That(entries, Has.Count.EqualTo(2), "识别不到模组时按原字符串去重");
        Assert.That(entries.All(e => e.ModuleIdentifier is null), Is.True);
        Assert.That(entries[0].Description, Is.EqualTo("无法识别的冲突描述"));
    }

    [Test]
    public void BuildConflictEntries_VersionPrefix_DoesNotMatchLongerVersion()
    {
        var alpha = CreateModule("alpha", "1.0");
        var beta = CreateModule("beta", "2.0");

        // "alpha 1.0.1" 不是 "alpha 1.0"，不能把它当成 alpha 的命中。
        var entries = CoreChangeSetPlanner.BuildConflictEntries(
            new[] { "alpha 1.0.1 与 beta 2.0 冲突" },
            new[] { alpha, beta });

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ModuleIdentifier, Is.Null, "只命中一个模组时不算成对，退回无标识兜底");
    }

    private static CkanModule CreateModule(string identifier, string version)
        => CkanModule.FromJson($$"""
            {
              "spec_version": 1,
              "identifier": "{{identifier}}",
              "name": "{{identifier}} 模组",
              "version": "{{version}}",
              "download_size": 1024,
              "download": "https://example.com/{{identifier}}.zip"
            }
            """);
}
