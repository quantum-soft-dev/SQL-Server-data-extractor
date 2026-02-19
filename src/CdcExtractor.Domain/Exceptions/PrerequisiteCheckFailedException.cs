namespace CdcExtractor.Domain.Exceptions;

/// <summary>
/// Thrown when a prerequisite check (e.g., SQL Server CDC enabled, permissions) fails.
/// </summary>
public sealed class PrerequisiteCheckFailedException : Exception
{
    public string CheckName { get; }
    public string Detail { get; }
    public string? Remediation { get; }

    public PrerequisiteCheckFailedException(string checkName, string detail, string? remediation)
        : base($"Prerequisite check '{checkName}' failed: {detail}")
    {
        CheckName = checkName;
        Detail = detail;
        Remediation = remediation;
    }

    public PrerequisiteCheckFailedException(string checkName, string detail, string? remediation, Exception innerException)
        : base($"Prerequisite check '{checkName}' failed: {detail}", innerException)
    {
        CheckName = checkName;
        Detail = detail;
        Remediation = remediation;
    }
}
