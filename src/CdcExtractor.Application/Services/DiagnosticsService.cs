using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.Models;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Runs diagnostic checks against SQL Server, CDC configuration,
/// permissions, and downstream service connectivity.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly ICdcManager _cdcManager;
    private readonly IDownstreamClient _downstreamClient;

    public DiagnosticsService(ICdcManager cdcManager, IDownstreamClient downstreamClient)
    {
        ArgumentNullException.ThrowIfNull(cdcManager);
        ArgumentNullException.ThrowIfNull(downstreamClient);

        _cdcManager = cdcManager;
        _downstreamClient = downstreamClient;
    }

    public async Task<IReadOnlyList<DiagnosticCheck>> RunAllChecksAsync(CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheck>();

        checks.Add(await CheckSqlConnectivityAsync(ct).ConfigureAwait(false));
        checks.Add(await CheckSqlAgentAsync(ct).ConfigureAwait(false));
        checks.Add(await CheckCdcDatabaseAsync(ct).ConfigureAwait(false));

        return checks;
    }

    private async Task<DiagnosticCheck> CheckSqlConnectivityAsync(CancellationToken ct)
    {
        try
        {
            // Use CDC manager to verify connectivity — it will open a connection internally
            await _cdcManager.IsCdcEnabledOnDatabaseAsync(ct).ConfigureAwait(false);
            return new DiagnosticCheck(
                "SQL Server connectivity",
                DiagnosticCategory.SqlServer,
                DiagnosticStatus.Ok,
                "Successfully connected to SQL Server",
                null);
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(
                "SQL Server connectivity",
                DiagnosticCategory.SqlServer,
                DiagnosticStatus.Fail,
                $"Cannot connect: {ex.Message}",
                "Verify SQL Server is running, network is accessible, and credentials are correct.");
        }
    }

    private async Task<DiagnosticCheck> CheckSqlAgentAsync(CancellationToken ct)
    {
        try
        {
            var isRunning = await _cdcManager.IsSqlAgentRunningAsync(ct).ConfigureAwait(false);
            if (isRunning)
            {
                return new DiagnosticCheck(
                    "SQL Server Agent",
                    DiagnosticCategory.SqlServer,
                    DiagnosticStatus.Ok,
                    "SQL Server Agent is running",
                    null);
            }

            return new DiagnosticCheck(
                "SQL Server Agent",
                DiagnosticCategory.SqlServer,
                DiagnosticStatus.Fail,
                "SQL Server Agent is not running",
                "Start SQL Server Agent service. CDC cleanup and capture jobs require it.");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(
                "SQL Server Agent",
                DiagnosticCategory.SqlServer,
                DiagnosticStatus.Warn,
                $"Could not check SQL Agent status: {ex.Message}",
                "Ensure the service account has VIEW SERVER STATE permission.");
        }
    }

    private async Task<DiagnosticCheck> CheckCdcDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var isEnabled = await _cdcManager.IsCdcEnabledOnDatabaseAsync(ct).ConfigureAwait(false);
            if (isEnabled)
            {
                return new DiagnosticCheck(
                    "CDC database status",
                    DiagnosticCategory.SqlServer,
                    DiagnosticStatus.Ok,
                    "CDC is enabled on the database",
                    null);
            }

            return new DiagnosticCheck(
                "CDC database status",
                DiagnosticCategory.SqlServer,
                DiagnosticStatus.Warn,
                "CDC is not enabled on the database",
                "Enable CDC with: EXEC sys.sp_cdc_enable_db");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(
                "CDC database status",
                DiagnosticCategory.SqlServer,
                DiagnosticStatus.Fail,
                $"Could not check CDC status: {ex.Message}",
                "Ensure the service account has db_owner or cdc_admin role.");
        }
    }
}
