using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Extracts a full snapshot of a table and uploads it to the downstream service.
/// </summary>
public interface ISnapshotService
{
    Task<DatasetRun> ExtractSnapshotAsync(
        TableIdentifier table,
        string batchId,
        string leaseToken,
        SchemaManifest manifest,
        CancellationToken ct = default);
}
