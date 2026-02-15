namespace CdcExtractor.Domain.Events;

/// <summary>Raised when a prerequisite check fails during startup or diagnostics.</summary>
public sealed record PrerequisiteCheckFailed(string CheckName, string Detail, string? Remediation);
