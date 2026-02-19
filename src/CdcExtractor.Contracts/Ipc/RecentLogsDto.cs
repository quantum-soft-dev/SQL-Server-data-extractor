namespace CdcExtractor.Contracts.Ipc;

public sealed record RecentLogsDto(
    IReadOnlyList<LogEntryDto> Entries);
