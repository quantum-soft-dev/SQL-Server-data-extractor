namespace CdcExtractor.Contracts.Ipc;

public sealed record BatchProgressDto(
    string? BatchId,
    IReadOnlyList<TableProgressDto> Tables);

public sealed record TableProgressDto(
    string Table,
    string Status,
    int ProgressPercent,
    long RowsProcessed,
    long RowsTotal,
    int ChunksUploaded,
    int ChunksTotal);
