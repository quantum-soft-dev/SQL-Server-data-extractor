using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Exceptions;
using CdcExtractor.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Orchestrates extraction batches — creates batch in downstream, iterates tables,
/// delegates to SnapshotService, updates state store, finishes batch.
/// </summary>
public sealed class ExtractionOrchestrator
{
    private readonly IStateStore _stateStore;
    private readonly IBatchHistoryStore _batchHistoryStore;
    private readonly IDownstreamClient _downstreamClient;
    private readonly ISnapshotService _snapshotService;
    private readonly IDeltaService _deltaService;
    private readonly ISchemaService _schemaService;
    private readonly IHeartbeatCoordinator? _heartbeat;
    private readonly SqlServerConfig _sqlConfig;
    private readonly ILogger<ExtractionOrchestrator> _logger;

    public ExtractionOrchestrator(
        IStateStore stateStore,
        IBatchHistoryStore batchHistoryStore,
        IDownstreamClient downstreamClient,
        ISnapshotService snapshotService,
        IDeltaService deltaService,
        ISchemaService schemaService,
        SqlServerConfig sqlConfig,
        ILogger<ExtractionOrchestrator> logger,
        IHeartbeatCoordinator? heartbeat = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(batchHistoryStore);
        ArgumentNullException.ThrowIfNull(downstreamClient);
        ArgumentNullException.ThrowIfNull(snapshotService);
        ArgumentNullException.ThrowIfNull(deltaService);
        ArgumentNullException.ThrowIfNull(schemaService);
        ArgumentNullException.ThrowIfNull(sqlConfig);
        ArgumentNullException.ThrowIfNull(logger);

        _stateStore = stateStore;
        _batchHistoryStore = batchHistoryStore;
        _downstreamClient = downstreamClient;
        _snapshotService = snapshotService;
        _deltaService = deltaService;
        _schemaService = schemaService;
        _heartbeat = heartbeat;
        _sqlConfig = sqlConfig;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full SNAPSHOT batch for all tracked tables.
    /// </summary>
    public async Task<BatchRun> RunSnapshotBatchAsync(
        BatchTrigger trigger, CancellationToken ct = default)
    {
        var batch = BatchRun.Create(BatchType.Snapshot, trigger);

        _logger.LogInformation(
            "Starting SNAPSHOT batch {BatchId} triggered by {Trigger}",
            batch.Id, trigger);

        await _batchHistoryStore.SaveBatchAsync(batch, ct).ConfigureAwait(false);

        var sqlServer = string.IsNullOrEmpty(_sqlConfig.Instance)
            ? _sqlConfig.Server
            : $"{_sqlConfig.Server}\\{_sqlConfig.Instance}";

        var (remoteBatchId, leaseToken) = await _downstreamClient.CreateBatchAsync(
            BatchType.Snapshot, sqlServer, _sqlConfig.Database, ct).ConfigureAwait(false);

        batch.SetRemoteBatchId(remoteBatchId);
        batch.SetLeaseToken(leaseToken);

        var tables = await _stateStore.GetAllTableStatesAsync(ct).ConfigureAwait(false);
        var hasFailure = false;
        long totalRows = 0;
        long totalBytes = 0;
        var tablesSucceeded = 0;

        foreach (var tableState in tables)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation(
                    "Extracting snapshot for table {Table} in batch {BatchId}",
                    tableState.TableId.FullName, batch.Id);

                var manifest = await _schemaService.InspectAndUploadSchemaAsync(
                    tableState.TableId, remoteBatchId, leaseToken, ct).ConfigureAwait(false);

                var dataset = await _snapshotService.ExtractSnapshotAsync(
                    tableState.TableId, remoteBatchId, leaseToken, manifest, ct)
                    .ConfigureAwait(false);

                batch.AddDataset(dataset);

                totalRows += dataset.RowCount;
                totalBytes += dataset.BytesSent;
                tablesSucceeded++;

                _logger.LogInformation(
                    "Snapshot complete for {Table}: {Rows} rows, {Chunks} chunks",
                    tableState.TableId.FullName, dataset.RowCount, dataset.ChunkCount);
            }
            catch (Exception ex) when (IsBatchLevelError(ex))
            {
                // Batch-level error: downstream unreachable or lease conflict — stop entire batch
                var batchStatus = IsLeaseConflict(ex) ? BatchStatus.Aborted : BatchStatus.Failed;

                _logger.LogError(ex,
                    "Batch-level error ({ErrorType}) during snapshot of {Table} in batch {BatchId}. " +
                    "Stopping batch with status {Status}. " +
                    "Check downstream service connectivity and verify single-instance deployment.",
                    ex.GetType().Name, tableState.TableId.FullName, batch.Id, batchStatus);

                hasFailure = true;
                batch.Finish(batchStatus);

                await FinishBatchSafeAsync(batch, remoteBatchId, leaseToken, batchStatus,
                    tables.Count, tablesSucceeded, totalRows, totalBytes, ct).ConfigureAwait(false);

                return batch;
            }
            catch (Exception ex)
            {
                // Table-level error: skip table, continue with remaining tables
                hasFailure = true;

                var errorCode = "SNAPSHOT_FAILED";
                var errorMessage = $"Snapshot failed for table {tableState.TableId.FullName} in batch {batch.Id}: " +
                    $"{ex.Message}. Verify the table exists, is readable, and CDC is enabled.";

                var failedDataset = DatasetRun.Create(tableState.TableId, null, null);
                failedDataset.Abort(errorCode, errorMessage);
                batch.AddDataset(failedDataset);

                tableState.SetError(errorMessage);
                await _stateStore.UpsertTableStateAsync(tableState, ct).ConfigureAwait(false);

                _logger.LogError(ex,
                    "Table-level error during snapshot of {Table} in batch {BatchId}. " +
                    "Skipping table; remaining tables will continue.",
                    tableState.TableId.FullName, batch.Id);

                try
                {
                    await _downstreamClient.ReportErrorAsync(
                        remoteBatchId, leaseToken, "TABLE", tableState.TableId.FullName,
                        null, "ERROR", errorCode, errorMessage,
                        isRetryable: false, isTerminal: false, ct).ConfigureAwait(false);
                }
                catch (Exception reportEx)
                {
                    _logger.LogWarning(reportEx,
                        "Failed to report error to downstream for table {Table} in batch {BatchId}. " +
                        "Error will be retried on next batch run.",
                        tableState.TableId.FullName, batch.Id);
                }
            }
        }

