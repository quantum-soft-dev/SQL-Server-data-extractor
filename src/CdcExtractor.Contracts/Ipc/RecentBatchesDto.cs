namespace CdcExtractor.Contracts.Ipc;

public sealed record RecentBatchesDto(
    IReadOnlyList<BatchSummaryDto> Batches);

public sealed record BatchSummaryDto(
    string BatchId,
    string Type,
    string Trigger,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int TablesTotal,
    int TablesSucceeded,
    long TotalRows);
