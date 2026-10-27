using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ModPacks;
using CkanUI.TestSupport;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="ModPackEditorStore"/> 的离线用例（06 篇 UI-24、01 篇 F-46）：
/// 写盘字段名必须是上游 <c>CkanModule</c> 的形状、往返保真、<c>any_of</c> / 多选 / 精确版本三种关系写法、
/// 校验不通过时一个字节都不写、<c>.tmp + Move</c> 不留下半成品。
/// 全部写进 <see cref="TempWorkspace"/>，不联网、不碰真实 CKAN 注册表。
/// </summary>
[TestFixture]
public sealed class ModPackEditorStoreTests
{
    private TempWorkspace _workspace = null!;
    private ModPackEditorStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("modpack-editor-store");
        _store = new ModPackEditorStore();
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private string TargetPath(string name = "my-modpack.ckan") => _workspace.Combine(name);

    private ModPackEditorDocument Minimal(string? path = null) => new()
    {
        FilePath = path ?? TargetPath(),
        Identifier = "my-modpack",
        Name = "我的整合包",
        Abstract = "一句话摘要",
        Version = "1.0.0",
        Licenses = new[] { "MIT" },
        Authors = new[] { "Tester" },
        Kind = "metapackage",
        ReleaseStatus = "stable",
    };

    // ---------------- 写盘 ----------------

    [Test]
    public void Save_MinimalDocument_WritesFileAndReturnsAbsolutePath()
    {
        var result = _store.Save(Minimal());

        Assert.That(result.IsSuccess, Is.True, result.Message + " / " + result.Detail);
        Assert.That(result.Value, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(result.Value), Is.True);
        Assert.That(result.Value, Is.EqualTo(Path.GetFullPath(TargetPath())));
    }

    [Test]
    public void Save_UsesUpstreamFieldNames()
    {
        var result = _store.Save(Minimal());
        var json = File.ReadAllText(result.Value!);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.That(root.TryGetProperty("spec_version", out _), Is.True);
        Assert.That(root.TryGetProperty("identifier", out _), Is.True);
        Assert.That(root.TryGetProperty("name", out _), Is.True);
        Assert.That(root.TryGetProperty("abstract", out _), Is.True);
        Assert.That(root.TryGetProperty("version", out _), Is.True);
        Assert.That(root.TryGetProperty("license", out _), Is.True);
        Assert.That(root.TryGetProperty("kind", out _), Is.True);

        // 没有关系行时上游靠 NullValueHandling.Ignore 跳过这五个键，编辑器不擅自补空数组。
        Assert.That(root.TryGetProperty("depends", out _), Is.False);
        Assert.That(root.TryGetProperty("recommends", out _), Is.False);
    }

    [Test]
    public void Save_ThenLoad_RoundTripsScalarFields()
    {
        var source = Minimal() with
        {
            Description = "详细描述在这里。",
            // ksp_version 与 ksp_version_(min|max) 在 CKAN 里互斥（见下面的冲突用例），这里只走范围写法。
            KspVersionMin = "1.12.0",
            KspVersionMax = "1.12.9",
        };

        var saved = _store.Save(source);
        Assert.That(saved.IsSuccess, Is.True, saved.Message);

        var loaded = _store.Load(saved.Value!);
        Assert.That(loaded.IsSuccess, Is.True, loaded.Message);
        var document = loaded.Value!;

        Assert.That(document.FilePath, Is.EqualTo(Path.GetFullPath(saved.Value!)));
        Assert.That(document.Identifier, Is.EqualTo(source.Identifier));
        Assert.That(document.Name, Is.EqualTo(source.Name));
        Assert.That(document.Abstract, Is.EqualTo(source.Abstract));
        Assert.That(document.Description, Is.EqualTo(source.Description));
        Assert.That(document.Authors, Is.EqualTo(source.Authors));
        Assert.That(document.Version, Is.EqualTo(source.Version));
        Assert.That(document.Licenses, Is.EqualTo(source.Licenses));
        Assert.That(document.ReleaseStatus, Is.EqualTo("stable"));
        Assert.That(document.Kind, Is.EqualTo("metapackage"));
        Assert.That(document.KspVersion, Is.Empty);
        Assert.That(document.KspVersionMin, Is.EqualTo("1.12.0"));
        Assert.That(document.KspVersionMax, Is.EqualTo("1.12.9"));
        // metapackage + stable + MIT 的最小规范版本就是 v1.6（与 RegistryManager.GenerateModpack 同源推导）。
        Assert.That(document.SpecVersion, Is.EqualTo("v1.6"));
    }

