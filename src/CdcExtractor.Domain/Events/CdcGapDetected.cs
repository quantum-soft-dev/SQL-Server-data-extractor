using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a gap is detected in the CDC LSN sequence for a table.</summary>
public sealed record CdcGapDetected(TableIdentifier Table, Lsn ExpectedLsn, Lsn MinAvailableLsn);
