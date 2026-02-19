namespace CdcExtractor.Contracts.Ipc;

public sealed record DiagnosticResultDto(
    IReadOnlyList<DiagnosticCheckDto> Checks);