    [Test]
    public void Save_ThenLoad_RoundTripsExactKspVersion()
    {
        var source = Minimal() with { KspVersion = "1.12.5" };

        var saved = _store.Save(source);
        Assert.That(saved.IsSuccess, Is.True, saved.Message);

        var loaded = _store.Load(saved.Value!);
        Assert.That(loaded.IsSuccess, Is.True, loaded.Message);
        Assert.That(loaded.Value!.KspVersion, Is.EqualTo("1.12.5"));
        Assert.That(loaded.Value!.KspVersionMin, Is.Empty);
        Assert.That(loaded.Value!.KspVersionMax, Is.Empty);
    }

    [Test]
    public void Save_KspVersionTogetherWithRange_RefusesBecauseCkanWouldRejectIt()
    {
        // 上游 CkanModule 的 CheckHealth 对"精确版本 + 上下限"抛 InvalidModuleAttributesKraken，
        // 这种文件写得出去却读不回来，所以校验阶段就要拦。
        var source = Minimal() with
        {
            KspVersion = "1.12.5",
            KspVersionMin = "1.12.0",
        };

        var result = _store.Save(source);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("校验未通过"));
        Assert.That(result.Detail, Does.Contain("ksp_version"));
        Assert.That(File.Exists(TargetPath()), Is.False);
    }

    [Test]
    public void Save_ThenLoad_RoundTripsRelations()
    {
        var source = Minimal() with
        {
            Relations = new[]
            {
                new ModPackRelation
                {
                    RelationType = ModPackRelationTypes.Depends,
                    Mode = ModPackRelationModes.Single,
                    Name = "modulemanager",
                    MinVersion = "5.0",
                },
                new ModPackRelation
                {
                    RelationType = ModPackRelationTypes.Recommends,
                    Mode = ModPackRelationModes.AnyOf,
                    Candidates = new[] { "scatterer", "ksp-community-fixes" },
                },
                new ModPackRelation
                {
                    RelationType = ModPackRelationTypes.Conflicts,
                    Mode = ModPackRelationModes.Single,
                    Name = "old-toolbar",
                },
            },
        };

        var saved = _store.Save(source);
        Assert.That(saved.IsSuccess, Is.True, saved.Message);

        var loaded = _store.Load(saved.Value!);
        Assert.That(loaded.IsSuccess, Is.True, loaded.Message);
        var relations = loaded.Value!.Relations;

        Assert.That(relations.Count, Is.EqualTo(3));

        Assert.That(relations[0].RelationType, Is.EqualTo(ModPackRelationTypes.Depends));
        Assert.That(relations[0].Mode, Is.EqualTo(ModPackRelationModes.Single));
        Assert.That(relations[0].Name, Is.EqualTo("modulemanager"));
        Assert.That(relations[0].MinVersion, Is.EqualTo("5.0"));
        Assert.That(relations[0].MaxVersion, Is.Empty);

        Assert.That(relations[1].RelationType, Is.EqualTo(ModPackRelationTypes.Recommends));
        Assert.That(relations[1].Mode, Is.EqualTo(ModPackRelationModes.AnyOf));
        Assert.That(relations[1].Candidates, Is.EqualTo(new[] { "scatterer", "ksp-community-fixes" }));

        Assert.That(relations[2].RelationType, Is.EqualTo(ModPackRelationTypes.Conflicts));
        Assert.That(relations[2].Name, Is.EqualTo("old-toolbar"));
    }

    [Test]
    public void Save_AnyOfRelation_WritesUpstreamAnyOfArray()
    {
        var source = Minimal() with
        {
            Relations = new[]
            {
                new ModPackRelation
                {
                    RelationType = ModPackRelationTypes.Depends,
                    Mode = ModPackRelationModes.AnyOf,
                    Candidates = new[] { "scatterer", "ksp-community-fixes" },
                },
            },
        };

        var result = _store.Save(source);
        Assert.That(result.IsSuccess, Is.True, result.Message);

        using var document = JsonDocument.Parse(File.ReadAllText(result.Value!));
        var depends = document.RootElement.GetProperty("depends");
        Assert.That(depends.GetArrayLength(), Is.EqualTo(1));

        var anyOf = depends[0].GetProperty("any_of");
        Assert.That(anyOf.GetArrayLength(), Is.EqualTo(2));
        Assert.That(anyOf[0].GetProperty("name").GetString(), Is.EqualTo("scatterer"));
        Assert.That(anyOf[1].GetProperty("name").GetString(), Is.EqualTo("ksp-community-fixes"));
    }

    [Test]
    public void Save_MultiMode_ExpandsIntoSeparateNameRows()
    {
        var source = Minimal() with
        {
            Relations = new[]
            {
                new ModPackRelation
                {
                    RelationType = ModPackRelationTypes.Suggests,
                    Mode = ModPackRelationModes.Multi,
                    Candidates = new[] { "scatterer", "ksp-community-fixes" },
                },
            },
        };

        var result = _store.Save(source);
        Assert.That(result.IsSuccess, Is.True, result.Message);

        using var document = JsonDocument.Parse(File.ReadAllText(result.Value!));
        var suggests = document.RootElement.GetProperty("suggests");
        Assert.That(suggests.GetArrayLength(), Is.EqualTo(2));
        // 多选只是界面便利：写盘后展开成上游认识的单条 name 行，磁盘上没有自定义字段。
        Assert.That(suggests[0].GetProperty("name").GetString(), Is.EqualTo("scatterer"));
        Assert.That(suggests[1].GetProperty("name").GetString(), Is.EqualTo("ksp-community-fixes"));
        Assert.That(suggests[0].TryGetProperty("mode", out _), Is.False);
    }

    [Test]
    public void Save_EqualMinAndMax_FoldsIntoUpstreamVersionField()
    {
        var source = Minimal() with
        {
            Relations = new[]
            {
                new ModPackRelation
                {
                    RelationType = ModPackRelationTypes.Depends,
                    Mode = ModPackRelationModes.Single,
                    Name = "modulemanager",
                    MinVersion = "5.0",
                    MaxVersion = "5.0",
                },
            },
        };

        var result = _store.Save(source);
        Assert.That(result.IsSuccess, Is.True, result.Message);

        using var document = JsonDocument.Parse(File.ReadAllText(result.Value!));
        var row = document.RootElement.GetProperty("depends")[0];
        Assert.That(row.GetProperty("version").GetString(), Is.EqualTo("5.0"));
        Assert.That(row.TryGetProperty("min_version", out _), Is.False);
        Assert.That(row.TryGetProperty("max_version", out _), Is.False);
    }

    [Test]
    public void Save_InvalidDocument_RefusesAndWritesNothing()
    {
        var path = TargetPath("broken.ckan");
        var broken = Minimal(path) with { Identifier = "Not-Lowercase" };

        var result = _store.Save(broken);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("校验未通过"));
        Assert.That(result.Detail, Does.Contain("必须全部小写"));
        Assert.That(File.Exists(path), Is.False);
        Assert.That(File.Exists(path + ".tmp"), Is.False);
    }

    [Test]
    public void Save_MissingExtension_AppendsCkanSuffix()
    {
        var path = _workspace.Combine("no-suffix");

        var result = _store.Save(Minimal(path));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Does.EndWith(".ckan"));
        Assert.That(File.Exists(result.Value), Is.True);
    }

    [Test]
    public void Save_OverwritesExistingFile_LeavesNoTempFile()
    {
        var path = TargetPath();
        Assert.That(_store.Save(Minimal(path)).IsSuccess, Is.True);
        var firstLength = new FileInfo(path).Length;

        var updated = Minimal(path) with { Name = "改过的名字", Abstract = "更长的摘要，用来确认文件确实被新内容覆盖掉了。" };
        var result = _store.Save(updated);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(new FileInfo(path).Length, Is.Not.EqualTo(firstLength));
        Assert.That(File.Exists(path + ".tmp"), Is.False);
        Assert.That(Directory.GetFiles(_workspace.Path, "*.tmp"), Is.Empty);
        Assert.That(_store.Load(path).Value!.Name, Is.EqualTo("改过的名字"));
    }

    // ---------------- 读盘 ----------------

    [Test]
    public void Load_MissingFile_Fails()
    {
        var result = _store.Load(TargetPath("nothing-here.ckan"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("找不到文件"));
    }

    [Test]
    public void Load_EmptyPath_Fails()
    {
        var result = _store.Load("   ");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("保存路径").Or.Contain("路径"));
    }

    [Test]
    public void Load_BrokenJson_FailsWithoutThrowing()
    {
        var path = TargetPath("broken.ckan");
        File.WriteAllText(path, "{ 这不是 JSON");

        var result = _store.Load(path);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不是合法的 CKAN 元数据包"));
    }

    [Test]
    public void Load_ForeignCkanFile_ProjectsUpstreamFields()
    {
        var path = TargetPath("foreign.ckan");
        File.WriteAllText(path, """
        {
          "spec_version": "v1.16",
          "identifier": "foreign-pack",
          "name": "外部整合包",
          "abstract": "别人写的 .ckan",
          "author": "Someone",
          "version": "2.3",
          "license": "GPL-3.0",
          "release_status": "testing",
          "kind": "metapackage",
          "ksp_version": "1.12.5",
          "depends": [
            { "name": "modulemanager", "min_version": "5.0" }
          ]
        }
        """);

        var result = _store.Load(path);

        Assert.That(result.IsSuccess, Is.True, result.Message + " / " + result.Detail);
        var document = result.Value!;
        Assert.That(document.Identifier, Is.EqualTo("foreign-pack"));
        Assert.That(document.Name, Is.EqualTo("外部整合包"));
        Assert.That(document.Abstract, Is.EqualTo("别人写的 .ckan"));
        Assert.That(document.Authors, Is.EqualTo(new[] { "Someone" }));
        Assert.That(document.Version, Is.EqualTo("2.3"));
        Assert.That(document.Licenses, Is.EqualTo(new[] { "GPL-3.0" }));
        Assert.That(document.ReleaseStatus, Is.EqualTo("testing"));
        Assert.That(document.KspVersion, Is.EqualTo("1.12.5"));
        Assert.That(document.Relations.Count, Is.EqualTo(1));
        Assert.That(document.Relations[0].Name, Is.EqualTo("modulemanager"));
        Assert.That(document.Relations[0].MinVersion, Is.EqualTo("5.0"));
    }

    [Test]
    public void Load_ExactVersionRelation_FillsBothBounds()
    {
        var path = TargetPath("exact.ckan");
        File.WriteAllText(path, """
        {
          "spec_version": "v1.16",
          "identifier": "exact-pack",
          "name": "精确版本",
          "abstract": "关系行写了 version",
          "author": "Someone",
          "version": "1.0",
          "license": "MIT",
          "kind": "metapackage",
          "depends": [ { "name": "modulemanager", "version": "5.0" } ]
        }
        """);

        var result = _store.Load(path);

        Assert.That(result.IsSuccess, Is.True, result.Message + " / " + result.Detail);
        var relation = result.Value!.Relations[0];
        Assert.That(relation.MinVersion, Is.EqualTo("5.0"));
        Assert.That(relation.MaxVersion, Is.EqualTo("5.0"));
    }

    [Test]
    public void Save_DevelopmentStatus_DerivesHigherSpecVersionThanStable()
    {
        var path = TargetPath("dev.ckan");
        var result = _store.Save(Minimal(path) with { ReleaseStatus = "development" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var loaded = _store.Load(result.Value!);
        Assert.That(loaded.IsSuccess, Is.True, loaded.Message);
        // release_status 不是 stable 时上游要求 spec_version >= v1.36。
        Assert.That(loaded.Value!.SpecVersion, Is.EqualTo("v1.36"));
    }

    [Test]
    public void BuildModule_MapsKindsAndRelationsWithoutTouchingDisk()
    {
        var module = ModPackEditorStore.BuildModule(Minimal() with
        {
            Relations = new[]
            {
                new ModPackRelation
                {
                    RelationType = ModPackRelationTypes.Supports,
                    Mode = ModPackRelationModes.Single,
                    Name = "modulemanager",
                },
            },
        });

        Assert.That(module.identifier, Is.EqualTo("my-modpack"));
        Assert.That(module.kind, Is.EqualTo(CKAN.ModuleKind.metapackage));
        Assert.That(module.supports, Is.Not.Null);
        Assert.That(module.supports!.Count, Is.EqualTo(1));
        Assert.That(module.depends, Is.Null);
    }

    [Test]
    public void NormalizeTargetPath_AppendsSuffixAndMakesAbsolute()
    {
        var path = ModPackEditorStore.NormalizeTargetPath(TargetPath("plain"));

        Assert.That(path, Does.EndWith(".ckan"));
        Assert.That(Path.IsPathRooted(path), Is.True);

        var already = ModPackEditorStore.NormalizeTargetPath(TargetPath("plain.ckan"));
        Assert.That(already, Does.EndWith("plain.ckan"));
    }
}
