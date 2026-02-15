using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Enums;
using Cronos;

namespace CdcExtractor.Service.Workers;

/// <summary>
/// BackgroundService that schedules extraction runs based on cron expressions.
/// Uses Cronos for cron parsing and a SemaphoreSlim to prevent parallel runs.
/// Supports manual triggering via <see cref="TriggerManualRunAsync"/>.
/// </summary>
public sealed class SchedulerWorker : BackgroundService
{
    private readonly ExtractionOrchestrator _orchestrator;
    private readonly ScheduleConfig _scheduleConfig;
    private readonly ILogger<SchedulerWorker> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public SchedulerWorker(
        ExtractionOrchestrator orchestrator,
        ScheduleConfig scheduleConfig,
        ILogger<SchedulerWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(scheduleConfig);
        ArgumentNullException.ThrowIfNull(logger);

        _orchestrator = orchestrator;
        _scheduleConfig = scheduleConfig;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if a batch is currently running.
    /// </summary>
    public bool IsRunning => _runLock.CurrentCount == 0;

    /// <summary>
    /// Returns the next scheduled run time based on all configured cron expressions.
    /// </summary>
    public DateTimeOffset? GetNextRunTime()
    {
        return CalculateNextRunTime(
            _scheduleConfig.CronExpressions, _scheduleConfig.Timezone, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Calculates the next run time from a set of 6-field cron expression strings.
    /// </summary>
    public static DateTimeOffset? CalculateNextRunTime(
        IReadOnlyList<string> cronExpressions,
        string timezone,
        DateTimeOffset now)
    {
        if (cronExpressions.Count == 0)
        {
            return null;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        DateTimeOffset? earliest = null;

        foreach (var expr in cronExpressions)
        {
            var cron = CronExpression.Parse(expr, CronFormat.IncludeSeconds);
            var next = cron.GetNextOccurrence(now.UtcDateTime, tz);
            if (next.HasValue)
            {
                var nextOffset = new DateTimeOffset(next.Value, TimeSpan.Zero);
                if (earliest is null || nextOffset < earliest)
                {
                    earliest = nextOffset;
                }
            }
        }

        return earliest;
    }

    /// <summary>
    /// Triggers a manual batch run. Returns whether it was accepted and the batch ID or rejection reason.
    /// </summary>
    public async Task<(bool Accepted, string? BatchId, string? Reason)> TriggerManualRunAsync(
        CancellationToken ct = default)
    {
        if (!_runLock.Wait(0))
        {
            return (false, null, "A batch is already running");
        }

        try
        {
            _logger.LogInformation("Manual batch run triggered");

            var batch = await _orchestrator.RunDeltaBatchAsync(BatchTrigger.Manual, ct)
                .ConfigureAwait(false);

            return (true, batch.Id.Value.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual batch run failed");
            return (true, null, $"Batch failed: {ex.Message}");
        }
        finally
        {
            _runLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SchedulerWorker started with {Count} cron expressions, timezone {Timezone}",
            _scheduleConfig.CronExpressions.Count, _scheduleConfig.Timezone);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = CalculateNextRunTime(
                _scheduleConfig.CronExpressions, _scheduleConfig.Timezone, DateTimeOffset.UtcNow);

            if (nextRun is null)
            {
                _logger.LogWarning("No cron expressions configured. SchedulerWorker idle.");
                // Wait and re-check periodically in case config changes
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
                continue;
            }

            var delay = nextRun.Value - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "Next scheduled run at {NextRun} (in {Delay})",
                    nextRun.Value, delay);

                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Single-instance lock — skip if a run is already in progress
            if (!_runLock.Wait(0))
            {
                _logger.LogWarning("Scheduled run skipped — a batch is already running");
                continue;
            }

            try
            {
                _logger.LogInformation("Starting scheduled extraction batch");

                await _orchestrator.RunDeltaBatchAsync(BatchTrigger.Scheduled, stoppingToken)
                    .ConfigureAwait(false);

                _logger.LogInformation("Scheduled extraction batch completed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Scheduled batch cancelled due to shutdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled extraction batch failed");
            }
            finally
            {
                _runLock.Release();
            }
        }

        _logger.LogInformation("SchedulerWorker stopped");
    }

    public override void Dispose()
    {
        _runLock.Dispose();
        base.Dispose();
    }
}
