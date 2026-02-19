using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a table is flagged for re-bootstrap (e.g., due to a CDC gap).</summary>
public sealed record TableReBootstrapFlagged(TableIdentifier Table, string Reason);
