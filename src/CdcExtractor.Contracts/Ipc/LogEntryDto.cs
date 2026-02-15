namespace CdcExtractor.Contracts.Ipc;

public sealed record LogEntryDto(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? CorrelationId,
    string? BatchId,
    string? Table,
    string? DatasetId,
    Dictionary<string, object?>? Properties);
