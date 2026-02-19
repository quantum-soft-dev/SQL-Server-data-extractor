using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Persists and retrieves batch run history.
/// </summary>
public interface IBatchHistoryStore
{
    Task SaveBatchAsync(BatchRun batch, CancellationToken ct = default);
    Task UpdateBatchStatusAsync(BatchId id, BatchStatus status, DateTimeOffset? finishedAt, CancellationToken ct = default);
    Task<IReadOnlyList<BatchRun>> GetRecentBatchesAsync(int limit, CancellationToken ct = default);
    Task<BatchRun?> GetBatchByIdAsync(BatchId id, CancellationToken ct = default);
}
