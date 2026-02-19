namespace CdcExtractor.Contracts.Ipc;

public sealed record TableStatesDto(
    IReadOnlyList<TableStateDto> Tables);

public sealed record TableStateDto(
    string Table,
    string Mode,
    bool CdcEnabled,
    string? LastProcessedLsn,
    string BootstrapStatus,
    DateTimeOffset? LastSyncTime,
    int Lag,
    string? Error);
