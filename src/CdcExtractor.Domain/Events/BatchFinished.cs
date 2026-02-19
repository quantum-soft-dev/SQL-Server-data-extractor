using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a batch completes (succeeded, failed, or aborted).</summary>
public sealed record BatchFinished(BatchId BatchId, BatchStatus Status, TimeSpan Duration);
