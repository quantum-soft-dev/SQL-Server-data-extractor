using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Interfaces;

namespace CdcExtractor.Service.Workers;

/// <summary>
/// Background service that sends periodic heartbeats to the downstream service
/// while a batch is active. Handles 409 lease conflict by signaling batch abort.
/// </summary>
public sealed class HeartbeatWorker : IHeartbeatCoordinator, IDisposable
{
    private readonly IDownstreamClient _downstreamClient;
    private readonly int _intervalSeconds;
    private readonly ILogger<HeartbeatWorker> _logger;

    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private volatile string? _batchId;
    private volatile string? _leaseToken;

    /// <summary>
    /// Raised when a 409 lease conflict is received, indicating the batch has been superseded.
    /// </summary>
    public event Action? LeaseConflictDetected;

    public HeartbeatWorker(
        IDownstreamClient downstreamClient,
        DownstreamConfig config,
        ILogger<HeartbeatWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(downstreamClient);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _downstreamClient = downstreamClient;
        _intervalSeconds = config.HeartbeatIntervalSeconds > 0
            ? config.HeartbeatIntervalSeconds
            : 120;
        _logger = logger;
    }

    /// <summary>
    /// Starts sending heartbeats for the given batch.
    /// </summary>
    public void StartHeartbeat(string batchId, string leaseToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);

        StopHeartbeat();

        _batchId = batchId;
        _leaseToken = leaseToken;
        _cts = new CancellationTokenSource();

        _heartbeatTask = RunHeartbeatLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stops sending heartbeats for the current batch.
    /// </summary>
    public void StopHeartbeat()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _heartbeatTask = null;
        _batchId = null;
        _leaseToken = null;
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "Heartbeat started for batch {BatchId}, interval {Interval}s",
            _batchId, _intervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            var currentBatchId = _batchId;
            var currentLeaseToken = _leaseToken;
            if (currentBatchId is null || currentLeaseToken is null)
                break;

            try
            {
                await _downstreamClient.HeartbeatAsync(currentBatchId, currentLeaseToken, ct)
                    .ConfigureAwait(false);

                _logger.LogDebug("Heartbeat sent for batch {BatchId}", currentBatchId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Domain.Exceptions.SinkUploadException ex) when (ex.HttpStatusCode == 409)
            {
                _logger.LogWarning(
                    "Lease conflict (409) detected for batch {BatchId}. Batch has been superseded.",
                    _batchId);

                LeaseConflictDetected?.Invoke();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Heartbeat failed for batch {BatchId}. " +
                    "Will retry in {Interval}s. If persistent, check downstream connectivity.",
                    _batchId, _intervalSeconds);
            }
        }

        _logger.LogInformation("Heartbeat stopped for batch {BatchId}", _batchId);
    }

    public void Dispose()
    {
        StopHeartbeat();
    }
}
