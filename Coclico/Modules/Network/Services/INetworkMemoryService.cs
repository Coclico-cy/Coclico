using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkMemoryService
{
    HardwareKnowledgeProfile? GetProfile(string hardwareFingerprint);
    Task RecordExperimentAsync(string hardwareFingerprint, string adapterDesc, string driverVer, ExperimentRecord experiment, CancellationToken ct = default);
    Task SaveProvenBestSettingAsync(string hardwareFingerprint, string keyword, string value, double score, CancellationToken ct = default);
    Task MarkKeywordHarmfulAsync(string hardwareFingerprint, string keyword, CancellationToken ct = default);
}