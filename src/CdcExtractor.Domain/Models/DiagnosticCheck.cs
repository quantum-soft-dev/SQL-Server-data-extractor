namespace CdcExtractor.Domain.Models;

/// <summary>
/// Result of a single diagnostic check.
/// </summary>
public sealed record DiagnosticCheck(
    string Name,
    DiagnosticCategory Category,
    DiagnosticStatus Status,
    string Detail,
    string? Remediation);

/// <summary>Categorizes diagnostic checks by subsystem.</summary>
public enum DiagnosticCategory { SqlServer, Permissions, Downstream, Ipc }

/// <summary>Outcome of a diagnostic check.</summary>
public enum DiagnosticStatus { Ok, Warn, Fail }
