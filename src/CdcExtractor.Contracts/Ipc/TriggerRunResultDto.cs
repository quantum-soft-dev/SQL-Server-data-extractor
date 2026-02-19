namespace CdcExtractor.Contracts.Ipc;

public sealed record TriggerRunResultDto(
    bool Accepted,
    string? BatchId,
    string? Reason);
