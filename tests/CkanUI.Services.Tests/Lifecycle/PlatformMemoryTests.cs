using System.Runtime.InteropServices;
using CkanUI.Services.Lifecycle;
using NUnit.Framework;

namespace CkanUI.Services.Tests.Lifecycle;

/// <summary>
/// 18 篇 §3 L3/L4（RLS-02/RLS-03）的平台内存回收契约测试。
/// 断言的是接口语义（是否接入、计数、文案、不抛异常），**不断言工作集一定下降**：
/// trim 之后内核可以立刻因为后续断言或日志换回页面，测下降会在 CI 上随机红。
/// 平台差异写在同一个用例里就地分支，而不是 <see cref="Assert.Ignore(string)"/>：
/// 跳过会把整套 runner 的汇总结果打成 Warning，门禁要求 0 失败 0 跳过。
/// </summary>
[TestFixture]
public sealed class PlatformMemoryTests
{
    /// <summary>
    /// 按当前平台断言 L4 的能力声明：Windows 必须 Supported=true 且文案非空；
    /// 其他平台必须 Supported=false 且文案说清「依赖 Windows + 当前平台标识」。
    /// 任何平台都不允许抛异常——调用失败只降级成带原因的结果对象。
    /// </summary>
    [Test]
    public void TrimWorkingSet_ReportsCurrentPlatformCapability()
    {
        var memory = new PlatformMemory();

        // 刻意不用 try/catch 包：内部一旦抛异常，用例自然失败，这正是「不能把异常扔给调用方」的断言。
        var result = memory.TrimWorkingSet();

        Assert.That(result.Detail, Is.Not.Null.And.Not.Empty, "无论成败都要带可读文案");
        Assert.That(result.ReleasedBytes, Is.GreaterThanOrEqualTo(0), "回收字节数只能是 0 或正数");

        if (OperatingSystem.IsWindows())
        {
            Assert.That(result.Supported, Is.True, "Windows 上 L4 已接入（psapi!EmptyWorkingSet）");
            Assert.That(result.ReleasedItemCount, Is.EqualTo(1), "一次 trim 记 1 次回收");
            Assert.That(result.Detail, Does.Contain("工作集"), "文案要说清是工作集回收");
        }
        else
        {
            Assert.That(result.Supported, Is.False, "非 Windows 没有 EmptyWorkingSet 的等价物，按契约报未接入");
            Assert.That(result.ReleasedItemCount, Is.EqualTo(0));
            Assert.That(result.ReleasedBytes, Is.EqualTo(0));
            Assert.That(result.Detail, Does.Contain("Windows"), "文案要说明该能力依赖 Windows");
            Assert.That(result.Detail, Does.Contain(RuntimeInformation.RuntimeIdentifier), "文案要带出当前平台标识，方便读报告");
        }
    }

    /// <summary>
    /// 已经跑过 L3 之后 L4 仍要执行：编排层在激进档就是先 L3 后 L4，
    /// 不能因为「刚回收过」就跳过 trim（跳过会把「没做」伪装成「做完了」）。
    /// </summary>
    [Test]
    public void TrimWorkingSet_StillRunsAfterManagedHeapCollection()
    {
        var memory = new PlatformMemory();

        var heap = memory.CollectManagedHeap();
        var first = memory.TrimWorkingSet();
        var second = memory.TrimWorkingSet();

        Assert.That(heap.Supported, Is.True, "L3 是纯托管动作，任何平台都接入");
        Assert.That(second.Supported, Is.EqualTo(first.Supported), "连调两次的接入结论必须一致，不能第二次就变卦");
        Assert.That(second.ReleasedItemCount, Is.EqualTo(first.ReleasedItemCount));
        Assert.That(second.ReleasedBytes, Is.GreaterThanOrEqualTo(0));
        Assert.That(second.Detail, Is.Not.Null.And.Not.Empty);

        if (OperatingSystem.IsWindows())
        {
            Assert.That(first.ReleasedItemCount, Is.EqualTo(1), "L3 之后第一次 trim 也必须真的执行");
            Assert.That(second.ReleasedItemCount, Is.EqualTo(1), "第二次 trim 同样要真的执行");
        }
    }

    /// <summary>
    /// L3 跨平台可测：只锁「已接入、字节数非负、文案非空」，不断言一定回收了多少
    /// （进程刚启动时堆里几乎没垃圾，差值为 0 是完全正常的结果）。
    /// </summary>
    [Test]
    public void CollectManagedHeap_ReportsSupportedAndNonNegativeBytes()
    {
        var memory = new PlatformMemory();

        var result = memory.CollectManagedHeap();

        Assert.That(result.Supported, Is.True, "L3 是纯托管动作，任何平台都接入");
        Assert.That(result.ReleasedItemCount, Is.EqualTo(1));
        Assert.That(result.ReleasedBytes, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.Detail, Is.Not.Null.And.Not.Empty);
    }
}
