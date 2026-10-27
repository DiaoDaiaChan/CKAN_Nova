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
        Assert.That(typeof(ModRowData).GetProperties().Length, Is.LessThanOrEqualTo(14));
    }
}
