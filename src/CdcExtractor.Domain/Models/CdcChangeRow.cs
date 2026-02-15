namespace CdcExtractor.Domain.Models;

/// <summary>
/// Represents a single CDC change row read from SQL Server change tables.
/// </summary>
public sealed record CdcChangeRow(
    string Operation,
    string Lsn,
    string SeqVal,
    DateTimeOffset Timestamp,
    Dictionary<string, object?> Values);
