using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Manages CDC configuration on SQL Server databases and tables.
/// </summary>
public interface ICdcManager
{
    Task<bool> IsCdcEnabledOnDatabaseAsync(CancellationToken ct = default);
    Task EnableCdcOnDatabaseAsync(CancellationToken ct = default);
    Task<bool> IsCdcEnabledOnTableAsync(TableIdentifier table, CancellationToken ct = default);

    /// <summary>
    /// Enables CDC on the specified table and returns the capture instance name.
    /// </summary>
    Task<string> EnableCdcOnTableAsync(TableIdentifier table, CancellationToken ct = default);

    Task<int> GetRetentionMinutesAsync(CancellationToken ct = default);
    Task SetRetentionMinutesAsync(int minutes, CancellationToken ct = default);
    Task<bool> IsSqlAgentRunningAsync(CancellationToken ct = default);
}
