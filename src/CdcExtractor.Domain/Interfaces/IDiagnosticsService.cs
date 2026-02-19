using CdcExtractor.Domain.Models;

namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Runs diagnostic checks against SQL Server, permissions, downstream service, and IPC.
/// </summary>
public interface IDiagnosticsService
{
    Task<IReadOnlyList<DiagnosticCheck>> RunAllChecksAsync(CancellationToken ct = default);
}
