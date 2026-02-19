namespace CdcExtractor.Domain.Models;

/// <summary>
/// Represents a single row of data read from a full table scan.
/// </summary>
public sealed record DataRow(Dictionary<string, object?> Values);
