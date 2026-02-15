using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Entities;

/// <summary>
/// Aggregate root representing a single extraction run.
/// Contains one or more <see cref="DatasetRun"/> entries.
/// </summary>
public sealed class BatchRun
{
    private readonly List<DatasetRun> _datasets = [];

    public BatchId Id { get; }
    public string? RemoteBatchId { get; private set; }
    public BatchType Type { get; }
    public BatchTrigger Trigger { get; }
    public BatchStatus Status { get; private set; }
    public string? LeaseToken { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public IReadOnlyList<DatasetRun> Datasets => _datasets.AsReadOnly();

    private BatchRun(BatchId id, BatchType type, BatchTrigger trigger, DateTimeOffset startedAt)
    {
        Id = id;
        Type = type;
        Trigger = trigger;
        Status = BatchStatus.Running;
        StartedAt = startedAt;
    }

    /// <summary>
    /// Creates a new <see cref="BatchRun"/> with a generated ID and Running status.
    /// </summary>
    public static BatchRun Create(BatchType type, BatchTrigger trigger)
    {
        return new BatchRun(BatchId.New(), type, trigger, DateTimeOffset.UtcNow);
    }

    /// <summary>Sets the remote batch identifier returned by the downstream service.</summary>
    public void SetRemoteBatchId(string remoteBatchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteBatchId);
        RemoteBatchId = remoteBatchId;
    }

    /// <summary>Sets the lease token for heartbeat and commit operations.</summary>
    public void SetLeaseToken(string leaseToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);
        LeaseToken = leaseToken;
    }

    /// <summary>
    /// Adds a dataset to this batch. The batch must be in <see cref="BatchStatus.Running"/> status.
    /// </summary>
    public void AddDataset(DatasetRun dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (Status != BatchStatus.Running)
        {
            throw new InvalidOperationException(
                $"Cannot add dataset to batch in status '{Status}'. Batch must be '{BatchStatus.Running}'.");
        }

        _datasets.Add(dataset);
    }

    /// <summary>
    /// Finishes the batch with the given terminal status.
    /// </summary>
    public void Finish(BatchStatus status)
    {
        if (status == BatchStatus.Running)
        {
            throw new ArgumentException(
                $"Cannot finish a batch with status '{BatchStatus.Running}'. " +
                $"Use '{BatchStatus.Succeeded}', '{BatchStatus.Failed}', or '{BatchStatus.Aborted}'.",
                nameof(status));
        }

        if (FinishedAt.HasValue)
        {
            throw new InvalidOperationException("Batch is already finished.");
        }

        Status = status;
        FinishedAt = DateTimeOffset.UtcNow;
    }
}
