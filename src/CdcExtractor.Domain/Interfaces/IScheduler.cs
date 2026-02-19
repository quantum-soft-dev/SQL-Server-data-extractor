namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Controls extraction scheduling and manual trigger support.
/// </summary>
public interface IScheduler
{
    Task<DateTimeOffset?> GetNextRunTimeAsync(CancellationToken ct = default);
    Task<bool> IsRunningAsync(CancellationToken ct = default);
    Task TriggerManualRunAsync(CancellationToken ct = default);
}
