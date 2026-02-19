using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a dataset extraction or upload fails.</summary>
public sealed record DatasetFailed(DatasetId DatasetId, TableIdentifier Table, string ErrorCode);
