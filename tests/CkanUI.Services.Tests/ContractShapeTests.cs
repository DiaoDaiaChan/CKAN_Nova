using System.Reflection;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 契约形状测试（04 篇 §3 / docs/design/service-contracts.md）：
/// 防止并行开发时有人"顺手"改了公共接口签名而没人发现。
/// </summary>
[TestFixture]
public sealed class ContractShapeTests
{
    [Test]
    public void Dispatcher_has_the_four_authoritative_members()
    {
        var properties = typeof(IDispatcher).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        // 接口上属性的访问器（get_IsOnUiThread 等）也会被 GetMethods 返回，必须排除 IsSpecialName，
        // 否则断言会把属性当成"多出来的方法"。
        var methods = typeof(IDispatcher).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName)
            .ToArray();

        // 05 篇 §2 的权威四成员：IsOnUiThread / Post / InvokeAsync(Action) / InvokeAsync<T>(Func<T>)。
        // 两个 InvokeAsync 是重载，GetMethods 会各返回一次，所以按名字去重后再比较集合。
        Assert.That(properties.Select(p => p.Name), Is.EquivalentTo(new[] { "IsOnUiThread" }));
        Assert.That(methods.Select(m => m.Name).Distinct(StringComparer.Ordinal),
            Is.EquivalentTo(new[] { "Post", "InvokeAsync" }));
        Assert.That(methods.Count(m => m.Name == "InvokeAsync"), Is.EqualTo(2),
            "InvokeAsync 必须保留两个重载（Action 与 Func<T>）");
    }

    [Test]
    public void Dtos_never_reference_ckan_types()
    {
        var dtoTypes = typeof(ModRowData).Assembly.GetTypes()
            .Where(t => t.Namespace == "CkanUI.Services.Abstractions.Dtos")
            .ToArray();

        Assert.That(dtoTypes, Is.Not.Empty);

        foreach (var type in dtoTypes)
        {
            foreach (var property in type.GetProperties())
            {
                Assert.That(property.PropertyType.FullName, Does.Not.StartWith("CKAN."),
                    $"{type.Name}.{property.Name} 泄漏了 CKAN 类型");
            }
        }
    }

    [Test]
    public void All_service_interfaces_live_in_the_abstractions_assembly()
    {
        var interfaces = typeof(IDispatcher).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.StartsWith('I') && t.Name.Length > 1)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(interfaces, Is.SupersetOf(new[]
        {
            nameof(IDispatcher), nameof(IActivatable), nameof(IInstanceService),
            nameof(IRegistryService), nameof(IRepositoryService), nameof(IChangeSetService),
            nameof(IInstallerService), nameof(ISettingsService), nameof(IUiSettingsStore),
            nameof(IOperationQueue), nameof(IDialogService), nameof(IErrorReporter),
            nameof(IUnmanagedService),
        }));
    }

    [Test]
    public void ModRowData_stays_a_small_readonly_struct()
    {
        Assert.That(typeof(ModRowData).IsValueType, Is.True, "3000 行的列表投影必须是 struct");
        // 14 → 15：新增的 Facts 一个属性承载 desc:/lic:/lang:/dep:/rec:/sug:/conf:/sup:/label:/
        // is:cached/is:newly-compatible/is:replaceable 所需的全部服务层搜索事实（12 篇 F-20/F-21）。
        // 宁可多一个聚合字段，也不把 10 个子字段摊平进行宽，struct 语义保持不变。
        Assert.That(typeof(ModRowData).GetProperties().Length, Is.LessThanOrEqualTo(15));
    }

    [Test]
    public void ChangeSetPreview_exposes_structured_conflict_entries_without_changing_shape()
    {
        // B0/B2：结构化冲突必须是 init 属性，位置参数保持 4 个（全仓构造点不改）。
        var ctor = typeof(ChangeSetPreview).GetConstructors().Single();
        Assert.That(ctor.GetParameters().Length, Is.EqualTo(4), "位置参数不能增加");

        var property = typeof(ChangeSetPreview).GetProperty(nameof(ChangeSetPreview.ConflictEntries));
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(IReadOnlyList<ConflictEntry>)));
        Assert.That(property.SetMethod, Is.Not.Null, "必须是 init 属性，而不是只读计算属性");
        Assert.That(property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(),
                    Does.Contain(typeof(System.Runtime.CompilerServices.IsExternalInit)),
                    "init 访问器需要 IsExternalInit 修饰，确认外部无法在构造后改写");

        var preview = new ChangeSetPreview(
            Array.Empty<ChangeItemDto>(), Array.Empty<string>(), 0, Array.Empty<string>());
        Assert.That(preview.ConflictEntries, Is.Empty, "默认空集合，旧调用点语义不变");
        Assert.That(preview.HasConflicts, Is.False);

        var withEntry = preview with
        {
            ConflictEntries = new[] { new ConflictEntry("alpha 1.0 与 beta 2.0 冲突", "alpha", "alpha 模组") },
        };
        Assert.That(withEntry.HasConflicts, Is.True, "只有结构化条目时 HasConflicts 也必须为真");
    }

    [Test]
    public void InstallReport_warnings_are_not_failures()
    {
        // B1：只有失败项才让结果不完整；警告单独走 HasWarnings。
        var warned = new InstallReport(
            new[] { "alpha" }, Array.Empty<string>(), Array.Empty<string>(), new[] { "checksum mismatch" });
        var failed = new InstallReport(
            Array.Empty<string>(), Array.Empty<string>(), new[] { "beta" }, new[] { "checksum mismatch" });

        Assert.That(warned.IsFullSuccess, Is.True);
        Assert.That(warned.HasWarnings, Is.True);
        Assert.That(failed.IsFullSuccess, Is.False);
    }

    [Test]
    public void InstallHistoryRestoreResult_warnings_are_not_failures()
    {
        var plan = new InstallHistoryRestorePlan(
            DateTimeOffset.UnixEpoch, Array.Empty<ChangeItemDto>(), Array.Empty<string>(), 0);
        var warned = new InstallHistoryRestoreResult(
            plan, new[] { "alpha" }, Array.Empty<string>(), Array.Empty<string>(), new[] { "slow mirror" });

        Assert.That(warned.IsFullSuccess, Is.True);
        Assert.That(warned.HasWarnings, Is.True);
    }
}
