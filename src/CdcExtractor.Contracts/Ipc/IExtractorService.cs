namespace CdcExtractor.Contracts.Ipc;

public interface IExtractorService
{
    Task<ServiceStatusDto> GetStatusAsync(CancellationToken ct = default);
    Task<BatchProgressDto> GetBatchProgressAsync(CancellationToken ct = default);
    Task<RecentBatchesDto> GetRecentBatchesAsync(int limit, CancellationToken ct = default);
    Task<BatchDetailsDto> GetBatchDetailsAsync(string batchId, CancellationToken ct = default);
    Task<TableStatesDto> GetTableStatesAsync(CancellationToken ct = default);
    Task<TriggerRunResultDto> TriggerRunAsync(CancellationToken ct = default);
    Task<DiagnosticResultDto> RunDiagnosticsAsync(CancellationToken ct = default);
    Task<SubscribeResultDto> SubscribeLogsAsync(string minLevel, CancellationToken ct = default);
    Task<UnsubscribeResultDto> UnsubscribeLogsAsync(CancellationToken ct = default);
    Task<RecentLogsDto> GetRecentLogsAsync(int count, string minLevel, CancellationToken ct = default);
}
