using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Tests;

/// <summary>导入导出服务的替身：记录调用参数，返回预设结果，不碰文件系统。</summary>
internal sealed class ModPacksFakeModPackService : IModPackService
{
    public bool HasActiveInstance { get; set; } = true;

    public string? InstanceName { get; set; } = "测试实例";

    public int ExportCalls { get; private set; }

    public int PreviewCalls { get; private set; }

    public int ImportCalls { get; private set; }

    public int CompareCalls { get; private set; }

    public ModPackExportRequest? LastRequest { get; private set; }

    public IReadOnlyCollection<string>? LastPaths { get; private set; }

    public ModPackPreview? LastPreview { get; private set; }

    public IReadOnlyCollection<string>? LastSelected { get; private set; }

    public string? LastLeft { get; private set; }

    public string? LastRight { get; private set; }

    public OperationResult<ModPackExportResult> ExportResult { get; set; } =
        OperationResult<ModPackExportResult>.Ok(Export("D:/out/mods.ckan", 2));

    public OperationResult<ModPackPreview> PreviewResult { get; set; } =
        OperationResult<ModPackPreview>.Ok(EmptyPreview());

    public OperationResult<ModPackImportReport> ImportResult { get; set; } =
        OperationResult<ModPackImportReport>.Ok(new ModPackImportReport(
            new[] { "A" },
            Array.Empty<string>(),
            Array.Empty<string>()));

    public OperationResult<InstanceComparison> CompareResult { get; set; } =
        OperationResult<InstanceComparison>.Ok(new InstanceComparison("左", "右", Array.Empty<InstanceDiffItem>()));

    public Exception? ExportThrows { get; set; }

    public Exception? PreviewThrows { get; set; }

    public Exception? ImportThrows { get; set; }

    public Exception? CompareThrows { get; set; }

    /// <summary>导入时是否先上报一条 Failed 进度（用于验证进度与错误条的联动）。</summary>
    public bool ImportReportsFailureProgress { get; set; }

    public Task<OperationResult<ModPackExportResult>> ExportAsync(
        ModPackExportRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ExportCalls++;
        LastRequest = request;
        if (ExportThrows is { } boom)
        {
            throw boom;
        }

        progress?.Report(new OperationProgress("modpack-export", "export", "正在导出模组列表", null, 40, false, OperationState.Running));
        return Task.FromResult(ExportResult);
    }

    public Task<OperationResult<ModPackPreview>> PreviewImportAsync(
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken ct = default)
    {
        PreviewCalls++;
        LastPaths = sourcePaths;
        if (PreviewThrows is { } boom)
        {
            throw boom;
        }

        return Task.FromResult(PreviewResult);
    }

    public Task<OperationResult<ModPackImportReport>> ImportAsync(
        ModPackPreview preview,
        IReadOnlyCollection<string>? selectedIdentifiers = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ImportCalls++;
        LastPreview = preview;
        LastSelected = selectedIdentifiers;
        if (ImportThrows is { } boom)
        {
            throw boom;
        }

        if (ImportReportsFailureProgress)
        {
            progress?.Report(new OperationProgress("modpack-import", "import", "导入失败", "磁盘已满", 50, false, OperationState.Failed));
        }
        else
        {
            progress?.Report(new OperationProgress("modpack-import", "import", "正在导入模组", "正在安装 A", 50, false, OperationState.Running));
        }

        return Task.FromResult(ImportResult);
    }

    public Task<OperationResult<InstanceComparison>> CompareAsync(
        string leftInstance,
        string rightInstance,
        CancellationToken ct = default)
    {
        CompareCalls++;
        LastLeft = leftInstance;
        LastRight = rightInstance;
        if (CompareThrows is { } boom)
        {
            throw boom;
        }

        return Task.FromResult(CompareResult);
    }

    public static ModPackExportResult Export(string path, int moduleCount, string? archive = null, int bundled = 0)
        => new(path, moduleCount, archive, bundled, bundled * 1024L, 128L, Array.Empty<string>());

    public static ModPackPreview EmptyPreview()
        => new(Array.Empty<string>(), "空包", Array.Empty<ModPackEntry>(), Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>实例服务的替身：列表可改、事件可手动触发。</summary>
internal sealed class ModPacksFakeInstanceService : IInstanceService
{
    public List<InstanceInfo> Items { get; } = new();

    public IReadOnlyList<InstanceInfo> Instances => Items;

    public InstanceInfo? Current => Items.FirstOrDefault();

    public event Action? InstancesChanged;

    public event Action<InstanceInfo?>? CurrentChanged;

    public int RefreshCalls { get; private set; }

    public OperationResult<IReadOnlyList<InstanceInfo>> RefreshResult { get; set; } =
        OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Array.Empty<InstanceInfo>());

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshCalls++;
        return Task.FromResult(RefreshResult);
    }

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持切换实例。"));

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持新增实例。"));

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持克隆实例。"));

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Fail("测试替身不支持移除实例。"));

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持重命名实例。"));

    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public void RaiseCurrentChanged(InstanceInfo? info) => CurrentChanged?.Invoke(info);

    public void Seed(params string[] names)
    {
        Items.Clear();
        foreach (var name in names)
        {
            Items.Add(new InstanceInfo(name, "C:/games/" + name, "1.12.5", false, false, 0));
        }
    }
}

/// <summary>错误上报替身：只记录，不弹窗。</summary>
internal sealed class ModPacksFakeErrorReporter : IErrorReporter
{
    /// <summary>按顺序记录的错误条目，供用例断言。</summary>
    public List<ErrorEntry> Entries { get; } = new();

    public event Action<ErrorEntry>? Reported;

    public IReadOnlyList<ErrorEntry> Recent => Entries;

    public void Report(Exception exception, string context, string? userMessage = null)
        => Entries.Add(new ErrorEntry(DateTime.UtcNow, context, userMessage ?? exception.Message, null, exception));

    public void Report(string context, string userMessage, string? detail = null)
        => Entries.Add(new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null));

    /// <summary>触发 <see cref="Reported"/>，避免事件从未被使用。</summary>
    public void Raise() => Reported?.Invoke(Entries.LastOrDefault()!);
}
