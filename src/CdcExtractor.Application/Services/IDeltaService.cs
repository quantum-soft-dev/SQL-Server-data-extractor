using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Extracts incremental CDC changes for a single table and uploads them to the downstream service.
/// </summary>
public interface IDeltaService
{
    Task<DatasetRun> ExtractDeltaAsync(
        TableState tableState,
        string batchId,
        string leaseToken,
        SchemaManifest manifest,
        CancellationToken ct = default);
}
