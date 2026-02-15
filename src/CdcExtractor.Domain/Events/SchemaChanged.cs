using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a table's schema hash changes between extraction runs.</summary>
public sealed record SchemaChanged(TableIdentifier Table, SchemaHash OldHash, SchemaHash NewHash);
