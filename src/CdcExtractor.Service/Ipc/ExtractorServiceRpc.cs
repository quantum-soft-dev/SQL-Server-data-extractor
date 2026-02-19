using System.Diagnostics;
using CdcExtractor.Contracts.Ipc;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Service.Workers;
using StreamJsonRpc;

namespace CdcExtractor.Service.Ipc;

/// <summary>
/// JSON-RPC method implementations for the IPC contract.
/// Stub implementation for US1 — getStatus and getBatchProgress return real data,
/// remaining methods return placeholder responses.
/// </summary>
public sealed class ExtractorServiceRpc : IExtractorService
{
    private readonly IBatchHistoryStore _batchHistoryStore;
    private readonly IStateStore _stateStore;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly LogBroadcaster _logBroadcaster;
    private readonly SchedulerWorker _schedulerWorker;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly string _subscriberId = Guid.NewGuid().ToString("N");

    public ExtractorServiceRpc(
        IBatchHistoryStore batchHistoryStore,
        IStateStore stateStore,
        IDiagnosticsService diagnosticsService,
        LogBroadcaster logBroadcaster,
        SchedulerWorker schedulerWorker)
    {
        ArgumentNullException.ThrowIfNull(batchHistoryStore);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(diagnosticsService);
        ArgumentNullException.ThrowIfNull(logBroadcaster);
        ArgumentNullException.ThrowIfNull(schedulerWorker);

        _batchHistoryStore = batchHistoryStore;
        _stateStore = stateStore;
        _diagnosticsService = diagnosticsService;
        _logBroadcaster = logBroadcaster;
        _schedulerWorker = schedulerWorker;
    }

    [JsonRpcMethod("getStatus")]
    public Task<ServiceStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var uptime = DateTimeOffset.UtcNow - _startedAt;
        var dto = new ServiceStatusDto(
            IsRunning: _schedulerWorker.IsRunning,
            CurrentBatchId: null,
            CurrentBatchType: null,
            CurrentBatchTrigger: null,
            CurrentBatchStartedAt: null,
            NextScheduledRun: _schedulerWorker.GetNextRunTime(),
            ServiceUptime: $"PT{(int)uptime.TotalHours}H{uptime.Minutes}M",
            ServicePid: Environment.ProcessId);

        return Task.FromResult(dto);
    }

    [JsonRpcMethod("getBatchProgress")]
    public Task<BatchProgressDto> GetBatchProgressAsync(CancellationToken ct = default)
    {
        var dto = new BatchProgressDto(null, []);
        return Task.FromResult(dto);
    }

    [JsonRpcMethod("getRecentBatches")]
    public async Task<RecentBatchesDto> GetRecentBatchesAsync(int limit, CancellationToken ct = default)
    {
        var batches = await _batchHistoryStore.GetRecentBatchesAsync(limit, ct).ConfigureAwait(false);
        var summaries = batches.Select(b => new BatchSummaryDto(
            b.Id.ToString(),
            b.Type.ToString().ToUpperInvariant(),
            b.Trigger.ToString().ToUpperInvariant(),
            b.Status.ToString().ToUpperInvariant(),
            b.StartedAt,
            b.FinishedAt,
            b.Datasets.Count,
            b.Datasets.Count(d => d.Status == Domain.Enums.DatasetStatus.Committed),
            b.Datasets.Sum(d => d.RowCount)
        )).ToList();

        return new RecentBatchesDto(summaries);
    }

    [JsonRpcMethod("getBatchDetails")]
    public async Task<BatchDetailsDto> GetBatchDetailsAsync(string batchId, CancellationToken ct = default)
    {
        var batch = await _batchHistoryStore.GetBatchByIdAsync(
            Domain.ValueObjects.BatchId.Parse(batchId), ct).ConfigureAwait(false);

        if (batch is null)
        {
            return new BatchDetailsDto(batchId, "", "", "", default, null, [], []);
        }

        var datasets = batch.Datasets.Select(d => new DatasetDetailDto(
            d.TableId.FullName,
            d.FromLsn?.ToString(),
            d.ToLsn?.ToString(),
            d.Status.ToString().ToUpperInvariant(),
            d.RowCount,
            d.ChunkCount,
            d.ErrorCode,
            d.ErrorMessage
        )).ToList();

        return new BatchDetailsDto(
            batch.Id.ToString(),
            batch.Type.ToString().ToUpperInvariant(),
            batch.Trigger.ToString().ToUpperInvariant(),
            batch.Status.ToString().ToUpperInvariant(),
            batch.StartedAt,
            batch.FinishedAt,
            datasets,
            []);
    }

    [JsonRpcMethod("getTableStates")]
    public async Task<TableStatesDto> GetTableStatesAsync(CancellationToken ct = default)
    {
        var states = await _stateStore.GetAllTableStatesAsync(ct).ConfigureAwait(false);
        var dtos = states.Select(s => new TableStateDto(
            s.TableId.FullName,
            s.ExtractionMode.ToString().ToUpperInvariant(),
            CdcEnabled: s.CaptureInstance is not null,
            s.LastProcessedLsn.ToString(),
            s.BootstrapStatus.ToString().ToUpperInvariant(),
            s.LastSyncTime,
            Lag: s.LastSyncTime.HasValue
                ? (int)(DateTimeOffset.UtcNow - s.LastSyncTime.Value).TotalMinutes
                : 0,
            s.ErrorMessage
        )).ToList();

        return new TableStatesDto(dtos);
    }

    [JsonRpcMethod("triggerRun")]
    public Task<TriggerRunResultDto> TriggerRunAsync(CancellationToken ct = default)
    {
        // Stub — will be implemented in US5
        return Task.FromResult(new TriggerRunResultDto(false, null, "Manual trigger not yet implemented"));
    }

    [JsonRpcMethod("runDiagnostics")]
    public async Task<DiagnosticResultDto> RunDiagnosticsAsync(CancellationToken ct = default)
    {
        var checks = await _diagnosticsService.RunAllChecksAsync(ct).ConfigureAwait(false);
        var dtos = checks.Select(c => new DiagnosticCheckDto(
            c.Name,
            c.Category.ToString(),
            c.Status.ToString().ToUpperInvariant(),
            c.Detail,
            c.Remediation
        )).ToList();

        return new DiagnosticResultDto(dtos);
    }

    [JsonRpcMethod("subscribeLogs")]
    public Task<SubscribeResultDto> SubscribeLogsAsync(string minLevel, CancellationToken ct = default)
    {
        // Register with LogBroadcaster using instance-scoped subscriber ID.
        // The callback is a no-op for the MVP; full server→client streaming notifications
        // will require JsonRpc instance plumbing in IpcServer (separate concern).
        _logBroadcaster.Subscribe(_subscriberId, minLevel, _ => { });
        return Task.FromResult(new SubscribeResultDto(true));
    }

    [JsonRpcMethod("unsubscribeLogs")]
    public Task<UnsubscribeResultDto> UnsubscribeLogsAsync(CancellationToken ct = default)
    {
        var removed = _logBroadcaster.Unsubscribe(_subscriberId);
        return Task.FromResult(new UnsubscribeResultDto(removed));
    }

    [JsonRpcMethod("getRecentLogs")]
    public Task<RecentLogsDto> GetRecentLogsAsync(int count, string minLevel, CancellationToken ct = default)
    {
        var entries = _logBroadcaster.GetRecentLogs(count, minLevel);
        return Task.FromResult(new RecentLogsDto(entries.ToList()));
    }
}
