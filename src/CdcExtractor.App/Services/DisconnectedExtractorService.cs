using CdcExtractor.Contracts.Ipc;

namespace CdcExtractor.App.Services;

/// <summary>
/// Stub implementation of IExtractorService used when the IPC client is not connected.
/// Returns safe defaults so Manager pages can render without crashing.
/// </summary>
internal sealed class DisconnectedExtractorService : IExtractorService
{
    private static readonly string NotConnected = "Service not connected";

    public Task<ServiceStatusDto> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new ServiceStatusDto(
            IsRunning: false, null, null, null, null, null,
            ServiceUptime: "--", ServicePid: 0));

    public Task<BatchProgressDto> GetBatchProgressAsync(CancellationToken ct = default) =>
        Task.FromResult(new BatchProgressDto(null, []));

    public Task<RecentBatchesDto> GetRecentBatchesAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult(new RecentBatchesDto([]));

    public Task<BatchDetailsDto> GetBatchDetailsAsync(string batchId, CancellationToken ct = default) =>
        Task.FromResult(new BatchDetailsDto(batchId, "", "", "", default, null, [], []));

    public Task<TableStatesDto> GetTableStatesAsync(CancellationToken ct = default) =>
        Task.FromResult(new TableStatesDto([]));

    public Task<TriggerRunResultDto> TriggerRunAsync(CancellationToken ct = default) =>
        Task.FromResult(new TriggerRunResultDto(false, null, NotConnected));

    public Task<DiagnosticResultDto> RunDiagnosticsAsync(CancellationToken ct = default) =>
        Task.FromResult(new DiagnosticResultDto([]));

    public Task<SubscribeResultDto> SubscribeLogsAsync(string minLevel, CancellationToken ct = default) =>
        Task.FromResult(new SubscribeResultDto(false));

    public Task<UnsubscribeResultDto> UnsubscribeLogsAsync(CancellationToken ct = default) =>
        Task.FromResult(new UnsubscribeResultDto(false));

    public Task<RecentLogsDto> GetRecentLogsAsync(int count, string minLevel, CancellationToken ct = default) =>
        Task.FromResult(new RecentLogsDto([]));
}
