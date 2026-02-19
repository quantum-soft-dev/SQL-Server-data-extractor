namespace CdcExtractor.Contracts.Ipc;

public sealed record BatchDetailsDto(
    string BatchId,
    string Type,
    string Trigger,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<DatasetDetailDto> Datasets,
    IReadOnlyList<BatchErrorDto> Errors);

public sealed record DatasetDetailDto(
    string Table,
    string? FromLsn,
    string? ToLsn,
    string Status,
    long RowCount,
    int ChunkCount,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record BatchErrorDto(
    DateTimeOffset OccurredAt,
    string Scope,
    string? Table,
    string Severity,
    string Code,
    string Message);