        var finalStatus = hasFailure ? BatchStatus.Failed : BatchStatus.Succeeded;
        batch.Finish(finalStatus);

        await FinishBatchSafeAsync(batch, remoteBatchId, leaseToken, finalStatus,
            tables.Count, tablesSucceeded, totalRows, totalBytes, ct).ConfigureAwait(false);

        return batch;
    }

    /// <summary>
    /// Runs a DELTA batch: routes CDC-mode tables to DeltaService and
    /// SNAP-mode tables to SnapshotService. Per-table errors do not stop the batch.
    /// Batch-level errors (downstream unreachable, lease conflict) stop the entire batch.
    /// </summary>
    public async Task<BatchRun> RunDeltaBatchAsync(
        BatchTrigger trigger, CancellationToken ct = default)
    {
        var batch = BatchRun.Create(BatchType.Delta, trigger);

        _logger.LogInformation(
            "Starting DELTA batch {BatchId} triggered by {Trigger}",
            batch.Id, trigger);

        await _batchHistoryStore.SaveBatchAsync(batch, ct).ConfigureAwait(false);

        var sqlServer = string.IsNullOrEmpty(_sqlConfig.Instance)
            ? _sqlConfig.Server
            : $"{_sqlConfig.Server}\\{_sqlConfig.Instance}";

        var (remoteBatchId, leaseToken) = await _downstreamClient.CreateBatchAsync(
            BatchType.Delta, sqlServer, _sqlConfig.Database, ct).ConfigureAwait(false);

        batch.SetRemoteBatchId(remoteBatchId);
        batch.SetLeaseToken(leaseToken);

        // Start heartbeat to prevent batch TTL expiration
        _heartbeat?.StartHeartbeat(remoteBatchId, leaseToken);

        try
        {
            var tables = await _stateStore.GetAllTableStatesAsync(ct).ConfigureAwait(false);
            var hasFailure = false;
            long totalRows = 0;
            long totalBytes = 0;
            var tablesSucceeded = 0;

            foreach (var tableState in tables)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var manifest = await _schemaService.InspectAndUploadSchemaAsync(
                        tableState.TableId, remoteBatchId, leaseToken, ct).ConfigureAwait(false);

                    DatasetRun dataset;

                    if (tableState.BootstrapStatus == BootstrapStatus.ReBootstrap)
                    {
                        // RE_BOOTSTRAP tables: CDC gap was detected previously.
                        // Route to SnapshotService for full re-extract to re-establish LSN baseline.
                        _logger.LogWarning(
                            "Re-bootstrapping table {Table} in batch {BatchId} due to CDC gap. " +
                            "Performing full snapshot to re-establish LSN baseline.",
                            tableState.TableId.FullName, batch.Id);

                        // Report the gap error to downstream before re-extracting
                        try
                        {
                            var gapMessage = $"CDC gap detected for {tableState.TableId.FullName}. " +
                                "Change history was cleaned before extraction. " +
                                "Performing full re-bootstrap snapshot.";

                            await _downstreamClient.ReportErrorAsync(
                                remoteBatchId, leaseToken, "TABLE", tableState.TableId.FullName,
                                null, "ERROR", "CDC_GAP_DETECTED", gapMessage,
                                isRetryable: false, isTerminal: true, ct).ConfigureAwait(false);
                        }
                        catch (Exception reportEx)
                        {
                            _logger.LogWarning(reportEx,
                                "Failed to report CDC gap error to downstream for table {Table}",
                                tableState.TableId.FullName);
                        }

                        dataset = await _snapshotService.ExtractSnapshotAsync(
                            tableState.TableId, remoteBatchId, leaseToken, manifest, ct)
                            .ConfigureAwait(false);

                        // SnapshotService calls MarkComplete internally, resetting status to Complete.
                        // Persist the updated state.
                        await _stateStore.UpsertTableStateAsync(tableState, ct).ConfigureAwait(false);
                    }
                    else if (tableState.ExtractionMode == ExtractionMode.Snap)
                    {
                        // SNAP-mode tables always get full snapshots
                        _logger.LogInformation(
                            "Extracting snapshot for SNAP-mode table {Table} in batch {BatchId}",
                            tableState.TableId.FullName, batch.Id);

                        dataset = await _snapshotService.ExtractSnapshotAsync(
                            tableState.TableId, remoteBatchId, leaseToken, manifest, ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // CDC-mode tables get incremental delta extraction
                        _logger.LogInformation(
                            "Extracting delta for CDC-mode table {Table} in batch {BatchId}",
                            tableState.TableId.FullName, batch.Id);

                        dataset = await _deltaService.ExtractDeltaAsync(
                            tableState, remoteBatchId, leaseToken, manifest, ct)
                            .ConfigureAwait(false);
                    }

                    batch.AddDataset(dataset);

                    totalRows += dataset.RowCount;
                    totalBytes += dataset.BytesSent;
                    tablesSucceeded++;

                    _logger.LogInformation(
                        "Extraction complete for {Table}: {Rows} rows, {Chunks} chunks, status {Status}",
                        tableState.TableId.FullName, dataset.RowCount, dataset.ChunkCount, dataset.Status);
                }
                catch (Exception ex) when (IsBatchLevelError(ex))
                {
                    // Batch-level error: downstream unreachable or lease conflict — stop entire batch
                    var batchStatus = IsLeaseConflict(ex) ? BatchStatus.Aborted : BatchStatus.Failed;

                    _logger.LogError(ex,
                        "Batch-level error ({ErrorType}) during extraction of {Table} in batch {BatchId}. " +
                        "Stopping batch with status {Status}. " +
                        "Check downstream service connectivity and verify single-instance deployment.",
                        ex.GetType().Name, tableState.TableId.FullName, batch.Id, batchStatus);

                    hasFailure = true;
                    batch.Finish(batchStatus);

                    await FinishBatchSafeAsync(batch, remoteBatchId, leaseToken, batchStatus,
                        tables.Count, tablesSucceeded, totalRows, totalBytes, ct).ConfigureAwait(false);

                    return batch;
                }
                catch (Exception ex)
                {
                    // Table-level error: skip table, continue with remaining tables
                    hasFailure = true;

                    var errorCode = tableState.ExtractionMode == ExtractionMode.Snap
                        ? "SNAPSHOT_FAILED"
                        : "DELTA_FAILED";
                    var errorMessage = $"{errorCode} for table {tableState.TableId.FullName} " +
                        $"(mode: {tableState.ExtractionMode}) in batch {batch.Id}: {ex.Message}. " +
                        "Verify table permissions, CDC status, and network connectivity.";

                    var failedDataset = DatasetRun.Create(tableState.TableId, null, null);
                    failedDataset.Abort(errorCode, errorMessage);
                    batch.AddDataset(failedDataset);

                    tableState.SetError(errorMessage);
                    await _stateStore.UpsertTableStateAsync(tableState, ct).ConfigureAwait(false);

                    _logger.LogError(ex,
                        "Table-level error during {Mode} extraction of {Table} in batch {BatchId}. " +
                        "Skipping table; remaining tables will continue.",
                        tableState.ExtractionMode, tableState.TableId.FullName, batch.Id);

                    try
                    {
                        await _downstreamClient.ReportErrorAsync(
                            remoteBatchId, leaseToken, "TABLE", tableState.TableId.FullName,
                            null, "ERROR", errorCode, errorMessage,
                            isRetryable: false, isTerminal: false, ct).ConfigureAwait(false);
                    }
                    catch (Exception reportEx)
                    {
                        _logger.LogWarning(reportEx,
                            "Failed to report error to downstream for table {Table} in batch {BatchId}. " +
                            "Error will be retried on next batch run.",
                            tableState.TableId.FullName, batch.Id);
                    }
                }
            }

            var finalStatus = hasFailure ? BatchStatus.Failed : BatchStatus.Succeeded;
            batch.Finish(finalStatus);

            await FinishBatchSafeAsync(batch, remoteBatchId, leaseToken, finalStatus,
                tables.Count, tablesSucceeded, totalRows, totalBytes, ct).ConfigureAwait(false);

            return batch;
        }
        finally
        {
            _heartbeat?.StopHeartbeat();
        }
    }

    /// <summary>
    /// Determines whether an exception is a batch-level error that should stop the entire batch.
    /// Batch-level: downstream unreachable (DownstreamUnavailableException), lease conflict (SinkUploadException with 409).
    /// Table-level (all others): permission denied, CDC disabled, SQL errors — skip table, continue.
    /// </summary>
    private static bool IsBatchLevelError(Exception ex) =>
        ex is DownstreamUnavailableException ||
        IsLeaseConflict(ex) ||
        (ex is SinkUploadException { HttpStatusCode: >= 500 });

    /// <summary>
    /// Returns true if the exception represents a 409 lease conflict from the downstream API.
    /// </summary>
    private static bool IsLeaseConflict(Exception ex) =>
        ex is SinkUploadException { HttpStatusCode: 409 };

    /// <summary>
    /// Safely finishes a batch in downstream and local history, logging any errors.
    /// </summary>
    private async Task FinishBatchSafeAsync(
        BatchRun batch, string remoteBatchId, string leaseToken, BatchStatus status,
        int tablesTotal, int tablesSucceeded, long totalRows, long totalBytes,
        CancellationToken ct)
    {
        try
        {
            await _downstreamClient.FinishBatchAsync(
                remoteBatchId, leaseToken, status,
                tablesTotal, tablesSucceeded, totalRows, totalBytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finish batch {BatchId} in downstream", batch.Id);
        }

        await _batchHistoryStore.UpdateBatchStatusAsync(
            batch.Id, status, batch.FinishedAt, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Batch {BatchId} finished with status {Status}: {Succeeded}/{Total} tables",
            batch.Id, status, tablesSucceeded, tablesTotal);
    }
}
