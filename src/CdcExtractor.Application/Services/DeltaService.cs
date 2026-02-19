using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Extracts incremental CDC changes for a single table: computes LSN range,
/// reads changes, chunks, uploads, and commits the dataset.
/// LSN is advanced only after successful downstream commit (per R-001).
/// </summary>
public sealed class DeltaService : IDeltaService
{
    private readonly ICdcReader _cdcReader;
    private readonly IDownstreamClient _downstreamClient;
    private readonly ChunkingService _chunkingService;
    private readonly IStateStore _stateStore;

    public DeltaService(
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
    public async Task<DatasetRun> ExtractDeltaAsync(
        TableState tableState,
        string batchId,
        string leaseToken,
        SchemaManifest manifest,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tableState);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        ArgumentNullException.ThrowIfNull(manifest);

        // Step 1: Calculate LSN range
        // from_lsn = increment(last_processed_lsn) — exclusive boundary per R-001
        var captureInstance = tableState.CaptureInstance
            ?? throw new InvalidOperationException(
                $"Table {tableState.TableId.FullName} has no capture instance configured.");

        var fromLsn = await _cdcReader.IncrementLsnAsync(tableState.LastProcessedLsn, ct)
            .ConfigureAwait(false);
        var toLsn = await _cdcReader.GetMaxLsnAsync(ct).ConfigureAwait(false);

        // No new changes — skip this table
        if (fromLsn.CompareTo(toLsn) > 0)
        {
            var skipped = DatasetRun.Create(tableState.TableId, fromLsn, toLsn);
            skipped.Skip("No new CDC changes available");
            return skipped;
        }

        // Step 1b: CDC gap detection — verify change history is available
        var minAvailableLsn = await _cdcReader.GetMinLsnAsync(captureInstance, ct)
            .ConfigureAwait(false);

        if (minAvailableLsn.CompareTo(fromLsn) > 0)
        {
            // Gap detected: CDC history was cleaned before we could extract it.
            // Mark table for re-bootstrap and report error — do NOT extract partial data.
            var reason = $"CDC gap detected for {tableState.TableId.FullName}: " +
                $"expected LSN {fromLsn}, but minimum available LSN is {minAvailableLsn}. " +
                "Change history was cleaned before extraction. Table will be re-bootstrapped on next snapshot run.";

            tableState.MarkReBootstrap(reason);
            await _stateStore.UpsertTableStateAsync(tableState, ct).ConfigureAwait(false);

            try
            {
                await _downstreamClient.ReportErrorAsync(
                    batchId, leaseToken, "TABLE", tableState.TableId.FullName,
                    null, "ERROR", "CDC_GAP_DETECTED", reason,
                    isRetryable: false, isTerminal: true, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort error reporting; the gap handling is more important
            }

            var gapDataset = DatasetRun.Create(tableState.TableId, fromLsn, toLsn);
            gapDataset.Abort("CDC_GAP_DETECTED", reason);
            return gapDataset;
        }

        var dataset = DatasetRun.Create(tableState.TableId, fromLsn, toLsn);

        // Step 2: Create dataset in downstream with LSN range
        var remoteDatasetId = await _downstreamClient.CreateDatasetAsync(
            batchId, leaseToken, tableState.TableId.FullName,
            fromLsn.ToString(), toLsn.ToString(), manifest.Hash.Value, ct)
            .ConfigureAwait(false);

        dataset.SetRemoteDatasetId(remoteDatasetId);
        dataset.MarkUploading();

        try
        {
            // Step 3: Read CDC changes in LSN range
            var columnNames = manifest.Columns.Select(c => c.Name).ToList();
            var changes = _cdcReader.ReadAllChangesAsync(captureInstance, fromLsn, toLsn, ct);

            // Step 4: Chunk CDC rows into gzip CSV (includes _op/_lsn/_seqval/_ts service columns)
            var chunks = await _chunkingService.ChunkCdcRowsAsync(columnNames, changes, ct)
                .ConfigureAwait(false);

            // Step 5: Upload each chunk
            foreach (var chunk in chunks)
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

            // Step 7: Advance LSN ONLY after successful commit
            tableState.AdvanceLsn(toLsn, DateTimeOffset.UtcNow);
            await _stateStore.UpsertTableStateAsync(tableState, ct).ConfigureAwait(false);

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
