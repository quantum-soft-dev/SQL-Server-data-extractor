using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Executes a full-table snapshot for a single table: captures max LSN,
/// reads all rows, chunks, uploads, and commits the dataset.
/// Per R-001: LSN is captured before reading, advanced only after commit.
/// </summary>
public sealed class SnapshotService : ISnapshotService
{
    private readonly ICdcReader _cdcReader;
    private readonly IDownstreamClient _downstreamClient;
    private readonly ChunkingService _chunkingService;
    private readonly IStateStore _stateStore;

    public SnapshotService(
        ICdcReader cdcReader,
        IDownstreamClient downstreamClient,
        ChunkingService chunkingService,
        IStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(cdcReader);
        ArgumentNullException.ThrowIfNull(downstreamClient);
        ArgumentNullException.ThrowIfNull(chunkingService);
        ArgumentNullException.ThrowIfNull(stateStore);

        _cdcReader = cdcReader;
        _downstreamClient = downstreamClient;
        _chunkingService = chunkingService;
        _stateStore = stateStore;
    }

    /// <inheritdoc />
    public async Task<DatasetRun> ExtractSnapshotAsync(
        TableIdentifier table,
        string batchId,
        string leaseToken,
        SchemaManifest manifest,
        CancellationToken ct = default)
    {
        var dataset = DatasetRun.Create(table, null, null);

        // Step 1: Capture max LSN BEFORE reading (R-001 algorithm)
        var snapshotLsn = await _cdcReader.GetMaxLsnAsync(ct).ConfigureAwait(false);

        // Step 2: Create dataset in downstream
        var remoteDatasetId = await _downstreamClient.CreateDatasetAsync(
            batchId, leaseToken, table.FullName,
            null, null, manifest.Hash.Value, ct).ConfigureAwait(false);

        dataset.SetRemoteDatasetId(remoteDatasetId);
        dataset.MarkUploading();

        try
        {
            // Step 3: Read full table (within SNAPSHOT isolation per CdcReader)
            var columnNames = manifest.Columns.Select(c => c.Name).ToList();
            var rows = _cdcReader.ReadFullTableAsync(table, ct);

            // Step 4+5: Chunk rows into gzip CSV and upload each as it is produced
            await foreach (var chunk in _chunkingService.ChunkSnapshotRowsAsync(
                columnNames, rows, ct).ConfigureAwait(false))
            {
                await using (chunk.Data)
                {
                    await _downstreamClient.UploadChunkAsync(
                        remoteDatasetId, leaseToken, chunk.ChunkNumber, chunk.Data, ct)
                        .ConfigureAwait(false);
                }

                dataset.IncrementChunk(chunk.CompressedSize);
                dataset.IncrementRows(chunk.RowCount);
            }

            // Step 6: Commit dataset
            await _downstreamClient.CommitDatasetAsync(remoteDatasetId, leaseToken, ct)
                .ConfigureAwait(false);

            dataset.Commit();

            // Step 7: Update table state — advance LSN ONLY after commit
            var tableState = await _stateStore.GetTableStateAsync(table, ct).ConfigureAwait(false);
            if (tableState is not null)
            {
                tableState.MarkComplete(snapshotLsn, manifest.Hash, DateTimeOffset.UtcNow);
                await _stateStore.UpsertTableStateAsync(tableState, ct).ConfigureAwait(false);
            }

            return dataset;
        }
        catch
        {
            // Abort on failure — do NOT advance the LSN
            try
            {
                await _downstreamClient.AbortDatasetAsync(remoteDatasetId, leaseToken, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best-effort abort; the original exception is more important
            }

            throw;
        }
    }
}
