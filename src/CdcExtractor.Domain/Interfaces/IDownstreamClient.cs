using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;

namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Client for communicating with the downstream data ingestion service.
/// Manages batch lifecycle, dataset uploads, and schema reporting.
/// </summary>
public interface IDownstreamClient
{
    Task<(string batchId, string leaseToken)> CreateBatchAsync(
        BatchType type, string sqlServer, string database, CancellationToken ct = default);

    Task HeartbeatAsync(string batchId, string leaseToken, CancellationToken ct = default);

    Task FinishBatchAsync(
        string batchId, string leaseToken, BatchStatus status,
        int tablesTotal, int tablesSucceeded, long totalRows, long totalBytes,
        CancellationToken ct = default);

    Task ReportErrorAsync(
        string batchId, string leaseToken, string scope, string? table, string? datasetId,
        string severity, string code, string message, bool isRetryable, bool isTerminal,
        CancellationToken ct = default);

    Task<string> CreateDatasetAsync(
        string batchId, string leaseToken, string table,
        string? fromLsn, string? toLsn, string schemaHash,
        CancellationToken ct = default);

    Task UploadChunkAsync(
        string datasetId, string leaseToken, int chunkNo, Stream gzipCsv,
        CancellationToken ct = default);

    Task CommitDatasetAsync(string datasetId, string leaseToken, CancellationToken ct = default);

    Task AbortDatasetAsync(string datasetId, string leaseToken, CancellationToken ct = default);

    Task UploadSchemaAsync(
        string table, string schemaHash, SchemaManifest manifest, CancellationToken ct = default);
}
