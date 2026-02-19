using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a new extraction batch begins.</summary>
public sealed record BatchStarted(BatchId BatchId, BatchType Type, BatchTrigger Trigger);
