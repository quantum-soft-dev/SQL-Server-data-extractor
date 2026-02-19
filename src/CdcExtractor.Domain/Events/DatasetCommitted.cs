using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a dataset is successfully committed to the downstream service.</summary>
public sealed record DatasetCommitted(DatasetId DatasetId, TableIdentifier Table, Lsn? FromLsn, Lsn? ToLsn, long RowCount);
