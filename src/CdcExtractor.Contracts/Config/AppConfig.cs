namespace CdcExtractor.Contracts.Config;

public sealed record AppConfig
{
    public SqlServerConfig SqlServer { get; init; } = new();
    public DownstreamConfig Downstream { get; init; } = new();
    public ScheduleConfig Schedule { get; init; } = new();
    public CdcConfig Cdc { get; init; } = new();
    public ExtractionConfig Extraction { get; init; } = new();
}
