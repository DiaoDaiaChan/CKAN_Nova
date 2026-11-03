using CkanUI.Plugin.Rp1.Career;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Rp1.Tests.UI;

internal sealed class FakeCareerService : IRp1CareerService
{
    public List<Rp1SaveSummary> Saves { get; } = [];

    public Rp1CareerTimeline? Timeline { get; set; }

    public OperationResult<Rp1CareerTimeline>? LoadResult { get; set; }

    public int ListCalls { get; private set; }

    public int LoadCalls { get; private set; }

    public string? LastSaveName { get; private set; }

    public Task<OperationResult<IReadOnlyList<Rp1SaveSummary>>> ListSavesAsync(string gameDir, CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult(OperationResult<IReadOnlyList<Rp1SaveSummary>>.Ok(Saves.ToArray()));
    }

    public Task<OperationResult<Rp1CareerTimeline>> LoadAsync(
        string gameDir,
        string saveName,
        string? instanceName = null,
        string? gameVersion = null,
        CancellationToken cancellationToken = default)
    {
        LoadCalls++;
        LastSaveName = saveName;
        return Task.FromResult(LoadResult ?? OperationResult<Rp1CareerTimeline>.Ok(Timeline!));
    }
}
