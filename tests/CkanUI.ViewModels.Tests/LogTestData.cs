using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using CkanUI.ViewModels.Pages.Logs;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 日志页测试共用的构造器：用真实 <see cref="LogStore"/> + <see cref="LogQueryService"/>，
/// 而不是自造假查询服务——排序、聚合、指纹折叠这些行为正是被测对象本身。
/// 分类器传 null：测试里要显式指定日志族，不能被内置规则改回去。
/// </summary>
internal static class LogTestData
{
    public static LogStore CreateStore(int capacity = 500) =>
        new(null, new LogStoreOptions { Capacity = capacity });

    public static LogQueryService CreateQuery(ILogStore store) => new(store);

    public static LogEvent Entry(
        LogLevel level,
        string message,
        LogFamily family = LogFamily.Other,
        LogSourceKind kind = LogSourceKind.Log4Net,
        string? detail = null,
        string source = "log4net:CKAN.Test",
        IReadOnlyDictionary<string, string>? tags = null) =>
        LogEvent.Create(kind, level, source, message, detail, tags: tags) with { Family = family };

    public static (LogStore Store, LogQueryService Query, LogPageViewModel Page) CreatePage(
        int capacity = 500,
        ILogBundleService? bundle = null)
    {
        var store = CreateStore(capacity);
        var query = CreateQuery(store);
        return (store, query, new LogPageViewModel(store, query, bundle));
    }
}

/// <summary>诊断包服务替身：默认成功，可换成失败或抛异常，用来覆盖「未接线 / 失败 / 成功」三条路径。</summary>
internal sealed class FakeLogBundleService : ILogBundleService
{
    public Func<LogBundleRequest, OperationResult<LogBundleResult>> Handler { get; set; } = request =>
        OperationResult<LogBundleResult>.Ok(new LogBundleResult
        {
            ZipPath = request.OutputPath,
            ByteLength = 1024,
            TailLineCount = 7,
            ErrorLineCount = 2,
            Entries = Array.Empty<string>(),
            Sha256 = "deadbeef",
        });

    public List<LogBundleRequest> Requests { get; } = new();

    public Task<OperationResult<LogBundleResult>> CreateAsync(
        LogBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Handler(request));
    }
}
