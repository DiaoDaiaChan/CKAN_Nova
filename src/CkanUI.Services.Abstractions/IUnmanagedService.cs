using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>GameData 中未托管文件（M4，INT-09）。</summary>
public sealed record UnmanagedFileInfo(string RelativePath, long SizeBytes, bool IsDirectory, string? GuessIdentifier);

public interface IUnmanagedService
{
    Task<OperationResult<IReadOnlyList<UnmanagedFileInfo>>> ScanAsync(
        InstanceInfo instance,
        CancellationToken ct = default);

    /// <summary>把选中的文件接管为托管安装（写进注册表）。</summary>
    Task<OperationResult<IReadOnlyList<string>>> AdoptAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken ct = default);
}
