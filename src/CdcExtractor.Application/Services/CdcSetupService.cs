using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Manages CDC enablement on the database and individual tables,
/// and configures retention settings.
/// </summary>
public sealed class CdcSetupService
{
    private readonly ICdcManager _cdcManager;
    private readonly CdcConfig _config;

    public CdcSetupService(ICdcManager cdcManager, CdcConfig config)
    {
        ArgumentNullException.ThrowIfNull(cdcManager);
        ArgumentNullException.ThrowIfNull(config);

        _cdcManager = cdcManager;
        _config = config;
    }

    /// <summary>
    /// Enables CDC on the database if configured and not already enabled.
    /// </summary>
    public async Task EnsureCdcOnDatabaseAsync(CancellationToken ct = default)
    {
        if (!_config.AutoEnableDatabase)
        {
            return;
        }

        var isEnabled = await _cdcManager.IsCdcEnabledOnDatabaseAsync(ct).ConfigureAwait(false);
        if (!isEnabled)
        {
            await _cdcManager.EnableCdcOnDatabaseAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Enables CDC on a specific table if configured and not already enabled.
    /// Returns the capture instance name.
    /// </summary>
    public async Task<string?> EnsureCdcOnTableAsync(
        TableIdentifier table, CancellationToken ct = default)
    {
        if (!_config.AutoEnableTables)
        {
            return null;
        }

        var isEnabled = await _cdcManager.IsCdcEnabledOnTableAsync(table, ct).ConfigureAwait(false);
        if (isEnabled)
        {
            return null; // Already enabled, capture instance already set
        }

        return await _cdcManager.EnableCdcOnTableAsync(table, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the CDC retention to the configured minimum, without lowering an
    /// existing higher value.
    /// </summary>
    public async Task EnsureRetentionAsync(CancellationToken ct = default)
    {
        var configuredMinutes = _config.RetentionMinDays * 24 * 60;
        var currentMinutes = await _cdcManager.GetRetentionMinutesAsync(ct).ConfigureAwait(false);

        if (currentMinutes < configuredMinutes)
        {
            await _cdcManager.SetRetentionMinutesAsync(configuredMinutes, ct).ConfigureAwait(false);
        }
    }
}
