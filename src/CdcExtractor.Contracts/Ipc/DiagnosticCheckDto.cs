namespace CdcExtractor.Contracts.Ipc;

public sealed record DiagnosticCheckDto(
    string Name,
    string Category,
    string Status,
    string Detail,
    string? Remediation);
