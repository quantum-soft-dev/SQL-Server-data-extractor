namespace CdcExtractor.Contracts.Ipc;

public sealed record ServiceStatusDto(
    bool IsRunning,
    string? CurrentBatchId,
    string? CurrentBatchType,
    string? CurrentBatchTrigger,
    DateTimeOffset? CurrentBatchStartedAt,
    DateTimeOffset? NextScheduledRun,
    string ServiceUptime,
    int ServicePid);
